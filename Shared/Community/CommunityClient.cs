using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveTranslatorOverlay.Core.Security;

namespace LiveTranslatorOverlay.Core;

public sealed record SupportOffer(string Id, string Title, int? Cents, string Description)
{
    public string Price => Cents is int cents
        ? "US$" + (cents / 100m).ToString(cents % 100 == 0 ? "0" : "0.00", CultureInfo.InvariantCulture) + " once"
        : "Choose an amount";
    public string Label => $"{Title} · {Price}";
}

public static class SupportCatalog
{
    public const string Intro = "Live Translator Overlay is free under the MIT License. Core features stay free with no login required; sign in only for public Community Rankings or Lifetime tier verification.";
    public const string Closed = "Purchases and donations are currently closed.";
    public const string IndependenceNote = "Supporter and Insider are independent tiers — selecting both is US$119.99 total.";
    public const string AvailabilityNote = "Checkout is in staging preview; no payment is charged in this build.";
    public const string SoloDeveloperNote = "Every purchase or donation helps this solo developer support more languages across Windows and macOS.";

    public static IReadOnlyList<SupportOffer> Offers { get; } = Array.AsReadOnly(new SupportOffer[]
    {
        new("supporter", "Lifetime Supporter", 1999, "One-time support (sign-in required). Recognizes your account on Community Rankings with a personal thank-you."),
        new("insider", "Lifetime Insider", 10000, "One-time support (sign-in required). Unlocks developer-verified Beta access, experimental models, and priority support."),
        new("donation", "Donate", null, "Voluntary contribution of any positive amount. Works without login (listed as User#XXXXX on Supporters).")
    });
}

public enum CommunityBoard { AllTime, ThisMonth, Supporters }

public sealed record CommunityRank(int Rank, string Nickname, long Score, string[]? Medals, long? SupportCents = null)
{
    public string ScoreLabel(CommunityBoard board) => board == CommunityBoard.Supporters
        ? FormatSupport(Score)
        : Score < 60 ? $"{Score} s" : GuestAudioUsage.Format(TimeSpan.FromSeconds(Score));

    public string SupportLabel => SupportCents is long cents ? FormatSupport(cents) : "No public total";

    private static string FormatSupport(long cents) => "US$" + (cents / 100m).ToString("N2", CultureInfo.InvariantCulture);
}

public sealed record CommunityPage(bool Available, CommunityRank[] Entries, string Message)
{
    public IReadOnlyList<CommunityRank> PodiumEntries(string search, int offset) =>
        Available && offset == 0 && string.IsNullOrWhiteSpace(search) ? Entries.Take(3).ToArray() : [];
}

public sealed record AccountActionResult(bool Success, string Message, AccountSessionState? Session = null);

/// <summary>Reads consented public projections and manages email OTP login, licenses, and login-optional User#XXXXX donations.</summary>
public sealed class CommunityClient
{
    public const int PageSize = 50;
    private sealed record Endpoint(string Url, string PublishableKey, bool PreviewManualRequests);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient DefaultHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private readonly string _dataDirectory;
    private readonly HttpClient _http;
    private readonly Uri? _supabaseOrigin;
    private readonly Uri? _rpcBase;
    private readonly string? _key;
    private readonly Uri? _authBase;
    public bool ManualRequestPreview => _authBase != null;
    public bool HasOnlineAccountService => _authBase != null && _rpcBase != null;

    public const string NotConfigured = "Online rankings aren’t connected in this build. Your usage on this device stays private.";
    public const string Unavailable = "Could not load online rankings. Check your connection and try again.";

    public CommunityClient(string dataDirectory, HttpClient? http = null)
    {
        _dataDirectory = dataDirectory;
        _http = http ?? DefaultHttp;
        try
        {
            string path = Path.Combine(dataDirectory, "community.json");
            if (!File.Exists(path)) return;
            var endpoint = JsonSerializer.Deserialize<Endpoint>(File.ReadAllText(path), Json);
            if (endpoint?.PublishableKey is not { } key || !key.StartsWith("sb_publishable_", StringComparison.Ordinal) ||
                !Uri.TryCreate(endpoint.Url, UriKind.Absolute, out var url) || url.UserInfo.Length > 0 ||
                url.AbsolutePath != "/" || url.Query.Length > 0 || url.Fragment.Length > 0 ||
                !(url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp && url.IsLoopback)) return;
            _supabaseOrigin = url;
            _rpcBase = new Uri(url, "rest/v1/rpc/");
            _key = key;
            if (endpoint.PreviewManualRequests) _authBase = new Uri(url, "auth/v1/");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }

    public HardwareIdentity GetDeviceIdentity() => HardwareFingerprint.GetCurrent(_dataDirectory);

    public AccountSessionState? GetCachedSession() => AccountSessionStore.Load(_dataDirectory);

    public GuestDonationLedgerState? GetGuestDonations() => GuestDonationLedgerStore.Load(_dataDirectory);

    public UserProfileState? GetCachedProfile() => UserProfileStore.Load(_dataDirectory);

    public async Task<AccountActionResult> SaveProfileAsync(
        string nickname,
        bool syncUsage,
        bool publicUsage,
        bool publicSupport,
        bool showMedals,
        CancellationToken token)
    {
        var session = AccountSessionStore.Load(_dataDirectory);
        if (session == null)
            return new(false, "Sign in on the Support tab before saving a public ranking profile.");

        string clean = (nickname ?? "").Trim();
        if (clean.Length is < 3 or > 24)
            return new(false, "Choose a nickname between 3 and 24 characters.");

        var localProfile = new UserProfileState(clean, syncUsage, publicUsage, publicSupport, showMedals, DateTimeOffset.UtcNow);
        if (!HasOnlineAccountService || session.IsLocalStaging || string.IsNullOrWhiteSpace(session.AccessToken) || session.AccessToken == "local-staging-session")
        {
            UserProfileStore.Save(_dataDirectory, localProfile);
            return new(true, $"Profile saved locally ({clean}). Connect a community server to publish to live rankings.", session);
        }

        try
        {
            using var response = await PostPurchaseAsync(
                new Uri(_rpcBase!, "lt_save_profile"),
                new
                {
                    p_nickname = clean,
                    p_sync_usage = syncUsage,
                    p_public_usage = publicUsage,
                    p_public_support = publicSupport,
                    p_show_medals = showMedals
                },
                session.AccessToken,
                token).ConfigureAwait(false);

            if (response?.IsSuccessStatusCode == true)
            {
                var remote = await response.Content.ReadFromJsonAsync<RemoteProfileResponse>(Json, token).ConfigureAwait(false);
                if (remote?.Configured == true)
                {
                    var synced = new UserProfileState(
                        remote.Nickname ?? clean,
                        remote.SyncUsage,
                        remote.PublicUsage,
                        remote.PublicSupport,
                        remote.ShowMedals,
                        DateTimeOffset.UtcNow);
                    UserProfileStore.Save(_dataDirectory, synced);
                    return new(true, $"Public ranking profile updated ({synced.Nickname}).", session);
                }
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException ||
                                      error is OperationCanceledException && !token.IsCancellationRequested) { }

        return new(false, "Could not save profile to the ranking server (choose a different nickname or sign in again).");
    }

    public async Task<UserProfileState?> RefreshProfileAsync(CancellationToken token)
    {
        var session = AccountSessionStore.Load(_dataDirectory);
        if (session == null || !HasOnlineAccountService || session.IsLocalStaging || string.IsNullOrWhiteSpace(session.AccessToken))
            return UserProfileStore.Load(_dataDirectory);

        try
        {
            using var response = await PostPurchaseAsync(new Uri(_rpcBase!, "lt_my_profile"), new { }, session.AccessToken, token).ConfigureAwait(false);
            if (response?.IsSuccessStatusCode == true)
            {
                var remote = await response.Content.ReadFromJsonAsync<RemoteProfileResponse>(Json, token).ConfigureAwait(false);
                if (remote?.Configured == true && !string.IsNullOrWhiteSpace(remote.Nickname))
                {
                    var synced = new UserProfileState(remote.Nickname, remote.SyncUsage, remote.PublicUsage, remote.PublicSupport, remote.ShowMedals, DateTimeOffset.UtcNow);
                    UserProfileStore.Save(_dataDirectory, synced);
                    return synced;
                }
            }
        }
        catch { }

        return UserProfileStore.Load(_dataDirectory);
    }

    public CommunityPage BuildLocalPreviewPage(CommunityBoard board, TimeSpan localUsage, string search = "")
    {
        var session = AccountSessionStore.Load(_dataDirectory);
        var profile = UserProfileStore.Load(_dataDirectory);
        var hw = GetDeviceIdentity();
        var guestDonations = GuestDonationLedgerStore.Load(_dataDirectory);

        string displayName = !string.IsNullOrWhiteSpace(profile?.Nickname)
            ? profile.Nickname
            : session?.DisplayName ?? hw.GuestDisplayTag;

        long score = board == CommunityBoard.Supporters
            ? (session?.TotalDonatedCents ?? 0) + (guestDonations?.TotalDonatedCents ?? 0)
            : Math.Max(0, (long)Math.Floor(localUsage.TotalSeconds));

        if (score <= 0)
            return new(false, [], "No local usage or contributions recorded on this device yet.");

        string[]? medals = session?.ActiveTier switch
        {
            "insider" => ["Insider"],
            "supporter" => ["Supporter"],
            _ => null
        };
        long? supportCents = (session?.TotalDonatedCents ?? 0) + (guestDonations?.TotalDonatedCents ?? 0);
        if (supportCents == 0) supportCents = null;

        var entry = new CommunityRank(1, displayName, score, medals, supportCents);
        string q = (search ?? "").Trim();
        var filtered = string.IsNullOrEmpty(q) || entry.Nickname.Contains(q, StringComparison.OrdinalIgnoreCase)
            ? new[] { entry }
            : [];
        return new(true, filtered, filtered.Length == 0 ? "No local entries match that search." : "Showing local device standings (connect community.json for global rankings).");
    }

    public void SignOut() => AccountSessionStore.Clear(_dataDirectory);

    public async Task<AccountActionResult> SendLoginOtpAsync(string email, CancellationToken token)
    {
        email = (email ?? "").Trim();
        if (!IsValidEmail(email))
            return new(false, "Enter a valid email address.");

        if (_authBase == null)
        {
            return new(true, $"Local staging login active (production OTPs send from {ProjectDomainConfig.NoReplyAddress}). Enter any 6 digits (e.g. 123456) to sign in for Community Rankings and lifetime support tiers.");
        }

        using var response = await PostPurchaseAsync(new Uri(_authBase, "otp"), new { email, create_user = true }, null, token).ConfigureAwait(false);
        return response?.IsSuccessStatusCode == true
            ? new(true, $"A six-digit sign-in code was sent from {ProjectDomainConfig.NoReplyAddress} to {email}.")
            : new(false, "Could not send a sign-in code. Check the address and try again shortly.");
    }

    public async Task<AccountActionResult> VerifyLoginOtpAsync(string email, string code, CancellationToken token)
    {
        email = (email ?? "").Trim();
        code = (code ?? "").Trim();
        if (!IsValidEmail(email))
            return new(false, "Enter a valid email address.");
        if (code.Length != 6 || !code.All(char.IsAsciiDigit))
            return new(false, "Enter the six-digit code.");

        string displayName = email.Split('@')[0];
        if (string.IsNullOrWhiteSpace(displayName)) displayName = "Member";
        var hw = GetDeviceIdentity();

        if (_authBase == null || _rpcBase == null)
        {
            var existing = AccountSessionStore.Load(_dataDirectory);
            var localSession = existing != null && string.Equals(existing.Email, email, StringComparison.OrdinalIgnoreCase)
                ? existing with { AccessToken = "local-staging-session", IsLocalStaging = true, SignedInUtc = DateTimeOffset.UtcNow, HardwareFingerprintHash = hw.FingerprintHash }
                : new AccountSessionState(email, displayName, "local-staging-session", "free", 0, false, true, DateTimeOffset.UtcNow, [], hw.FingerprintHash);
            AccountSessionStore.Save(_dataDirectory, localSession);
            return new(true, $"Signed in as {email} (device {hw.GuestDisplayTag} bound).", localSession);
        }

        try
        {
            using var verification = await PostPurchaseAsync(new Uri(_authBase, "verify"), new { email, token = code, type = "email" }, null, token).ConfigureAwait(false);
            if (verification?.IsSuccessStatusCode != true)
                return new(false, "Could not verify your code. Request a new one and try again.");

            var session = await verification.Content.ReadFromJsonAsync<EmailSession>(Json, token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(session?.AccessToken) ||
                !session.AccessToken.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
                return new(false, "Could not verify your code.");

            var remoteState = await FetchRemoteAccountStateAsync(email, displayName, session.AccessToken, hw.FingerprintHash, token).ConfigureAwait(false);
            AccountSessionStore.Save(_dataDirectory, remoteState);
            return new(true, $"Signed in as {email}.", remoteState);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException ||
                                      error is OperationCanceledException && !token.IsCancellationRequested)
        {
            return new(false, "Could not complete sign-in. Check your network connection and retry.");
        }
    }

    public async Task<AccountActionResult> SubmitSupportChoiceAsync(string offerId, long customDonationCents, string note, CancellationToken token)
    {
        var offer = SupportCatalog.Offers.FirstOrDefault(o => o.Id == offerId);
        if (offer == null)
            return new(false, "Choose Lifetime Supporter (US$19.99), Lifetime Insider (US$100), or Donate.");

        var session = AccountSessionStore.Load(_dataDirectory);
        // License tiers & badges ALWAYS require signed-in account; custom donations are login-optional (User#XXXXX).
        if (session == null && offer.Id != "donation")
        {
            return new(false, $"Login required for {offer.Title}. Sign in with your email first to claim your lifetime support tier (donations work without login as User#XXXXX).");
        }

        long cents = offer.Cents ?? customDonationCents;
        if (cents <= 0 || cents > 10_000_000)
            return new(false, "Enter a positive support amount up to US$100,000.");

        var hw = GetDeviceIdentity();

        if (session == null)
        {
            return await SubmitGuestDonationAsync(cents, note, hw, token).ConfigureAwait(false);
        }

        if (!HasOnlineAccountService || session.IsLocalStaging)
        {
            string entryId = $"loc-{Guid.NewGuid():N}"[..16];
            var updated = AccountSessionStore.RecordSupport(
                _dataDirectory,
                session with { HardwareFingerprintHash = hw.FingerprintHash },
                entryId,
                offer.Id,
                offer.Title,
                cents,
                "local-staging",
                offer.Id == "donation" ? "tracked-staging" : "staged-preview",
                countsTowardTier: offer.Id is "supporter" or "insider");

            return new(
                true,
                $"Recorded {offer.Title} ({AccountSessionStore.FormatUsd(cents)}) in staging ledger [{entryId}]. No real payment was charged. {SupportCatalog.SoloDeveloperNote}",
                updated);
        }

        try
        {
            using var response = await PostPurchaseAsync(
                new Uri(_rpcBase!, "lt_submit_support_request"),
                new { p_offer = offer.Id, p_cents = (int)cents, p_note = (note ?? "").Trim() },
                session.AccessToken,
                token).ConfigureAwait(false);

            if (response?.IsSuccessStatusCode == true)
            {
                var summary = await response.Content.ReadFromJsonAsync<SupportRequestRpcResponse>(Json, token).ConfigureAwait(false);
                var updated = AccountSessionStore.RecordSupport(
                    _dataDirectory,
                    session with { HardwareFingerprintHash = hw.FingerprintHash },
                    summary?.RequestId ?? Guid.NewGuid().ToString("D"),
                    offer.Id,
                    offer.Title,
                    cents,
                    "supabase",
                    summary?.Status ?? "pending",
                    countsTowardTier: false);

                return new(
                    true,
                    $"Recorded {offer.Title} ({AccountSessionStore.FormatUsd(cents)}) · Request {summary?.RequestId}. Verification awaits settled payment. {SupportCatalog.SoloDeveloperNote}",
                    updated);
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException ||
                                      error is OperationCanceledException && !token.IsCancellationRequested) { }

        return new(false, "Could not submit your support request. Sign in again if your code session expired.");
    }

    private async Task<AccountActionResult> SubmitGuestDonationAsync(long cents, string note, HardwareIdentity hw, CancellationToken token)
    {
        if (_rpcBase != null)
        {
            try
            {
                using var response = await PostPurchaseAsync(
                    new Uri(_rpcBase, "lt_submit_guest_donation"),
                    new { p_cents = (int)cents, p_hwid_hash = hw.FingerprintHash, p_note = (note ?? "").Trim() },
                    null,
                    token).ConfigureAwait(false);

                if (response?.IsSuccessStatusCode == true)
                {
                    var summary = await response.Content.ReadFromJsonAsync<GuestDonationRpcResponse>(Json, token).ConfigureAwait(false);
                    string reqId = summary?.RequestId ?? Guid.NewGuid().ToString("D");
                    string tag = string.IsNullOrWhiteSpace(summary?.GuestTag) ? hw.GuestDisplayTag : summary.GuestTag;
                    GuestDonationLedgerStore.RecordDonation(_dataDirectory, hw, reqId, cents, "supabase-guest", summary?.Status ?? "pending");
                    return new(
                        true,
                        $"Guest donation recorded ({AccountSessionStore.FormatUsd(cents)}) · Request {reqId} · Anonymous leaderboard handle: {tag}. No login required. {SupportCatalog.SoloDeveloperNote}");
                }
            }
            catch (Exception error) when (error is HttpRequestException or IOException or JsonException ||
                                          error is OperationCanceledException && !token.IsCancellationRequested) { }

            return new(false, "Could not submit guest donation right now (check connection or daily device limit).");
        }

        string localId = $"gst-{Guid.NewGuid():N}"[..16];
        GuestDonationLedgerStore.RecordDonation(_dataDirectory, hw, localId, cents, "local-staging", "guest-tracked");
        return new(
            true,
            $"Guest donation recorded ({AccountSessionStore.FormatUsd(cents)}) as {hw.GuestDisplayTag} [{localId}]. No login required · no real payment charged. {SupportCatalog.SoloDeveloperNote}");
    }

    private async Task<AccountSessionState?> TryFetchRemoteAccountStateAsync(string email, string displayName, string accessToken, string hwHash, CancellationToken token)
    {
        try
        {
            using var response = await PostPurchaseAsync(new Uri(_rpcBase!, "lt_my_account_summary"), new { }, accessToken, token).ConfigureAwait(false);
            if (response?.IsSuccessStatusCode == true)
            {
                var remote = await response.Content.ReadFromJsonAsync<RemoteAccountSummary>(Json, token).ConfigureAwait(false);
                if (remote != null)
                {
                    var history = (remote.History ?? []).Select(h => new SupportLedgerEntry(
                        h.Id ?? "",
                        h.Offer ?? "donation",
                        SupportCatalog.Offers.FirstOrDefault(o => o.Id == h.Offer)?.Title ?? (h.Offer ?? "Support"),
                        h.Cents,
                        "supabase",
                        h.Status ?? "pending",
                        h.CreatedAt)).ToArray();

                    return new AccountSessionState(
                        string.IsNullOrWhiteSpace(remote.Email) ? email : remote.Email,
                        displayName,
                        accessToken,
                        remote.ActiveTier ?? "free",
                        remote.VerifiedTotalCents,
                        remote.BetaAccessUnlocked,
                        false,
                        DateTimeOffset.UtcNow,
                        history,
                        hwHash);
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<AccountSessionState> FetchRemoteAccountStateAsync(string email, string displayName, string accessToken, string hwHash, CancellationToken token) =>
        await TryFetchRemoteAccountStateAsync(email, displayName, accessToken, hwHash, token).ConfigureAwait(false)
        ?? new AccountSessionState(email, displayName, accessToken, "free", 0, false, false, DateTimeOffset.UtcNow, [], hwHash);

    public async Task<AccountActionResult> RefreshAccountStatusAsync(CancellationToken token)
    {
        var session = AccountSessionStore.Load(_dataDirectory);
        if (session == null)
            return new(false, "Sign in first to check your developer-verified access status.");

        if (!HasOnlineAccountService || session.IsLocalStaging)
            return new(true, $"Local staging account ({session.TierDisplayName}). Beta channel {(session.BetaAccessUnlocked ? "unlocked" : "locked")}.", session);

        var hw = GetDeviceIdentity();
        var updated = await TryFetchRemoteAccountStateAsync(session.Email, session.DisplayName, session.AccessToken, hw.FingerprintHash, token).ConfigureAwait(false);
        if (updated == null)
            return new(false, "Could not refresh account status right now (check connection or sign in again).", session);

        AccountSessionStore.Save(_dataDirectory, updated);
        return new(
            true,
            $"Account refreshed: {updated.TierDisplayName} · Beta access {(updated.BetaAccessUnlocked ? "unlocked" : "pending developer verification")}.",
            updated);
    }

    public async Task<(bool Allowed, string? ManifestJson, string Message)> FetchGatedBetaManifestAsync(string runtime, CancellationToken token)
    {
        var session = AccountSessionStore.Load(_dataDirectory);
        if (session == null)
            return (false, null, "Sign in with a developer-verified US$100 Lifetime Insider account to access Beta builds.");

        if (!session.BetaAccessUnlocked)
            return (false, null, "Beta channel is locked until the developer verifies your US$100 Lifetime Insider grant. Click 'Refresh access status' after approval.");

        if (_supabaseOrigin == null || string.IsNullOrWhiteSpace(_key))
            return (false, null, "Beta download server is not configured on this device (community.json missing).");

        if (session.IsLocalStaging || string.IsNullOrWhiteSpace(session.AccessToken) || session.AccessToken == "local-staging-session")
            return (false, null, "Sign in to an online community account to download private Beta builds.");

        try
        {
            var hw = GetDeviceIdentity();
            using var response = await PostPurchaseAsync(
                new Uri(_supabaseOrigin, "functions/v1/beta-release"),
                new { runtime, hwidHash = hw.FingerprintHash },
                session.AccessToken,
                token).ConfigureAwait(false);

            if (response == null)
                return (false, null, "Could not reach the Beta download server.");

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                return (false, null, "Beta access denied by server (verified US$100 Lifetime Insider grant required).");

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return (false, null, "No beta 1.x release published for this platform yet.");

            if (!response.IsSuccessStatusCode)
                return (false, null, $"Beta server returned HTTP {(int)response.StatusCode}.");

            string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return (true, body, "Beta release verified.");
        }
        catch (Exception error) when (error is HttpRequestException or IOException ||
                                      error is OperationCanceledException && !token.IsCancellationRequested)
        {
            return (false, null, "Could not reach the Beta download server.");
        }
    }

    public async Task<CommunityPage> ReadAsync(CommunityBoard board, string search = "", int offset = 0, CancellationToken token = default)
    {
        if (_rpcBase == null) return new(false, [], NotConfigured);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_rpcBase, "lt_community_board"));
            request.Headers.Add("apikey", _key);
            var boardName = board switch { CommunityBoard.ThisMonth => "month", CommunityBoard.Supporters => "supporters", _ => "all" };
            request.Content = JsonContent.Create(new { p_board = boardName, p_search = search.Trim(), p_offset = offset });
            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(false, [], Unavailable);
            var result = await response.Content.ReadFromJsonAsync<BoardResponse>(Json, token).ConfigureAwait(false);
            if (result == null) return new(false, [], Unavailable);
            if (!result.Open) return new(false, [], "Public rankings haven’t opened yet.");
            var entries = result.Entries ?? [];
            if (entries.Length > PageSize || entries.Any(row => row == null || row.Rank <= 0 ||
                row.Nickname is not { Length: > 0 and <= 64 } || row.Score <= 0 || row.SupportCents < 0 ||
                (board != CommunityBoard.Supporters && row.Score >= (long)TimeSpan.MaxValue.TotalSeconds)))
                return new(false, [], Unavailable);
            if (board == CommunityBoard.Supporters)
                entries = entries.Select(row => row with { SupportCents = row.Score }).ToArray();
            return new(true, entries, entries.Length == 0 ? "No matching entries." : "");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            return new(false, [], Unavailable);
        }
    }

    public async Task<string> SendPurchaseCodeAsync(string email, CancellationToken token)
    {
        if (_authBase == null) return SupportCatalog.Closed;
        if (!IsValidEmail(email)) return "Enter your staging account email.";
        using var response = await PostPurchaseAsync(new Uri(_authBase, "otp"), new { email, create_user = false }, null, token).ConfigureAwait(false);
        return response?.IsSuccessStatusCode == true
            ? "If the staging account exists, a code was sent. Check your email."
            : "Could not send a code. Check the address and wait before trying again.";
    }

    public async Task<string> RequestManualPurchaseAsync(string email, string code, string offer, CancellationToken token)
    {
        if (_authBase == null || _rpcBase == null) return SupportCatalog.Closed;
        if (!IsValidEmail(email)) return "Enter your staging account email.";
        if (code.Length != 6 || !code.All(char.IsAsciiDigit)) return "Enter the six-digit code from your email.";
        if (!SupportCatalog.Offers.Any(item => item.Id == offer && item.Cents.HasValue)) return "Choose a lifetime offer.";
        try
        {
            using var verification = await PostPurchaseAsync(new Uri(_authBase, "verify"), new { email, token = code, type = "email" }, null, token).ConfigureAwait(false);
            if (verification?.IsSuccessStatusCode != true) return "Could not verify your code. Request a new one and try again.";
            var session = await verification.Content.ReadFromJsonAsync<EmailSession>(Json, token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(session?.AccessToken) ||
                !session.AccessToken.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
                return "Could not verify your code.";
            using var request = await PostPurchaseAsync(new Uri(_rpcBase, "lt_request_manual_purchase"), new { p_offer = offer }, session.AccessToken, token).ConfigureAwait(false);
            if (request?.IsSuccessStatusCode == true)
            {
                var id = await request.Content.ReadFromJsonAsync<Guid>(Json, token).ConfigureAwait(false);
                if (id != Guid.Empty) return $"Pending request {id}. No payment was taken.";
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException ||
            error is OperationCanceledException && !token.IsCancellationRequested) { }
        return "Could not confirm the request. Get a new code and retry. A pending request will be reused.";
    }

    private static bool IsValidEmail(string email) =>
        email.Length <= 254 && System.Net.Mail.MailAddress.TryCreate(email, out var address) && address.Address == email;

    private async Task<HttpResponseMessage?> PostPurchaseAsync(Uri endpoint, object body, string? accessToken, CancellationToken token)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("apikey", _key);
            if (accessToken != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(body);
            return await _http.SendAsync(request, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            return null;
        }
    }

    private sealed record EmailSession([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record BoardResponse([property: JsonPropertyName("open")] bool Open, CommunityRank[]? Entries);
    private sealed record SupportRequestRpcResponse([property: JsonPropertyName("requestId")] string RequestId, [property: JsonPropertyName("status")] string Status);
    private sealed record GuestDonationRpcResponse(
        [property: JsonPropertyName("requestId")] string RequestId,
        [property: JsonPropertyName("guestTag")] string GuestTag,
        [property: JsonPropertyName("status")] string Status);
    private sealed record RemoteHistoryRow(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("offer")] string? Offer,
        [property: JsonPropertyName("cents")] long Cents,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);
    private sealed record RemoteAccountSummary(
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("activeTier")] string? ActiveTier,
        [property: JsonPropertyName("verifiedTotalCents")] long VerifiedTotalCents,
        [property: JsonPropertyName("betaAccessUnlocked")] bool BetaAccessUnlocked,
        [property: JsonPropertyName("history")] RemoteHistoryRow[]? History);
    private sealed record RemoteProfileResponse(
        [property: JsonPropertyName("configured")] bool Configured,
        [property: JsonPropertyName("nickname")] string? Nickname,
        [property: JsonPropertyName("syncUsage")] bool SyncUsage,
        [property: JsonPropertyName("publicUsage")] bool PublicUsage,
        [property: JsonPropertyName("publicSupport")] bool PublicSupport,
        [property: JsonPropertyName("showMedals")] bool ShowMedals);
}
