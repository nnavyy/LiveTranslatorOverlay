using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveTranslatorOverlay.Core;
using NAudio.Wave;
using Whisper.net;

internal static class RussianCaptionProbe
{
    public static async Task RunAsync(string manifest)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(manifest))!;
        var samples = File.ReadLines(manifest).Select(line => JsonSerializer.Deserialize<JsonElement>(line))
            .Where(row => Text(row, "split") == "test" && Text(row, "kind") == "speech" && Text(row, "language") == "ru"
                && row.GetProperty("seconds").GetDouble() is >= 4 and <= 11.5)
            .GroupBy(row => Text(row, "corpus")).OrderBy(group => group.Key)
            .SelectMany(group => group.OrderBy(row => Text(row, "id")).Take(6))
            .Select(row => (Id: Text(row, "id"), Corpus: Text(row, "corpus"), Reference: Text(row, "reference"), Audio: ReadAudio(Path.Combine(root, Text(row, "path")))))
            .ToArray();
        if (samples.Length != 12 || samples.Select(row => row.Corpus).Distinct().Count() != 2)
            throw new InvalidOperationException("Expected six held-out Russian clips from each of two corpora.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var token = timeout.Token;
        foreach (bool larger in new[] { false, true })
        {
            await using var model = new LocalSpeechService(larger);
            var timer = Stopwatch.StartNew();
            await model.InitializeAsync("ru", token);
            var modelName = larger ? "small" : "base";
            Console.WriteLine(JsonSerializer.Serialize(new { model = modelName, initSeconds = timer.Elapsed.TotalSeconds }));
            await model.TranscribeAsync(samples[0].Audio, token); // Unscored warm-up.
            foreach (var row in samples)
            {
                timer.Restart();
                string prediction = await model.TranscribeAsync(row.Audio, token);
                var seconds = timer.Elapsed.TotalSeconds;
                var expected = Words(row.Reference);
                int edits = EditDistance(expected, Words(prediction));
                Console.WriteLine(JsonSerializer.Serialize(new { model = modelName, row.Id, row.Corpus, audioSeconds = row.Audio.Length / 16000d,
                    inferenceSeconds = seconds, words = expected.Length, edits, blank = string.IsNullOrWhiteSpace(prediction) }));
            }
        }

        using var vadFactory = WhisperVadFactory.FromPath(await SpeechModelDownload.EnsureVadAsync(null, token), new WhisperFactoryOptions { UseGpu = false });
        using var vad = vadFactory.CreateBuilder().WithUseGpu(false).WithThreads(2).WithThreshold(.5f).Build();
        foreach (int db in new[] { 0, -18, -24 })
        {
            float gain = (float)Math.Pow(10, db / 20d);
            int energyAccept = 0, neuralAccept = 0, lostBeforeNeural = 0;
            foreach (var row in samples)
            {
                var pcm = row.Audio.Select(sample => sample * gain).ToArray();
                bool energy = WhisperSttService.HasSustainedVoiceActivity(pcm);
                bool neural = (await vad.DetectSpeechAsync(pcm, token)).Count > 0;
                if (energy) energyAccept++;
                if (neural) neuralAccept++;
                if (!energy && neural) lostBeforeNeural++;
            }
            Console.WriteLine(JsonSerializer.Serialize(new { gainDb = db, clips = samples.Length, energyAccept, neuralAccept, lostBeforeNeural }));
        }
    }

    private static string Text(JsonElement row, string key) => row.GetProperty(key).GetString()!;

    private static float[] ReadAudio(string path)
    {
        using var reader = new WaveFileReader(path);
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1) throw new InvalidDataException("Expected mono 16 kHz WAV.");
        var samples = new float[reader.Length / reader.WaveFormat.BlockAlign];
        var source = reader.ToSampleProvider();
        int offset = 0;
        while (offset < samples.Length)
        {
            int read = source.Read(samples.AsSpan(offset));
            if (read == 0) throw new EndOfStreamException(path);
            offset += read;
        }
        return samples;
    }

    private static string[] Words(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+")
        .Select(match => match.Value.Replace('ё', 'е')).ToArray();

    private static int EditDistance(string[] expected, string[] actual)
    {
        var row = Enumerable.Range(0, actual.Length + 1).ToArray();
        for (int i = 1; i <= expected.Length; i++)
        {
            int diagonal = row[0];
            row[0] = i;
            for (int j = 1; j <= actual.Length; j++)
            {
                int prior = row[j];
                row[j] = Math.Min(diagonal + (expected[i - 1] == actual[j - 1] ? 0 : 1), Math.Min(prior + 1, row[j - 1] + 1));
                diagonal = prior;
            }
        }
        return row[^1];
    }
}
