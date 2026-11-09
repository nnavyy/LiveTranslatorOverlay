using System.Net;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Localization;
using LiveTranslatorOverlay.Core.Translation;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.LibraryLoader;

static void Check(bool result, string message)
{
    if (!result) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}

if (args.Contains("--ui"))
{
    Exception? error = null;
    var thread = new Thread(() =>
    {
        try
        {
            string root = Path.Combine(Path.GetTempPath(), "LiveTranslatorUiChecks-" + Guid.NewGuid().ToString("N"));
            AppContext.SetData("LiveTranslatorOverlay.DataDirectory", root);
            var app = new System.Windows.Application();
            var window = new LiveTranslatorOverlay.MainWindow();
            var speech = (System.Windows.Controls.ComboBox)window.FindName("CmbSttMode");
            var translation = (System.Windows.Controls.ComboBox)window.FindName("CmbEngine");
            Check(speech.SelectedIndex == 2 && translation.SelectedIndex == 0, "WPF starts with local speech and key-free translation selected");
            Check(((System.Windows.Controls.PasswordBox)window.FindName("TxtApiKey")).Password == "", "Fresh UI requires no API key");
            Check(!((System.Windows.Controls.ComboBoxItem)window.FindName("ItemLocalTranslation")).IsEnabled, "Unconfigured advanced translation cannot be selected");
            window.Close();
            app.Shutdown();
        }
        catch (Exception exception) { error = exception; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start(); thread.Join();
    if (error != null) throw error;
    return;
}

Check(new AppSettings().SttModeIndex == 2, "New users default to key-free speech");
foreach (int oldMode in new[] { 0, 1, 2, 3, 99 })
{
    var settings = new AppSettings { SttModeIndex = oldMode, EngineIndex = 2 };
    settings.UseAvailableEngines(false);
    Check(settings.SttModeIndex == (oldMode == 3 ? 3 : 2) && settings.EngineIndex == 0,
        $"Keyless settings {oldMode} migrate to available engines");
}
var configured = new AppSettings { SttModeIndex = 0, EngineIndex = 1, DeepgramApiKey = "test", GroqApiKey = "test" };
configured.UseAvailableEngines(false);
Check(configured.SttModeIndex == 0 && configured.EngineIndex == 1, "Explicit configured cloud choices survive");

var translator = new GoogleFreeTranslateProvider();
var client = typeof(GoogleFreeTranslateProvider).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!;
((HttpClient)client.GetValue(translator)!).Dispose();
client.SetValue(translator, new HttpClient(new UnavailableHandler()));
try
{
    await translator.TranslateAsync("Do not open the door.", "id", "en");
    throw new Exception("An outage was silently displayed as a translation");
}
catch (HttpRequestException) { Console.WriteLine("PASS: Translation outage is reported, not shown as a successful translation"); }

string isolated = Path.Combine(Path.GetTempPath(), "LiveTranslatorCpuChecks-" + Guid.NewGuid().ToString("N"));
AppContext.SetData("LiveTranslatorOverlay.DataDirectory", isolated);
using var canceled = new CancellationTokenSource();
try
{
    await SpeechModelDownload.EnsureAsync(false, _ => canceled.Cancel(), canceled.Token);
    throw new Exception("Canceled download succeeded");
}
catch (OperationCanceledException) { Console.WriteLine("PASS: First-use model download can be canceled"); }
Check(!Directory.EnumerateFiles(isolated, "*", SearchOption.AllDirectories).Any(), "Canceled download leaves no model or partial file");
// Exercise the real downloader against a tiny local HTTP fixture with the wrong digest.
using (var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
{
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var response = Task.Run(async () =>
    {
        using var connection = await listener.AcceptTcpClientAsync(deadline.Token);
        using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(deadline.Token))) { }
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: close\r\n\r\noops"), deadline.Token);
    });
    var download = typeof(SpeechModelDownload).GetMethod("EnsureFileAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
    var task = (Task<string>)download.Invoke(null, ["bad.bin", new string('0', 64), 4L, $"http://127.0.0.1:{port}/", null, deadline.Token])!;
    try { await task; throw new Exception("Invalid checksum was accepted"); }
    catch (InvalidDataException) { Console.WriteLine("PASS: Download checksum mismatch is rejected"); }
    await response;
    Check(!Directory.EnumerateFiles(isolated, "*", SearchOption.AllDirectories).Any(), "Failed integrity check removes the partial download");
}
AppContext.SetData("LiveTranslatorOverlay.DataDirectory", null);
Directory.Delete(Path.Combine(isolated, "models"));
Directory.Delete(isolated);

if (!args.Contains("--integration")) return;
if (args.Contains("--noavx"))
{
    RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.CpuNoAvx];
    _ = WhisperFactory.GetSupportedLanguages().ToArray();
    Check(RuntimeOptions.LoadedLibrary == RuntimeLibrary.CpuNoAvx, "Older-CPU native runtime loads");
}
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
bool small = args.Contains("--small");
await using (var service = new LocalSpeechService(small))
{
    await service.InitializeAsync("auto", timeout.Token, Console.WriteLine);
    Check(await service.TranscribeAsync(new float[64000], timeout.Token) == "", "Auto-detect starts without prepared setup and rejects silence");
    Check(await service.TranscribeAsync(Enumerable.Repeat(.1f, 64000).ToArray(), timeout.Token) == "", "Neural speech gate rejects constant non-speech input");
    Check(RuntimeOptions.LoadedLibrary is RuntimeLibrary.Cpu or RuntimeLibrary.CpuNoAvx, "Speech uses CPU runtime: " + RuntimeOptions.LoadedLibrary);
    try { await service.TranscribeAsync([float.NaN], timeout.Token); throw new Exception("Non-finite audio accepted"); }
    catch (ArgumentException) { Console.WriteLine("PASS: Invalid PCM rejected"); }
    await service.DisposeAsync();
    await service.DisposeAsync();
}
var nativeLanguages = WhisperFactory.GetSupportedLanguages().ToHashSet();
Check(nativeLanguages.Where(code => code != "yue").All(code => LanguageCatalog.Names.ContainsKey(code)), "Language selector covers all multilingual base/small speech languages");

int fixtureArg = Array.IndexOf(args, "--fixtures");
if (fixtureArg < 0) return;
string fixtureRoot = Path.GetFullPath(args[fixtureArg + 1]);
var rows = File.ReadLines(Path.Combine(fixtureRoot, "test.jsonl"))
    .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
foreach (string language in new[] { "en", "ru" })
{
    await using var service = new LocalSpeechService(small);
    await service.InitializeAsync(language == "en" ? "auto" : language, timeout.Token);
    foreach (var row in rows.Where(row => row.GetProperty("language").GetString() == language &&
        row.GetProperty("kind").GetString() == "speech" && row.GetProperty("seconds").GetDouble() < 12).Take(3))
    {
        using var reader = new WaveFileReader(Path.Combine(fixtureRoot, row.GetProperty("path").GetString()!));
        Check(reader.WaveFormat.SampleRate == 16000 && reader.WaveFormat.Channels == 1, "Fixture is mono 16 kHz");
        var samples = new float[reader.Length / reader.WaveFormat.BlockAlign];
        var source = reader.ToSampleProvider();
        int offset = 0;
        while (offset < samples.Length)
        {
            int read = source.Read(samples.AsSpan(offset));
            if (read == 0) throw new IOException("Incomplete WAV");
            offset += read;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        string actual = await service.TranscribeAsync(samples, timeout.Token);
        string expected = row.GetProperty("reference").GetString()!;
        double errorRate = WordErrorRate(expected, actual);
        Console.WriteLine($"{language} {row.GetProperty("id").GetString()}: WER={errorRate:P1}; time={watch.Elapsed.TotalSeconds:F2}s; audio={samples.Length / 16000d:F2}s; text={actual}");
        Check(!string.IsNullOrWhiteSpace(actual) && errorRate < .6, "Speech smoke fixture retains most reference words (not a general accuracy score)");
    }
}

static double WordErrorRate(string expected, string actual)
{
    static string[] Words(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+")
        .Select(match => match.Value.Replace('ё', 'е')).ToArray();
    var reference = Words(expected); var hypothesis = Words(actual);
    var costs = Enumerable.Range(0, hypothesis.Length + 1).ToArray();
    for (int i = 1; i <= reference.Length; i++)
    {
        int diagonal = costs[0]; costs[0] = i;
        for (int j = 1; j <= hypothesis.Length; j++)
        {
            int previous = costs[j];
            costs[j] = Math.Min(Math.Min(costs[j] + 1, costs[j - 1] + 1), diagonal + (reference[i - 1] == hypothesis[j - 1] ? 0 : 1));
            diagonal = previous;
        }
    }
    return (double)costs[^1] / Math.Max(1, reference.Length);
}
sealed class UnavailableHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
}
