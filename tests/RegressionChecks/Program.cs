using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Reflection;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Translation;
using LiveTranslatorOverlay.Core.Localization;
using LiveTranslatorOverlay.Core.Update;
using LiveTranslatorOverlay.Core.Feedback;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Offline regression checks against the current release assembly. No credentials,
// devices, updater execution, feedback submissions, or real HTTP requests.
const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
AppContext.SetData("LiveTranslatorOverlay.QuotaCacheDirectory",
    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LiveTranslatorQuotaChecks-" + Guid.NewGuid().ToString("N")));
int observed = 0;
void Confirm(string name, bool reproduced, string detail)
{
    Console.WriteLine($"{(reproduced ? "PASS" : "FAIL")}: {name}: {detail}");
    if (reproduced) observed++;
}
void Set(object target, string field, object value) =>
    target.GetType().GetField(field, PrivateInstance)!.SetValue(target, value);
T Get<T>(object target, string field) =>
    (T)target.GetType().GetField(field, PrivateInstance)!.GetValue(target)!;

string[] ordinarySpeech = ["yes", "no", "thank you", "terima kasih", "спасибо", "你好", "はい", "안녕"];
var blocked = ordinarySpeech.Where(HallucinationDetector.IsHallucination).ToArray();
Confirm("Ordinary short speech survives filtering", blocked.Length == 0,
    $"{blocked.Length}/{ordinarySpeech.Length} valid examples rejected: {string.Join(" / ", blocked)}");

var groq = new GroqTranslateProvider("offline-dummy-key");
Set(groq, "_cachedInput", "The price is 100 dollars.");
Set(groq, "_lastTranslatedInput", "The price is 100 dollars.");
Set(groq, "_cachedTarget", "id");
Set(groq, "_cachedSource", "en");
Set(groq, "_cachedResult", "Harganya 100 dolar.");
Get<HttpClient>(groq, "_httpClient").Dispose();
Set(groq, "_httpClient", new HttpClient(new PriceHandler()));
groq.SetModel("llama-3.3-70b-versatile");
string wrongPrice = await groq.TranslateAsync("The price is 900 dollars.", "id", "en");
Set(groq, "_cachedInput", "The price is 100 dollars.");
Set(groq, "_cachedResult", "Harganya 100 dolar.");
string streamedPrice = "";
await groq.TranslateStreamAsync("The price is 900 dollars.", "id", "en", s => streamedPrice = s);
Confirm("Changed number receives a fresh translation", wrongPrice == "Harganya 900 dolar." && streamedPrice == wrongPrice,
    $"100 -> 900: both methods return '{wrongPrice}'");

var google = new GoogleFreeTranslateProvider();
Get<HttpClient>(google, "_httpClient").Dispose();
var mock = new MockHandler();
Set(google, "_httpClient", new HttpClient(mock));
string fallbackText = await google.TranslateAsync("Hello", "en", "en");
Confirm("Valid unchanged translation avoids fallback",
    mock.Requests == 1 && fallbackText == "Hello",
    $"mock HTTP calls={mock.Requests}; displayed='{fallbackText}'");
string repeated = await google.TranslateAsync("Hello", "en", "en");
Confirm("Identical successful translation uses cache", repeated == "Hello" && mock.Requests == 1, $"HTTP calls={mock.Requests}");
var fallbackMethod = typeof(GoogleFreeTranslateProvider).GetMethod("TryMyMemoryAsync", PrivateInstance)!;
string rejectedError = await (Task<string>)fallbackMethod.Invoke(google, ["Hello", "en", "en"])!;
Confirm("Fallback application errors do not become captions", rejectedError == "", $"rejected error text; HTTP calls={mock.Requests}");

var audio = new AudioCaptureService();
var buffer = Get<ConcurrentQueue<float>>(audio, "_audioBuffer");
int emitted = 0;
audio.AudioChunkReady += (_, _) => Interlocked.Increment(ref emitted);
Set(audio, "_isCapturing", true);
for (int i = 0; i < 32000; i++) buffer.Enqueue((float)(0.1 * Math.Sin(i * 0.1)));
var loop = (Task)typeof(AudioCaptureService).GetMethod("ChunkEmitterLoop", PrivateInstance)!.Invoke(audio, null)!;
await Task.Delay(1300);
int shortCount = emitted;
for (int i = 0; i < 32000; i++) buffer.Enqueue((float)(0.1 * Math.Sin(i * 0.1)));
await Task.Delay(1300);
int fourSecondCount = emitted;
audio.Stop();
await loop.WaitAsync(TimeSpan.FromSeconds(2));
Confirm("Short playback emits exactly once per segment", shortCount == 1 && fourSecondCount == 2,
    $"2 seconds + 1.3 seconds idle: {shortCount} chunks; after total 4 seconds: {fourSecondCount} chunk");

Confirm("Silence and repetition stay filtered",
    HallucinationDetector.IsHallucination("...") && HallucinationDetector.IsHallucination("[music]")
    && HallucinationDetector.IsHallucination("hello hello hello hello")
    && !WhisperSttService.HasSustainedVoiceActivity(new float[32000]), "noise controls");

var loc = LocalizationManager.Instance;
var strings = Get<Dictionary<string, Dictionary<string, string>>>(loc, "_strings");
var missing = strings["en"].Keys.Where(k => !strings["es"].ContainsKey(k)).ToArray();
bool formatsMatch = strings["en"].All(kv =>
    Regex.Matches(kv.Value, @"\{\d+\}").Select(m => m.Value).Order()
    .SequenceEqual(Regex.Matches(strings["es"].GetValueOrDefault(kv.Key, ""), @"\{\d+\}").Select(m => m.Value).Order()));
loc.SetLanguage("es");
Confirm("Spanish resources cover every English key and placeholder",
    missing.Length == 0 && formatsMatch && loc.ActiveLanguage == "es" && loc.Get("BtnSettings") == "Ajustes",
    $"missing={string.Join(',', missing)}; format parity={formatsMatch}; keys={strings["es"].Count}");
loc.SetLanguage("en");
Confirm("New language codes reach both prompt mappings", AdditionalLanguages.Names.Count >= 9
    && AdditionalLanguages.Names.All(kv => GroqTranslateProvider.ResolveSourceLanguageName(kv.Key) == kv.Value
    && GroqTranslateProvider.ResolveTargetLanguageDescription(kv.Key, LiveTranslatorOverlay.Core.Japanese.JapaneseScriptStyle.Standard) == kv.Value),
    string.Join(", ", AdditionalLanguages.Names.Keys));
Confirm("Release notice is once per installed version", ReleaseNotes.ShouldNotify(null)
    && ReleaseNotes.ShouldNotify("v1.3.3") && !ReleaseNotes.ShouldNotify(AppVersion.DisplayString), AppVersion.DisplayString);
var assemblyVersion = typeof(GroqTranslateProvider).Assembly.GetName().Version!;
Confirm("Compiled version and release notes agree", assemblyVersion.Major == AppVersion.Current.Major
    && assemblyVersion.Minor == AppVersion.Current.Minor && assemblyVersion.Build == AppVersion.Current.Build
    && ReleaseNotes.Text.Contains(AppVersion.DisplayString), $"assembly={assemblyVersion}; notes={AppVersion.DisplayString}");
Confirm("Machine-learning edition reports 1.0.0 and can advance to 1.0.1",
    AppVersion.Current == new Version(1, 0, 0) && AppVersion.IsNewerThanCurrent("1.0.1"),
    $"installed={AppVersion.DisplayString}; next=1.0.1");
var payload = Encoding.UTF8.GetBytes("stable update test fixture");
var checksum = Convert.ToHexString(SHA256.HashData(payload));
string manifest = $$"""
    {"line":"ml","channel":"stable","version":"1.0.1","packages":{
      "win-x64":{"url":"https://github.com/nnavyy/LiveTranslatorOverlay/releases/download/v1.0.1-ml/LiveTranslatorOverlay-Windows-x64.zip",
      "size":{{payload.Length}},"sha256":"{{checksum}}"}
    }
    }
    """;
var nextRelease = StableRelease.Read(manifest, "win-x64")!;
Confirm("Stable updater selects only the new release line and matching platform",
    nextRelease.Version > AppVersion.Current && StableRelease.Read(manifest, "osx-arm64") == null
    && StableRelease.Read(manifest.Replace("\"ml\"", "\"legacy\""), "win-x64") == null
    && StableRelease.Read(manifest.Replace("\"stable\"", "\"beta\""), "win-x64") == null,
    $"candidate={nextRelease.Version}; legacy and beta excluded");
var releaseFixture = Directory.CreateTempSubdirectory("LiveTranslatorReleaseChecks-").FullName;
try
{
    string archive = Path.Combine(releaseFixture, "fixture.zip");
    File.WriteAllBytes(archive, payload);
    await nextRelease.VerifyAsync(archive, CancellationToken.None);
    payload[0] ^= 1;
    File.WriteAllBytes(archive, payload);
    bool mismatchRejected = false;
    try { await nextRelease.VerifyAsync(archive, CancellationToken.None); }
    catch (InvalidDataException) { mismatchRejected = true; }
    Confirm("Update checksum rejects same-sized tampering", mismatchRejected, "verified original payload, rejected altered payload");

    string receipt = Path.Combine(releaseFixture, "terms-accepted.txt");
    bool newInstall = !TermsNotice.HasAccepted(releaseFixture);
    File.WriteAllLines(receipt, ["1", DateTimeOffset.UtcNow.ToString("O")]);
    bool oldRejected = !TermsNotice.HasAccepted(releaseFixture);
    TermsNotice.Accept(releaseFixture);
    Confirm("Terms require acceptance of the current bundled revision", newInstall && oldRejected
        && TermsNotice.HasAccepted(releaseFixture) && TermsNotice.Text.Contains("Privacy Policy"), TermsNotice.Revision);

    var time = new ManualTime();
    var usage = new GuestAudioUsage(releaseFixture, time);
    time.Advance(15);
    var beforeStart = usage.Checkpoint();
    usage.Start();
    time.Advance(15);
    var afterFirstCheckpoint = usage.Checkpoint();
    time.Advance(600); // Sleep or a missed heartbeat.
    var afterSleep = usage.Checkpoint();
    time.Advance(2);
    var stopped = usage.Stop();
    time.Advance(10);
    var reloaded = new GuestAudioUsage(releaseFixture, time).Checkpoint();
    Confirm("Guest audio duration counts healthy capture time once and persists locally",
        beforeStart == TimeSpan.Zero && afterFirstCheckpoint == TimeSpan.FromSeconds(15)
        && afterSleep == afterFirstCheckpoint && stopped == TimeSpan.FromSeconds(17)
        && usage.Checkpoint() == stopped && reloaded == stopped, $"persisted={reloaded.TotalSeconds}s");
}
finally { Directory.Delete(releaseFixture, recursive: true); }

var records = new List<FeedbackRecord> { new() { Id = "REP-TEST", AppVersion = "v1.3.3", Status = "Pending" } };
var applyStatuses = typeof(FeedbackHistoryManager).GetMethod("ApplyStatuses", BindingFlags.Static | BindingFlags.NonPublic)!;
using var unrelated = JsonDocument.Parse("{\"REP-OTHER\":{\"status\":\"Fixed\"}}");
applyStatuses.Invoke(null, [records, unrelated.RootElement]);
bool keptPending = records[0].Status == "Pending";
using var explicitFix = JsonDocument.Parse("{\"REP-TEST\":{\"status\":\"Fixed\",\"note\":\"Resolved in v1.4.0\"}}");
applyStatuses.Invoke(null, [records, explicitFix.RootElement]);
Confirm("Only explicit report mappings change resolution", keptPending && records[0].Status == "Fixed"
    && records[0].ResolutionNote == "Resolved in v1.4.0", $"unrelated release kept pending={keptPending}");

Console.WriteLine($"Regression result: {observed}/17 passed"); Environment.ExitCode = observed == 17 ? 0 : 1;
sealed class ManualTime : TimeProvider
{
    private long _seconds;
    public override long TimestampFrequency => 1;
    public override long GetTimestamp() => _seconds;
    public void Advance(long seconds) => _seconds += seconds;
}
sealed class MockHandler : HttpMessageHandler
{
    public const string Error = "INVALID LANGUAGE PAIR";
    public int Requests { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        string body = Requests == 1
            ? "{\"sentences\":[{\"trans\":\"Hello\"}]}"
            : "{\"responseData\":{\"translatedText\":\"INVALID LANGUAGE PAIR\"},\"responseStatus\":400}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
sealed class PriceHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        bool streaming = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Contains("\"stream\":true");
        string body = streaming ? "data: {\"choices\":[{\"delta\":{\"content\":\"Harganya 900 dolar.\"}}]}\n\ndata: [DONE]\n" : "{\"choices\":[{\"message\":{\"content\":\"Harganya 900 dolar.\"}}]}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
