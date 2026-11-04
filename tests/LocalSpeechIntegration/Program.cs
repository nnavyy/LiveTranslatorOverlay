using System.Text.Json;
using LiveTranslatorOverlay.Core;
using NAudio.Wave;

if (args.Length != 1) throw new ArgumentException("Supply accuracy-round run directory");
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
foreach (string lang in new[] { "en", "ru" })
{
    string name = lang == "en" ? "distil-beam5" : "current-ru";
    using var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(args[0], "test", name + "-gated.json")));
    var speech = rows.RootElement.EnumerateArray().First(r => r.GetProperty("kind").GetString() == "speech" && r.GetProperty("prediction").GetString()!.Length > 0);
    var root = Path.GetFullPath(Path.Combine("..", "offline-research", "russian-focus-20261103"));
    using var reader = new WaveFileReader(Path.Combine(root, speech.GetProperty("path").GetString()!));
    var samples = new float[reader.Length / reader.WaveFormat.BlockAlign];
    var source = reader.ToSampleProvider();
    int position = 0;
    while (position < samples.Length)
    {
        int read = source.Read(samples.AsSpan(position));
        if (read == 0) throw new IOException("Truncated WAV");
        position += read;
    }
    await using var service = new LocalSpeechService();
    await service.InitializeAsync(lang, timeout.Token);
    var transcript = await service.TranscribeAsync(samples, timeout.Token);
    string expected = speech.GetProperty("prediction").GetString()!;
    if (transcript != expected) throw new Exception($"{lang} differs: {transcript} / {expected}");
    if (await service.TranscribeAsync(new float[64000], timeout.Token) != "") throw new Exception("Silence captioned");
    Console.WriteLine($"PASS {lang}: exact benchmark transcript; four-second silence rejected");
    await service.DisposeAsync(); // Stop/close can safely call cleanup twice.
}
await using (var service = new LocalSpeechService())
{
    try { await service.InitializeAsync("auto", timeout.Token); throw new Exception("Auto accepted"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS unsupported source fails explicitly"); }
}

var translator = new LiveTranslatorOverlay.Core.Translation.LocalTranslateProvider();
foreach (var pair in new[] { ("en", "ru", "Hello, how are you?"), ("ru", "en", "Привет, как дела?"), ("en", "id", "Good morning.") })
{
    string translated = await translator.TranslateAsync(pair.Item3, pair.Item2, pair.Item1);
    if (string.IsNullOrWhiteSpace(translated) || translated == pair.Item3) throw new Exception("Local translation failed");
    Console.WriteLine($"PASS local translation {pair.Item1}->{pair.Item2}: {translated}");
}
