using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Translation;
using NAudio.Wave;
using Whisper.net.LibraryLoader;

internal static class StreamingBenchmark
{
    private sealed record ClipBenchmarkResult(
        string Id,
        string Language,
        double AudioSeconds,
        double VoiceOnsetSeconds,
        int StepsRun,
        double StepMeanMs,
        double StepP50Ms,
        double StepP95Ms,
        double FinalDecodeMs,
        double FirstProvisionalStepComputeMs,
        double FirstProvisionalAudioOffsetSec,
        double FirstCommittedAudioOffsetSec,
        double FinalRtf,
        string ErrorMetricName,
        double ErrorRatePct,
        string Reference,
        string Hypothesis);

    private sealed record TranslationSampleResult(
        string SourceLanguage,
        string TargetLanguage,
        double ElapsedMs,
        bool IsColdStart,
        string SourceText,
        string TranslatedText);

    public static async Task RunAsync(string manifestPath, bool cpuOnly, bool skipMt, string? outJsonPath)
    {
        if (cpuOnly)
        {
            Environment.SetEnvironmentVariable("LIVETRANSLATOR_DISABLE_GPU", "1");
        }

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
            throw new InvalidOperationException("No matching held-out test clips found in manifest: " + manifestPath);

        Console.WriteLine($"Streaming benchmark: mode={(cpuOnly ? "CPU-only (Base)" : "Auto (Vulkan GPU LargeTurbo / CPU fallback)")}; clips={selected.Length}");

        var requestedTier = LocalSpeechModelTier.Auto;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var token = timeout.Token;

        var results = new List<ClipBenchmarkResult>(selected.Length);
        var mtInputs = new List<(string Language, string Text)>();

        double initTotalMs = 0;
        bool usingGpu = false;
        string backendLabel = "unknown";
        string modelLabel = "unknown";
        string loadedLibrary = "unknown";

        foreach (var group in selected.GroupBy(c => c.Language))
        {
            string lang = group.Key;
            await using var service = new LocalSpeechService(requestedTier);
            var initSw = Stopwatch.StartNew();
            await service.InitializeAsync(lang, token, msg => Console.WriteLine($"  [{lang}] {msg}"));
            initSw.Stop();

            if (initTotalMs == 0) initTotalMs = initSw.Elapsed.TotalMilliseconds;
            usingGpu = service.UsingGpu;
            backendLabel = service.BackendLabel;
            modelLabel = service.ActiveModelLabel;
            loadedLibrary = RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown";

            foreach (var clip in group)
            {
                const int sampleRate = 16000;
                const int stepSamples = 4800; // 300 ms
                double voiceOnsetSec = FindVoiceOnsetSeconds(clip.Audio);
                var agreement = new StreamingAgreement();
                var stepLatenciesMs = new List<double>();
                double firstProvisionalComputeMs = -1;
                double firstProvisionalOffsetSec = -1;
                double firstCommittedOffsetSec = -1;

                for (int end = stepSamples; end < clip.Audio.Length; end += stepSamples)
                {
                    int sliceStart = Math.Max(0, end - (sampleRate * 10));
                    var window = clip.Audio[sliceStart..end];
                    var sw = Stopwatch.StartNew();
                    string stepHyp = await service.TranscribeStreamingStepAsync(window, token);
                    sw.Stop();
                    stepLatenciesMs.Add(sw.Elapsed.TotalMilliseconds);

                    if (!string.IsNullOrWhiteSpace(stepHyp)
                        && !HallucinationDetector.IsHallucination(stepHyp)
                        && !HallucinationDetector.IsWhisperArtifact(stepHyp))
                    {
                        var st = agreement.Observe(stepHyp);
                        if (firstProvisionalOffsetSec < 0 && !string.IsNullOrWhiteSpace(st.FullText))
                        {
                            firstProvisionalOffsetSec = end / (double)sampleRate;
                            firstProvisionalComputeMs = sw.Elapsed.TotalMilliseconds;
                        }
                        if (firstCommittedOffsetSec < 0 && !string.IsNullOrWhiteSpace(st.CommittedText))
                        {
                            firstCommittedOffsetSec = end / (double)sampleRate;
                        }
                    }
                }

                var finalSw = Stopwatch.StartNew();
                string finalHyp = await service.TranscribeAsync(clip.Audio, token);
                finalSw.Stop();

                var finalState = agreement.FinalizeSegment(finalHyp);
                string hypothesis = string.IsNullOrWhiteSpace(finalHyp) ? finalState.FullText : finalHyp;
                mtInputs.Add((clip.Language, hypothesis));

                bool useCer = lang is "ja" or "zh" or "zh-TW";
                double errorPct = (useCer ? CharacterErrorRate(clip.Reference, hypothesis) : WordErrorRate(clip.Reference, hypothesis)) * 100.0;
                string metricName = useCer ? "CER" : "WER";
                double p50 = Percentile(stepLatenciesMs, 50);
                double p95 = Percentile(stepLatenciesMs, 95);
                double mean = stepLatenciesMs.Count > 0 ? stepLatenciesMs.Average() : 0;
                double rtf = finalSw.Elapsed.TotalSeconds / Math.Max(0.1, clip.Seconds);

                results.Add(new ClipBenchmarkResult(
                    clip.Id,
                    lang,
                    clip.Seconds,
                    voiceOnsetSec,
                    stepLatenciesMs.Count,
                    Math.Round(mean, 1),
                    Math.Round(p50, 1),
                    Math.Round(p95, 1),
                    Math.Round(finalSw.Elapsed.TotalMilliseconds, 1),
                    Math.Round(firstProvisionalComputeMs, 1),
                    Math.Round(firstProvisionalOffsetSec, 2),
                    Math.Round(firstCommittedOffsetSec, 2),
                    Math.Round(rtf, 3),
                    metricName,
                    Math.Round(errorPct, 2),
                    clip.Reference,
                    hypothesis));

                Console.WriteLine(
                    $"  [{lang}] {clip.Id} | {clip.Seconds:F2}s | step mean/p50/p95={mean:F1}/{p50:F1}/{p95:F1}ms | final={finalSw.Elapsed.TotalMilliseconds:F1}ms (RTF={rtf:F3}) | {metricName}={errorPct:F1}% | 1stWord@{firstProvisionalOffsetSec:F2}s ({firstProvisionalComputeMs:F1}ms)");
            }
        }

        var mtResults = new List<TranslationSampleResult>();
        if (!skipMt)
        {
            string localSpeechJson = Path.Combine(AppSettings.DataDirectory, "local-speech.json");
            if (File.Exists(localSpeechJson))
            {
                Console.WriteLine("Benchmarking persistent LocalTranslateProvider (--daemon)...");
                using var translator = new LocalTranslateProvider();
                for (int i = 0; i < mtInputs.Count; i++)
                {
                    var item = mtInputs[i];
                    if (string.IsNullOrWhiteSpace(item.Text)) continue;
                    string tgt = item.Language == "en" ? "id" : "en";
                    var sw = Stopwatch.StartNew();
                    string translated = await translator.TranslateAsync(item.Text, tgt, item.Language);
                    sw.Stop();
                    bool cold = mtResults.Count == 0;
                    mtResults.Add(new TranslationSampleResult(item.Language, tgt, Math.Round(sw.Elapsed.TotalMilliseconds, 1), cold, item.Text, translated));
                    Console.WriteLine($"  [MT {(cold ? "COLD" : "WARM")}] {item.Language}->{tgt}: {sw.Elapsed.TotalMilliseconds:F1}ms | \"{translated}\"");
                }
            }
            else
            {
                Console.WriteLine("Skipping LocalTranslateProvider benchmark: local-speech.json not found.");
            }
        }

        var allSteps = results.Where(r => r.StepsRun > 0).ToArray();
        var perLang = results.GroupBy(r => r.Language).Select(g => new
        {
            language = g.Key,
            clips = g.Count(),
            metric = g.First().ErrorMetricName,
            meanErrorPct = Math.Round(g.Average(x => x.ErrorRatePct), 2),
            stepMeanMs = Math.Round(g.Average(x => x.StepMeanMs), 1),
            stepP95Ms = Math.Round(g.Average(x => x.StepP95Ms), 1),
            finalRtf = Math.Round(g.Average(x => x.FinalRtf), 3)
        }).ToArray();

        var warmMt = mtResults.Where(m => !m.IsColdStart).Select(m => m.ElapsedMs).ToList();
        var summary = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            mode = cpuOnly ? "cpu-fallback" : "vulkan-gpu-turbo",
            usingGpu,
            backendLabel,
            modelLabel,
            loadedLibrary,
            initMs = Math.Round(initTotalMs, 1),
            overallStepMeanMs = Math.Round(allSteps.Average(r => r.StepMeanMs), 1),
            overallStepP50Ms = Math.Round(Percentile(allSteps.Select(r => r.StepP50Ms).ToList(), 50), 1),
            overallStepP95Ms = Math.Round(Percentile(allSteps.Select(r => r.StepP95Ms).ToList(), 95), 1),
            overallFinalRtf = Math.Round(results.Average(r => r.FinalRtf), 3),
            firstProvisionalComputeMeanMs = Math.Round(results.Where(r => r.FirstProvisionalStepComputeMs >= 0).DefaultIfEmpty().Average(r => r?.FirstProvisionalStepComputeMs ?? 0), 1),
            perLanguage = perLang,
            mtColdFirstCallMs = mtResults.FirstOrDefault()?.ElapsedMs,
            mtWarmMeanMs = warmMt.Count > 0 ? Math.Round(warmMt.Average(), 1) : (double?)null,
            mtWarmP50Ms = warmMt.Count > 0 ? Math.Round(Percentile(warmMt, 50), 1) : (double?)null,
            mtWarmP95Ms = warmMt.Count > 0 ? Math.Round(Percentile(warmMt, 95), 1) : (double?)null,
            clips = results,
            translations = mtResults
        };

        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrWhiteSpace(outJsonPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outJsonPath))!);
            File.WriteAllText(outJsonPath, json, Encoding.UTF8);
            Console.WriteLine($"Wrote streaming benchmark report: {outJsonPath}");
        }
        Console.WriteLine($"SUMMARY [{summary.backendLabel} / {summary.modelLabel}]: stepMean={summary.overallStepMeanMs}ms, stepP95={summary.overallStepP95Ms}ms, RTF={summary.overallFinalRtf}, MT warm mean={summary.mtWarmMeanMs}ms");
    }

    private static float[] ReadAudio16k(string path)
    {
        using var reader = new WaveFileReader(path);
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1)
            throw new InvalidDataException($"Expected 16 kHz mono WAV: {path}");
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

    private static double FindVoiceOnsetSeconds(float[] samples)
    {
        for (int i = 0; i + 1600 <= samples.Length; i += 800)
        {
            double energy = 0;
            for (int j = 0; j < 1600; j++) energy += Math.Abs(samples[i + j]);
            if (energy / 1600.0 >= 0.004) return i / 16000.0;
        }
        return 0;
    }

    private static double Percentile(List<double> values, double pct)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        double pos = (pct / 100.0) * (sorted.Length - 1);
        int lo = (int)Math.Floor(pos);
        int hi = (int)Math.Ceiling(pos);
        if (lo == hi) return sorted[lo];
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
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
