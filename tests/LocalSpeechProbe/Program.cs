using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Wave;

// Explicit local files only: no downloader, credentials, microphone or HTTP client.
if (args.Length != 3 || !File.Exists(args[0]) || !File.Exists(args[1]) || args[2] is not ("en" or "ru"))
{
    Console.Error.WriteLine("Usage: LocalSpeechProbe <ggml-model.bin> <16kHz-mono.wav> <en|ru>");
    return 2;
}

using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; timeout.Cancel(); };
using var audio = File.OpenRead(args[1]);
var wav = new WaveParser(audio);
// WaveParser populates format properties while reading samples.
var samples = await wav.GetAvgSamplesAsync(timeout.Token);
if (wav.SampleRate != 16000 || wav.Channels != 1)
{
    Console.Error.WriteLine("Provide 16 kHz mono audio. This probe does not resample input.");
    return 2;
}
using var modelFile = File.OpenRead(args[0]);
string modelHash = Convert.ToHexString(await SHA256.HashDataAsync(modelFile, timeout.Token)).ToLowerInvariant();
int threads = Math.Min(4, Environment.ProcessorCount);
RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
var load = Stopwatch.StartNew();
using var factory = WhisperFactory.FromPath(args[0], new WhisperFactoryOptions { UseGpu = false });
using var processor = factory.CreateBuilder().WithLanguage(args[2]).WithThreads(threads)
    .WithNoContext().WithTemperature(0).Build();
load.Stop();
for (int pass = 1; pass <= 2; pass++)
{
    var inference = Stopwatch.StartNew();
    double? firstSegmentSeconds = null;
    var text = new List<string>();
    await foreach (var segment in processor.ProcessAsync(samples, timeout.Token))
    {
        firstSegmentSeconds ??= inference.Elapsed.TotalSeconds;
        text.Add(segment.Text.Trim());
    }
    inference.Stop();
    double duration = samples.Length / 16000d;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        model = Path.GetFileName(args[0]), modelSha256 = modelHash,
        audio = Path.GetFileName(args[1]), language = args[2], pass,
        runtime = "CPU", threads, audioSeconds = duration,
        modelLoadSeconds = load.Elapsed.TotalSeconds,
        firstSegmentSeconds, inferenceSeconds = inference.Elapsed.TotalSeconds,
        realTimeFactor = duration == 0 ? (double?)null : inference.Elapsed.TotalSeconds / duration,
        peakWorkingSetMiB = Process.GetCurrentProcess().PeakWorkingSet64 / (1024d * 1024),
        transcript = string.Join(" ", text)
    }));
}
return 0;
