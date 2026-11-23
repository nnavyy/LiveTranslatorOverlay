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

// StreamingAgreement Local Agreement-2 word and CJK-grapheme stabilization
var agreement = new StreamingAgreement();
var step1 = agreement.Observe("Testing.");
var step2 = agreement.Observe("Testing one two");
var step3 = agreement.Observe("Testing one two three");
var finalStep = agreement.FinalizeSegment("Testing one two three.");
Check("Local Agreement-2 stabilizes English words progressively without premature periods",
    step1.CommittedText == "" && step1.ProvisionalText == "Testing."
    && step2.CommittedText == "Testing" && step2.ProvisionalText == "one two"
    && step3.CommittedText == "Testing one two" && step3.ProvisionalText == "three"
    && finalStep.CommittedText == "Testing one two three.",
    $"step2='{step2.CommittedText}|{step2.ProvisionalText}'; final='{finalStep.CommittedText}'");

agreement.Reset();
var cjk1 = agreement.Observe("今日は");
var cjk2 = agreement.Observe("今日はいい天気です");
var cjkFinal = agreement.FinalizeSegment("今日はいい天気です");
Check("Local Agreement-2 stabilizes CJK characters without inserting spaces",
    cjk2.CommittedText == "今日は" && cjkFinal.CommittedText == "今日はいい天気です",
    $"cjk2='{cjk2.CommittedText}+{cjk2.ProvisionalText}'; final='{cjkFinal.CommittedText}'");

// Hardware-adaptive model tier resolution
Check("Auto model tier selects LargeTurbo when GPU is available and Base on CPU",
    SpeechModelDownload.ResolveEffectiveTier(LocalSpeechModelTier.Auto, gpuAvailable: true) == LocalSpeechModelTier.LargeTurbo
    && SpeechModelDownload.ResolveEffectiveTier(LocalSpeechModelTier.Auto, gpuAvailable: false) == LocalSpeechModelTier.Base
    && SpeechModelDownload.ResolveEffectiveTier(LocalSpeechModelTier.Small, gpuAvailable: true) == LocalSpeechModelTier.Small,
    "auto tiering verified");

// Project domain, salted HWID fingerprint, ECDSA P-256 entitlements & login-optional policy
Check("ProjectDomainConfig defaults cleanly to livetranslator.app addresses",
    ProjectDomainConfig.DefaultDomain == "livetranslator.app"
    && ProjectDomainConfig.NoReplyAddress == "noreply@livetranslator.app"
    && ProjectDomainConfig.SupportAddress == "support@livetranslator.app",
    $"domain={ProjectDomainConfig.Domain}; noreply={ProjectDomainConfig.NoReplyAddress}");

object? prevHwSeed = AppContext.GetData("LiveTranslatorOverlay.HardwareSeed");
string acctRoot = Path.Combine(Path.GetTempPath(), "TranslatorAccountAudit-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(acctRoot);
try
{
    AppContext.SetData("LiveTranslatorOverlay.HardwareSeed", "audit-device-seed-A");
    var hwA1 = LiveTranslatorOverlay.Core.Security.HardwareFingerprint.GetCurrent(acctRoot);
    var hwA2 = LiveTranslatorOverlay.Core.Security.HardwareFingerprint.GetCurrent(acctRoot);
    AppContext.SetData("LiveTranslatorOverlay.HardwareSeed", "audit-device-seed-B");
    var hwB = LiveTranslatorOverlay.Core.Security.HardwareFingerprint.GetCurrent(acctRoot);
    Check("Salted HardwareFingerprint produces deterministic 64-hex SHA-256 and User#XXXXX tag",
        hwA1.FingerprintHash.Length == 64 && hwA1.FingerprintHash == hwA2.FingerprintHash
        && hwA1.GuestDisplayTag.StartsWith("User#", StringComparison.Ordinal) && hwA1.GuestDisplayTag.Length == 10
        && hwA1.FingerprintHash != hwB.FingerprintHash,
        $"tagA={hwA1.GuestDisplayTag}; tagB={hwB.GuestDisplayTag}");

    var (pubPem, privPem) = LiveTranslatorOverlay.Core.Security.EntitlementSignature.GenerateKeyPairPem();
    var now = DateTimeOffset.UtcNow;
    var signed = LiveTranslatorOverlay.Core.Security.EntitlementSignature.SignEntitlement(
        "donor@example.test", "insider", 10000, true, hwA1.FingerprintHash, now, now.AddDays(30), privPem);
    var tampered = signed with { ActiveTier = "supporter" };
    Check("ECDSA P-256 signed entitlement verifies device binding and rejects tampering, wrong HWID, and expiry",
        LiveTranslatorOverlay.Core.Security.EntitlementSignature.VerifyEntitlement(signed, pubPem, hwA1.FingerprintHash, now)
        && !LiveTranslatorOverlay.Core.Security.EntitlementSignature.VerifyEntitlement(signed, pubPem, hwB.FingerprintHash, now)
        && !LiveTranslatorOverlay.Core.Security.EntitlementSignature.VerifyEntitlement(signed, pubPem, hwA1.FingerprintHash, now.AddDays(31))
        && !LiveTranslatorOverlay.Core.Security.EntitlementSignature.VerifyEntitlement(tampered, pubPem, hwA1.FingerprintHash, now),
        "p256 verified");

    AppContext.SetData("LiveTranslatorOverlay.HardwareSeed", "audit-device-seed-A");
    var communityClient = new CommunityClient(acctRoot);
    var guestSupporterBlocked = await communityClient.SubmitSupportChoiceAsync("supporter", 0, "", CancellationToken.None);
    var guestInsiderBlocked = await communityClient.SubmitSupportChoiceAsync("insider", 0, "", CancellationToken.None);
    var guestDonateOk = await communityClient.SubmitSupportChoiceAsync("donation", 500, "Guest support", CancellationToken.None);
    var guestLedger = communityClient.GetGuestDonations();
    Check("License badges require login while Donate works without login as User#XXXXX",
        !guestSupporterBlocked.Success && guestSupporterBlocked.Message.Contains("Login required", StringComparison.OrdinalIgnoreCase)
        && !guestInsiderBlocked.Success && guestInsiderBlocked.Message.Contains("Login required", StringComparison.OrdinalIgnoreCase)
        && guestDonateOk.Success && guestDonateOk.Message.Contains(hwA1.GuestDisplayTag, StringComparison.Ordinal)
        && guestLedger != null && guestLedger.TotalDonatedCents == 500 && guestLedger.GuestTag == hwA1.GuestDisplayTag,
        $"guestTag={guestLedger?.GuestTag}; guestCents={guestLedger?.TotalDonatedCents}");

    var otpRes = await communityClient.SendLoginOtpAsync("donor@example.test", CancellationToken.None);
    var loginRes = await communityClient.VerifyLoginOtpAsync("donor@example.test", "123456", CancellationToken.None);
    var donateRes = await communityClient.SubmitSupportChoiceAsync("donation", 1500, "Indonesian pack", CancellationToken.None);
    var insiderRes = await communityClient.SubmitSupportChoiceAsync("insider", 0, "Beta tester", CancellationToken.None);
    var reloaded = AccountSessionStore.Load(acctRoot);
    Check("Account login, custom donation, and $100 Insider beta unlock persist with solo-dev message & HWID",
        otpRes.Success && loginRes.Success && donateRes.Success && insiderRes.Success
        && donateRes.Message.Contains("solo developer", StringComparison.OrdinalIgnoreCase)
        && reloaded != null && reloaded.TotalDonatedCents == 11500
        && reloaded.BetaAccessUnlocked && reloaded.ActiveTier == "insider"
        && reloaded.HardwareFingerprintHash == hwA1.FingerprintHash
        && reloaded.History.Length == 2,
        $"tier={reloaded?.ActiveTier}; total={reloaded?.TotalDonatedCents}; history={reloaded?.History.Length}");

    string onlineRoot = Path.Combine(acctRoot, "online-beta-gate");
    Directory.CreateDirectory(onlineRoot);
    File.WriteAllText(Path.Combine(onlineRoot, "community.json"), """{"url":"https://example.supabase.co/","publishableKey":"sb_publishable_testkey","previewManualRequests":true}""");
    using var mockHttp = new HttpClient(new GatedBetaMockHandler());
    var gatedClient = new CommunityClient(onlineRoot, mockHttp);
    var signedOutBeta = await gatedClient.FetchGatedBetaManifestAsync("win-x64", CancellationToken.None);
    var freeLogin = await gatedClient.VerifyLoginOtpAsync("free@example.test", "123456", CancellationToken.None);
    var freeBeta = await gatedClient.FetchGatedBetaManifestAsync("win-x64", CancellationToken.None);
    var insiderLogin = await gatedClient.VerifyLoginOtpAsync("insider@example.test", "123456", CancellationToken.None);
    var insiderBeta = await gatedClient.FetchGatedBetaManifestAsync("win-x64", CancellationToken.None);
    var parsedBeta = insiderBeta.Allowed && insiderBeta.ManifestJson != null
        ? LiveTranslatorOverlay.Core.Update.StableRelease.Read(insiderBeta.ManifestJson, "win-x64", "beta")
        : null;
    Check("Private beta feed denies signed-out and free users and serves signed manifest to verified Insider",
        !signedOutBeta.Allowed && freeLogin.Success && !freeBeta.Allowed
        && insiderLogin.Success && insiderBeta.Allowed && parsedBeta != null && parsedBeta.Version == new Version(1, 1, 2),
        $"freeAllowed={freeBeta.Allowed}; insiderAllowed={insiderBeta.Allowed}; betaVer={parsedBeta?.Version}");

    var localProfile = new UserProfileState("AkungTester", true, true, true, false, DateTimeOffset.UtcNow);
    UserProfileStore.Save(acctRoot, localProfile);
    var reloadedProfile = UserProfileStore.Load(acctRoot);
    var previewAll = communityClient.BuildLocalPreviewPage(CommunityBoard.AllTime, TimeSpan.FromMinutes(12));
    var previewSupporters = communityClient.BuildLocalPreviewPage(CommunityBoard.Supporters, TimeSpan.Zero, "AkungTester");
    UserProfileStore.Clear(acctRoot);
    var clearedProfile = UserProfileStore.Load(acctRoot);
    Check("User profile store and local preview rankings work offline across All and Supporters boards",
        reloadedProfile != null && reloadedProfile.Nickname == "AkungTester" && reloadedProfile.SyncUsage
        && previewAll.Available && previewAll.Entries is [{ Nickname: "AkungTester", Score: 720 }]
        && previewSupporters.Available && previewSupporters.Entries is [{ Nickname: "AkungTester", SupportCents: 12000 }]
        && clearedProfile == null,
        $"allScore={previewAll.Entries.FirstOrDefault()?.Score}; supCents={previewSupporters.Entries.FirstOrDefault()?.SupportCents}");

    OwnerAdminConfigStore.Save(acctRoot, new OwnerAdminConfig("https://example.supabase.co/", "service_role_test"));
    var ownerCfg = OwnerAdminConfigStore.Load(acctRoot);
    OwnerAdminConfigStore.Clear(acctRoot);
    var clearedOwnerCfg = OwnerAdminConfigStore.Load(acctRoot);
    Check("Owner admin config store persists and clears encrypted Supabase credentials",
        ownerCfg != null && ownerCfg.SupabaseUrl == "https://example.supabase.co/" && ownerCfg.ServiceRoleKey == "service_role_test"
        && clearedOwnerCfg == null,
        $"loadedUrl={ownerCfg?.SupabaseUrl}; cleared={clearedOwnerCfg == null}");
}
finally
{
    AppContext.SetData("LiveTranslatorOverlay.HardwareSeed", prevHwSeed);
    if (Directory.Exists(acctRoot)) Directory.Delete(acctRoot, recursive: true);
}

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

sealed class GatedBetaMockHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        string path = request.RequestUri?.AbsolutePath ?? "";
        string reqBody = request.Content != null ? await request.Content.ReadAsStringAsync(token) : "";
        if (path.EndsWith("/auth/v1/verify", StringComparison.Ordinal))
        {
            string tok = reqBody.Contains("insider@example.test", StringComparison.Ordinal) ? "tok-insider" : "tok-free";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"access_token":"{{tok}}"}""") };
        }
        string auth = request.Headers.Authorization?.Parameter ?? "";
        if (path.EndsWith("/rest/v1/rpc/lt_my_account_summary", StringComparison.Ordinal))
        {
            if (auth == "tok-insider")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"email":"insider@example.test","activeTier":"insider","verifiedTotalCents":10000,"betaAccessUnlocked":true,"history":[]}""") };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"email":"free@example.test","activeTier":"free","verifiedTotalCents":0,"betaAccessUnlocked":false,"history":[]}""") };
        }
        if (path.EndsWith("/functions/v1/beta-release", StringComparison.Ordinal))
        {
            if (auth != "tok-insider")
                return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("""{"error":"Beta access denied."}""") };
            string manifestJson = ("""
                {"line":"ml","channel":"beta","version":"1.1.2","packages":{
                  "win-x64":{"url":"https://example.supabase.co/storage/v1/object/sign/beta-releases/v1.1.2/win-x64/LiveTranslatorOverlay-Windows-x64.zip?token=abc","size":4096,"sha256":"__HASH__"}
                }}
                """).Replace("__HASH__", new string('A', 64));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestJson) };
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
