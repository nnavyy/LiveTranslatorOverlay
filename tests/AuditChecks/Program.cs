using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.IO;
using LiveTranslatorOverlay;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Localization;
using LiveTranslatorOverlay.Core.Translation;
using LiveTranslatorOverlay.Core.Feedback;
using NAudio.CoreAudioApi;
using NAudio.Wave;

const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
AppContext.SetData("LiveTranslatorOverlay.QuotaCacheDirectory", Path.Combine(Path.GetTempPath(), "TranslatorAuditQuota-" + Guid.NewGuid().ToString("N")));
int passed = 0, total = 0;
void Check(string name, bool valid, string evidence)
{
    total++;
    if (valid) passed++;
    Console.WriteLine($"{(valid ? "PASS" : "FAIL")}: {name}: {evidence}");
}
void Set(object obj, string field, object value) => obj.GetType().GetField(field, Fields)!.SetValue(obj, value);
T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Fields)!.GetValue(obj)!;
void Mock(object provider, HttpMessageHandler handler)
{
    Get<HttpClient>(provider, "_httpClient").Dispose();
    Set(provider, "_httpClient", new HttpClient(handler));
}

const string directions = "Go north -> turn east -> stop.";
string cleaned = HallucinationDetector.CleanTranslationOutput(directions, directions);
Check("Translation cleanup preserves meaningful arrows", cleaned == directions, cleaned);

var provider = new GroqTranslateProvider("dummy-audit-key");
provider.SetModel("llama-3.3-70b-versatile");
Set(provider, "_cachedInput", "hello"); Set(provider, "_cachedTarget", "id");
Set(provider, "_cachedSource", "en"); Set(provider, "_cachedResult", "Old model output");
var modelHandler = new JsonHandler("{\"choices\":[{\"message\":{\"content\":\"New model output\"}}]}");
Mock(provider, modelHandler);
provider.SetModel("openai/gpt-oss-20b");
string modelOutput = await provider.TranslateAsync("hello", "id", "en");
Check("Changing model invalidates old translation", modelOutput == "New model output" && modelHandler.Calls == 1, $"output={modelOutput}; calls={modelHandler.Calls}");

var capture = new AudioCaptureService();
Set(capture, "_captureFormat", WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, 16000, 1, 64000, 4, 32));
var consume = (CaptureData)Delegate.CreateDelegate(typeof(CaptureData), capture, typeof(AudioCaptureService).GetMethod("OnDataAvailable", Fields)!);
consume([0, 0, 0, 64, 0, 0, 0, 64], (AudioClientBufferFlags)0, 0, 0);
float[] samples = Get<ConcurrentQueue<float>>(capture, "_audioBuffer").ToArray();
Check("PCM32 input preserves sample amplitude", samples.Length == 2 && samples.All(s => Math.Abs(s - 0.5f) < 0.001), string.Join(",", samples));

var fallback = typeof(GroqTranslateProvider).GetField("SharedGoogleFallback", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
Mock(fallback, new JsonHandler("{\"sentences\":[{\"trans\":\"Complete fallback translation\"}]}"));
var truncated = new GroqTranslateProvider("dummy-audit-key");
truncated.SetModel("llama-3.3-70b-versatile");
Mock(truncated, new JsonHandler("data: {\"choices\":[{\"delta\":{\"content\":\"Partial\"},\"finish_reason\":\"length\"}]}\n\ndata: [DONE]\n"));
string streamed = "";
await truncated.TranslateStreamAsync("Complete source sentence", "id", "en", s => streamed = s);
Check("Truncated streams are not cached as complete", Get<string>(truncated, "_cachedInput") != "Complete source sentence" && streamed == "Complete fallback translation", $"result={streamed}; cached={Get<string>(truncated, "_cachedInput")}");

bool listenersReleased = false;
Exception? historyError = null;
var historyThread = new Thread(() =>
{
    var locale = LocalizationManager.Instance;
    var subscriptions = typeof(LocalizationManager).GetField("LanguageChanged", Fields)!;
    object? original = subscriptions.GetValue(locale);
    try
    {
        int before = (original as Delegate)?.GetInvocationList().Length ?? 0;
        var window = new HistoryWindow();
        window.Close();
        int after = (subscriptions.GetValue(locale) as Delegate)?.GetInvocationList().Length ?? 0;
        listenersReleased = before == after;
    }
    catch (Exception ex) { historyError = ex; }
    finally { subscriptions.SetValue(locale, original); }
});
historyThread.SetApartmentState(ApartmentState.STA);
historyThread.Start();
historyThread.Join();
Check("Closed history window releases locale subscription", listenersReleased && historyError == null, historyError?.Message ?? $"released={listenersReleased}");

var queued = new AudioCaptureService();
int active = 0, peak = 0;
async Task Handle(float[] chunk)
{
    int now = Interlocked.Increment(ref active);
    int old;
    do { old = Volatile.Read(ref peak); } while (now > old && Interlocked.CompareExchange(ref peak, now, old) != old);
    await Task.Delay(1500);
    Interlocked.Decrement(ref active);
}
var asyncHandler = typeof(AudioCaptureService).GetProperty("AudioChunkHandler");
if (asyncHandler != null) asyncHandler.SetValue(queued, (Func<float[], Task>)Handle);
else queued.AudioChunkReady += async (_, chunk) => await Handle(chunk);
Set(queued, "_isCapturing", true);
var queue = Get<ConcurrentQueue<float>>(queued, "_audioBuffer");
var loop = (Task)typeof(AudioCaptureService).GetMethod("ChunkEmitterLoop", Fields)!.Invoke(queued, null)!;
for (int n = 0; n < 4; n++)
{
    for (int i = 0; i < 192000; i++) queue.Enqueue((float)(0.1 * Math.Sin(i * 0.1)));
    await Task.Delay(180);
}
queued.Stop();
await loop.WaitAsync(TimeSpan.FromSeconds(3));
await Task.Delay(1600);
Check("Async chunk processing respects two-slot limit", peak > 0 && peak <= 2, $"peak concurrent handlers={peak}");

string dataRoot = Path.Combine(Path.GetTempPath(), "TranslatorAuditData-" + Guid.NewGuid().ToString("N"));
AppContext.SetData("LiveTranslatorOverlay.DataDirectory", dataRoot);
Parallel.For(0, 20, i => FeedbackHistoryManager.AddRecord("Bug", $"Synthetic {i}", "Offline test; never sent", ""));
var reports = FeedbackHistoryManager.Load();
FeedbackHistoryManager.SetDeliveryState(reports[0].Id, "Failed");
FeedbackHistoryManager.SetDeliveryState(reports[1].Id, "Sent");
var persisted = FeedbackHistoryManager.Load();
Check("Concurrent feedback records and delivery state persist separately", persisted.Count == 20
    && persisted.Select(r => r.Id).Distinct().Count() == 20
    && persisted.Any(r => r.DeliveryState == "Failed" && r.Status == "Pending")
    && persisted.Any(r => r.DeliveryState == "Sent" && r.Status == "Pending"), $"records={persisted.Count}; distinct delivery/resolution states");
new AppSettings { UiLanguage = "es" }.Save();
Check("Settings use stable per-user storage", AppSettings.GetSettingsPath() == Path.Combine(dataRoot, "appsettings.json")
    && AppSettings.Load().UiLanguage == "es", "isolated settings round-trip");
var sessionHistory = new TranslationHistory();
for (int i = 0; i < 501; i++) sessionHistory.Add($"phrase-{i}:", "translated");
bool boundedHistory = !sessionHistory.ToString().Contains("phrase-0:") && sessionHistory.ToString().Contains("phrase-500:");
sessionHistory.Clear();
Check("Session history is bounded and clearable", boundedHistory && sessionHistory.ToString() == "", "last 500 entries retained, then cleared");
Console.WriteLine($"Audit checks: {passed}/{total} passed");
Environment.ExitCode = passed == total ? 0 : 1;

delegate void CaptureData(ReadOnlySpan<byte> bytes, AudioClientBufferFlags flags, long position, long clock);
sealed class JsonHandler(string body) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
