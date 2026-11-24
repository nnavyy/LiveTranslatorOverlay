using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Translation;
using NAudio.Wave;
using Whisper.net.LibraryLoader;

internal static class ContinuousDualModelBenchmark
{
    private sealed record TierClipResult(
        string Tier,
        string Id,
        string Language,
        double AudioSeconds,
        double DecodeMs,
        double Rtf,
        string Metric,
        double ErrorPct,
        string Reference,
        string Hypothesis);

    private sealed record TierSummary(
        string Tier,
        string ModelLabel,
        string BackendLabel,
        bool UsingGpu,
        double InitMs,
        double MeanDecodeMs,
        double P50DecodeMs,
        double P95DecodeMs,
        double MeanRtf,
        double MixedErrorPct,
        object[] PerLanguage,
        List<TierClipResult> Clips);

    private sealed record ContinuousStreamResult(
        string Language,
        double TotalSpeechSeconds,
        int ClausesEmitted,
        double MeanClauseAudioSeconds,
        double MaxClauseAudioSeconds,
        double MeanAsrMs,
        double MeanMtMs,
        string Metric,
        double StreamErrorPct,
        string ReferenceJoined,
        string HypothesisJoined,
        string TranslationJoined);

    public static async Task RunAsync(string manifestPath, string? outJsonPath)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var allRows = File.ReadLines(manifestPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line))
            .Where(row => row.GetProperty("split").GetString() == "test"
                       && row.GetProperty("kind").GetString() == "speech"
                       && row.GetProperty("seconds").GetDouble() is >= 3.5 and <= 11.5)
            .ToArray();

        var selected = new[] { "en", "id", "ja", "ru" }
            .SelectMany(lang => allRows
                .Where(r => r.GetProperty("language").GetString() == lang)
                .OrderBy(r => r.GetProperty("id").GetString(), StringComparer.Ordinal)
                .Take(3))
            .Select(r => (
                Id: r.GetProperty("id").GetString()!,
                Language: r.GetProperty("language").GetString()!,
                Seconds: r.GetProperty("seconds").GetDouble(),
                Reference: r.GetProperty("reference").GetString()!,
                Audio: ReadAudio16k(Path.Combine(root, r.GetProperty("path").GetString()!))
            ))
            .ToArray();

        if (selected.Length == 0)
            throw new InvalidOperationException("No held-out test clips found in manifest: " + manifestPath);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(25));
        var token = timeout.Token;

        var tierSummaries = new List<TierSummary>();
        foreach (var tier in new[] { LocalSpeechModelTier.Small, LocalSpeechModelTier.LargeTurbo })
        {
            Console.WriteLine($"\n=== Benchmarking ASR Model Tier: {tier} (GPU enabled) ===");
            var clipResults = new List<TierClipResult>(selected.Length);
            double initMs = 0;
            string modelLabel = "";
            string backendLabel = "";
            bool usingGpu = false;

            foreach (var group in selected.GroupBy(c => c.Language))
            {
                string lang = group.Key;
                await using var service = new LocalSpeechService(tier, allowGpu: true);
                var initSw = Stopwatch.StartNew();
                await service.InitializeAsync(lang, token);
                initSw.Stop();
                if (initMs == 0) initMs = initSw.Elapsed.TotalMilliseconds;
                modelLabel = service.ActiveModelLabel;
                backendLabel = service.BackendLabel;
                usingGpu = service.UsingGpu;

                foreach (var clip in group)
                {
                    var sw = Stopwatch.StartNew();
                    string hyp = await service.TranscribeAsync(clip.Audio, token);
                    sw.Stop();

                    bool useCer = lang is "ja" or "zh" or "zh-TW";
                    string metric = useCer ? "CER" : "WER";
                    double errPct = (useCer
                        ? CharacterErrorRate(clip.Reference, hyp)
                        : WordErrorRate(clip.Reference, hyp)) * 100.0;
                    double rtf = sw.Elapsed.TotalSeconds / Math.Max(0.1, clip.Seconds);

                    clipResults.Add(new TierClipResult(
                        tier.ToString(),
                        clip.Id,
                        lang,
                        Math.Round(clip.Seconds, 2),
                        Math.Round(sw.Elapsed.TotalMilliseconds, 1),
                        Math.Round(rtf, 3),
                        metric,
                        Math.Round(errPct, 2),
                        clip.Reference,
                        hyp));

                    Console.WriteLine($"  [{tier} · {backendLabel} · {lang}] {clip.Id} ({clip.Seconds:F2}s) -> {sw.Elapsed.TotalMilliseconds:F1}ms (RTF={rtf:F3}) | {metric}={errPct:F2}% | \"{hyp}\"");
                }
            }

            var decodeLatencies = clipResults.Select(c => c.DecodeMs).ToList();
            var perLang = clipResults.GroupBy(c => c.Language).Select(g => (object)new
            {
                language = g.Key,
                metric = g.First().Metric,
                meanErrorPct = Math.Round(g.Average(x => x.ErrorPct), 2),
                meanDecodeMs = Math.Round(g.Average(x => x.DecodeMs), 1),
                meanRtf = Math.Round(g.Average(x => x.Rtf), 3)
            }).ToArray();

            var tierSummary = new TierSummary(
                tier.ToString(),
                modelLabel,
                backendLabel,
                usingGpu,
                Math.Round(initMs, 1),
                Math.Round(decodeLatencies.Average(), 1),
                Math.Round(Percentile(decodeLatencies, 50), 1),
                Math.Round(Percentile(decodeLatencies, 95), 1),
                Math.Round(clipResults.Average(c => c.Rtf), 3),
                Math.Round(clipResults.Average(c => c.ErrorPct), 2),
                perLang,
                clipResults);
            tierSummaries.Add(tierSummary);

            Console.WriteLine($"  >> {tier} Summary ({backendLabel}): MixedError={tierSummary.MixedErrorPct:F2}%, Mean={tierSummary.MeanDecodeMs:F1}ms, P50={tierSummary.P50DecodeMs:F1}ms, P95={tierSummary.P95DecodeMs:F1}ms, RTF={tierSummary.MeanRtf:F3}");
        }

        Console.WriteLine("\n=== Benchmarking Continuous Unbroken Nonstop Speech Stream (Zero Dead Air) ===");
        string localSpeechJson = Path.Combine(AppSettings.DataDirectory, "local-speech.json");
        using var localMt = File.Exists(localSpeechJson) ? new LocalTranslateProvider() : null;
        if (localMt != null)
            await localMt.WarmUpAsync(token);

        var continuousResults = new List<ContinuousStreamResult>();
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        foreach (var group in selected.GroupBy(c => c.Language))
        {
            string lang = group.Key;
            string tgtLang = lang == "en" ? "id" : "en";

            // Concatenate 3 clips with leading/trailing silence stripped and only a 90 ms natural breath between sentences
            // so normal 480 ms pause detection NEVER triggers — forcing Tier 2 micro-pause / Tier 3 valley splitting!
            var streamSamples = new List<float>();
            var refParts = new List<string>();
            foreach (var clip in group)
            {
                var trimmed = StripOuterSilence(clip.Audio);
                if (streamSamples.Count > 0)
                    streamSamples.AddRange(new float[16000 * 90 / 1000]); // 90 ms tiny breath (< 480 ms normal pause!)
                streamSamples.AddRange(trimmed);
                refParts.Add(clip.Reference.Trim());
            }

            double totalSpeechSec = streamSamples.Count / 16000.0;
            await using var asr = new LocalSpeechService(LocalSpeechModelTier.LargeTurbo, allowGpu: true);
            await asr.InitializeAsync(lang, token);

            var capture = new AudioCaptureService
            {
                EnableStreamingWindows = true,
                EmitInterimWindows = false,
                SentenceMinSpeechMs = 1500,
                StreamingSilenceCommitMs = 520,
                ContinuousClauseMs = 4200,
                StreamingMaxWindowMs = 6600
            };

            var audioQueue = (ConcurrentQueue<float>)typeof(AudioCaptureService).GetField("_audioBuffer", PrivateInstance)!.GetValue(capture)!;
            var clauses = new List<(double AudioSec, double AsrMs, string DedupOriginal)>();
            string prevRaw = "";

            capture.StreamingWindowHandler = async (chunk, isFinal) =>
            {
                if (!isFinal) return;
                var asrSw = Stopwatch.StartNew();
                string raw = await asr.TranscribeAsync(chunk, token);
                asrSw.Stop();
                raw = HallucinationDetector.StripWhisperArtifacts(raw);
                if (string.IsNullOrWhiteSpace(raw)) return;

                string dedup = SentenceBoundaryDeduplicator.StripOverlapPrefix(prevRaw, raw);
                prevRaw = raw;
                if (string.IsNullOrWhiteSpace(dedup) || HallucinationDetector.IsHallucination(dedup)) return;

                clauses.Add((chunk.Length / 16000.0, asrSw.Elapsed.TotalMilliseconds, dedup));
            };

            typeof(AudioCaptureService).GetField("_isCapturing", PrivateInstance)!.SetValue(capture, true);
            var loopTask = (Task)typeof(AudioCaptureService).GetMethod("ChunkEmitterLoop", PrivateInstance)!.Invoke(capture, null)!;

            // Stream in 80 ms (1280-sample) packets with realistic pacing (~1.6x real-time) so emitter loop slices naturally
            const int packetSamples = 1280;
            for (int pos = 0; pos < streamSamples.Count; pos += packetSamples)
            {
                int count = Math.Min(packetSamples, streamSamples.Count - pos);
                for (int i = 0; i < count; i++)
                    audioQueue.Enqueue(streamSamples[pos + i]);
                await Task.Delay(45, token);
            }

            // Trailing 700 ms silence at end of speech so the final clause flushes cleanly
            for (int i = 0; i < 16000 * 700 / 1000; i++)
                audioQueue.Enqueue(0f);
            while (!audioQueue.IsEmpty)
                await Task.Delay(40, token);
            await Task.Delay(750, token);
            capture.Stop();
            await loopTask.WaitAsync(TimeSpan.FromSeconds(10));

            // Translate each deduplicated clause asynchronously (matching MainWindow's decoupled finalQueue MT worker)
            var fullClauses = new List<(double AudioSec, double AsrMs, double MtMs, string DedupOriginal, string Translated)>();
            foreach (var c in clauses)
            {
                double mtMs = 0;
                string trans = "";
                if (localMt != null)
                {
                    var mtSw = Stopwatch.StartNew();
                    trans = await localMt.TranslateAsync(c.DedupOriginal, tgtLang, lang);
                    mtSw.Stop();
                    mtMs = mtSw.Elapsed.TotalMilliseconds;
                }
                fullClauses.Add((c.AudioSec, c.AsrMs, mtMs, c.DedupOriginal, trans));
                Console.WriteLine($"  [Nonstop {lang}->{tgtLang} clause #{fullClauses.Count}] win={c.AudioSec:F2}s | ASR={c.AsrMs:F0}ms | MT={mtMs:F0}ms | \"{c.DedupOriginal}\" -> \"{trans}\"");
            }

            bool useCer = lang is "ja" or "zh" or "zh-TW";
            string joinSep = useCer ? "" : " ";
            string refJoined = string.Join(joinSep, refParts);
            string hypJoined = string.Join(joinSep, fullClauses.Select(c => c.DedupOriginal));
            string transJoined = string.Join(" ", fullClauses.Select(c => c.Translated).Where(t => !string.IsNullOrWhiteSpace(t)));
            double streamErrPct = (useCer
                ? CharacterErrorRate(refJoined, hypJoined)
                : WordErrorRate(refJoined, hypJoined)) * 100.0;

            continuousResults.Add(new ContinuousStreamResult(
                lang,
                Math.Round(totalSpeechSec, 2),
                fullClauses.Count,
                fullClauses.Count > 0 ? Math.Round(fullClauses.Average(c => c.AudioSec), 2) : 0,
                fullClauses.Count > 0 ? Math.Round(fullClauses.Max(c => c.AudioSec), 2) : 0,
                fullClauses.Count > 0 ? Math.Round(fullClauses.Average(c => c.AsrMs), 1) : 0,
                fullClauses.Count > 0 ? Math.Round(fullClauses.Average(c => c.MtMs), 1) : 0,
                useCer ? "CER" : "WER",
                Math.Round(streamErrPct, 2),
                refJoined,
                hypJoined,
                transJoined));

            Console.WriteLine($"  >> Nonstop {lang} ({totalSpeechSec:F2}s unbroken): clauses={fullClauses.Count}, meanWin={fullClauses.Average(c => c.AudioSec):F2}s, ASR={fullClauses.Average(c => c.AsrMs):F1}ms, MT={fullClauses.Average(c => c.MtMs):F1}ms, {(useCer ? "CER" : "WER")}={streamErrPct:F2}%");
        }

        // Multi-sentence MT batching & anti-repetition check
        object? mtBatchProbe = null;
        if (localMt != null)
        {
            string multiSentenceEn = "This is an extraordinary story from an extraordinary recording. Today we are testing continuous speech translation without pauses. Every clause must stay accurate and fast.";
            var mtSw = Stopwatch.StartNew();
            string multiTranslatedId = await localMt.TranslateAsync(multiSentenceEn, "id", "en");
            mtSw.Stop();
            mtBatchProbe = new
            {
                elapsedMs = Math.Round(mtSw.Elapsed.TotalMilliseconds, 1),
                source = multiSentenceEn,
                translated = multiTranslatedId
            };
            Console.WriteLine($"\n=== Multi-Sentence M2M-100 Batch Probe ===\n  EN->ID (3 sentences): {mtSw.Elapsed.TotalMilliseconds:F1}ms | \"{multiTranslatedId}\"");
        }

        var report = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            loadedLibrary = RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown",
            asrTiers = tierSummaries,
            continuousNonstopStreams = new
            {
                meanClausesPerStream = Math.Round(continuousResults.Average(c => c.ClausesEmitted), 2),
                meanClauseWindowSec = Math.Round(continuousResults.Average(c => c.MeanClauseAudioSeconds), 2),
                maxClauseWindowSec = Math.Round(continuousResults.Max(c => c.MaxClauseAudioSeconds), 2),
                meanAsrMs = Math.Round(continuousResults.Average(c => c.MeanAsrMs), 1),
                meanMtMs = Math.Round(continuousResults.Average(c => c.MeanMtMs), 1),
                meanMixedErrorPct = Math.Round(continuousResults.Average(c => c.StreamErrorPct), 2),
                streams = continuousResults
            },
            mtMultiSentenceBatchProbe = mtBatchProbe
        };

        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrWhiteSpace(outJsonPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outJsonPath))!);
            File.WriteAllText(outJsonPath, json, Encoding.UTF8);
            Console.WriteLine($"\nWrote continuous dual-model benchmark report: {outJsonPath}");
        }
    }

    private static float[] StripOuterSilence(float[] samples)
    {
        const int win = 800; // 50 ms
        float peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float a = Math.Abs(samples[i]);
            if (a > peak) peak = a;
        }
        float gate = Math.Clamp(peak * 0.04f, 0.0006f, 0.003f);

        int start = 0;
        while (start + win < samples.Length)
        {
            float sum = 0;
            for (int i = 0; i < win; i++) sum += Math.Abs(samples[start + i]);
            if (sum / win >= gate) break;
            start += win / 2;
        }
        int end = samples.Length;
        while (end - win > start)
        {
            float sum = 0;
            for (int i = 0; i < win; i++) sum += Math.Abs(samples[end - win + i]);
            if (sum / win >= gate) break;
            end -= win / 2;
        }
        start = Math.Max(0, start - 800);
        end = Math.Min(samples.Length, end + 800);
        return samples[start..end];
    }

    private static float[] ReadAudio16k(string path)
    {
        using var reader = new WaveFileReader(path);
        var samples = new float[reader.Length / reader.WaveFormat.BlockAlign];
        var source = reader.ToSampleProvider();
        int offset = 0;
        while (offset < samples.Length)
        {
            int read = source.Read(samples.AsSpan(offset));
            if (read == 0) break;
            offset += read;
        }
        return offset == samples.Length ? samples : samples[..offset];
    }

    private static double Percentile(List<double> values, double pct)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        double pos = (pct / 100.0) * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        return lo == hi ? sorted[lo] : sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    private static double WordErrorRate(string expected, string actual)
    {
        static string[] Words(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(m => m.Value.Replace('ё', 'е')).ToArray();
        return LevenshteinRate(Words(expected), Words(actual));
    }

    private static double CharacterErrorRate(string expected, string actual)
    {
        static char[] Chars(string text) => text.ToLowerInvariant()
            .Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray();
        return LevenshteinRate(Chars(expected), Chars(actual));
    }

    private static double LevenshteinRate<T>(T[] reference, T[] hypothesis) where T : notnull
    {
        var comparer = EqualityComparer<T>.Default;
        var costs = Enumerable.Range(0, hypothesis.Length + 1).ToArray();
        for (int i = 1; i <= reference.Length; i++)
        {
            int diagonal = costs[0];
            costs[0] = i;
            for (int j = 1; j <= hypothesis.Length; j++)
            {
                int previous = costs[j];
                costs[j] = Math.Min(Math.Min(costs[j] + 1, costs[j - 1] + 1),
                    diagonal + (comparer.Equals(reference[i - 1], hypothesis[j - 1]) ? 0 : 1));
                diagonal = previous;
            }
        }
        return (double)costs[^1] / Math.Max(1, reference.Length);
    }
}
