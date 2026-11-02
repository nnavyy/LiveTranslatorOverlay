using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using LiveTranslatorOverlay.Core;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.LibraryLoader;

// Read-only comparison. No capture device, API credentials, or HTTP clients.
if (args.Length != 2 || !File.Exists(args[0]) || !File.Exists(args[1]))
{
    Console.Error.WriteLine("Usage: SpeechGateProbe <ggml-silero-v6.2.0.bin> <manifest.jsonl>");
    return 2;
}
const string expectedHash = "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987";
using var model = File.OpenRead(args[0]);
if (Convert.ToHexString(SHA256.HashData(model)).ToLowerInvariant() != expectedHash)
    throw new InvalidDataException("Use the pinned Silero v6.2.0 model documented in the research report.");
RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
using var factory = WhisperVadFactory.FromPath(args[0], new WhisperFactoryOptions { UseGpu = false });
using var vad = factory.CreateBuilder().WithUseGpu(false).WithThreads(2).WithThreshold(0.5f).Build();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
string root = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
foreach (string line in File.ReadLines(args[1]))
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    using var doc = JsonDocument.Parse(line);
    var row = doc.RootElement;
    string path = Path.GetFullPath(Path.Combine(root, row.GetProperty("path").GetString()!));
    if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Audio path escapes the manifest directory.");
    using var stream = File.OpenRead(path);
    string audioHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, timeout.Token)).ToLowerInvariant();
    if (audioHash != row.GetProperty("sha256").GetString()) throw new InvalidDataException("Audio checksum mismatch.");
    stream.Position = 0;
    // FLEURS uses floating-point WAVs; reuse the app's NAudio decoder for both
    // those files and the generated PCM16 controls without changing their samples.
    using var reader = new WaveFileReader(stream);
    if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1)
        throw new InvalidDataException("Expected 16kHz mono.");
    int count = checked((int)(reader.Length / reader.WaveFormat.BlockAlign));
    if (count <= 0 || count > 20 * 16000) throw new InvalidDataException("Expected audio <=20 seconds.");
    var samples = new float[count];
    var provider = reader.ToSampleProvider();
    int position = 0;
    while (position < count)
    {
        int read = provider.Read(samples.AsSpan(position));
        if (read == 0) throw new InvalidDataException("Truncated audio.");
        position += read;
    }
    if (Array.Exists(samples, value => !float.IsFinite(value))) throw new InvalidDataException("Non-finite audio.");
    var timer = Stopwatch.StartNew();
    var segments = await vad.DetectSpeechAsync(samples, timeout.Token);
    timer.Stop();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        id = row.GetProperty("id").GetString(), split = row.GetProperty("split").GetString(),
        kind = row.GetProperty("kind").GetString(), language = row.GetProperty("language").GetString(),
        currentEnergyGate = WhisperSttService.HasSustainedVoiceActivity(samples),
        neuralGate = segments.Count > 0, vadSeconds = timer.Elapsed.TotalSeconds,
        threshold = 0.5, vadSha256 = expectedHash, audioSha256 = audioHash,
        segments = segments.Select(segment => new { start = segment.Start.TotalSeconds, end = segment.End.TotalSeconds })
    }));
}
return 0;
