using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public const string Intro = "Every contribution helps fund development, testing, and releases. The developer personally thanks each buyer. Stable translation remains free.";
    public const string Closed = "Purchases and donations are currently closed.";
    public static IReadOnlyList<SupportOffer> Offers { get; } = Array.AsReadOnly(new SupportOffer[]
    {
        new("supporter", "Lifetime Supporter", 1999, "Supporter medal and a personal thank-you."),
        new("insider", "Lifetime Insider", 10000, "Insider medal, release archive, beta and approved internal builds, compatible experimental models, and priority support."),
        new("donation", "Donate", null, "Support development with any positive amount. Donations do not unlock a paid tier.")
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

/// <summary>Reads the consented public projections. The desktop never needs a service key.</summary>
public sealed class CommunityClient
{
    public const int PageSize = 50;
    private sealed record Endpoint(string Url, string PublishableKey, bool PreviewManualRequests);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient DefaultHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private readonly HttpClient _http;
    private readonly Uri? _rpcBase;
    private readonly string? _key;
    private readonly Uri? _authBase;
    public bool ManualRequestPreview => _authBase != null;

    public const string NotConfigured = "Online rankings aren’t connected in this build. Your usage on this device stays private.";
    public const string Unavailable = "Could not load online rankings. Check your connection and try again.";

    public CommunityClient(string dataDirectory, HttpClient? http = null)
    {
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
            _rpcBase = new Uri(url, "rest/v1/rpc/");
            _key = key;
            if (endpoint.PreviewManualRequests) _authBase = new Uri(url, "auth/v1/");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
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
}
