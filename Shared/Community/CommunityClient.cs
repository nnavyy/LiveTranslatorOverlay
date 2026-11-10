using System.Globalization;
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
    public const string Closed = "Purchases and donations are closed until manual payment verification is ready.";
    public static IReadOnlyList<SupportOffer> Offers { get; } = Array.AsReadOnly(new SupportOffer[]
    {
        new("supporter", "Lifetime Supporter", 1999, "Supporter medal and a personal thank-you."),
        new("insider", "Lifetime Insider", 10000, "Insider medal, release archive, beta and approved internal builds, compatible experimental models, and priority support."),
        new("donation", "Donate", null, "Support development with any positive amount. Donations do not unlock a paid tier.")
    });
}

public enum CommunityBoard { AllTime, ThisMonth, Supporters }

public sealed record CommunityRank(int Rank, string Nickname, long Score, string[]? Medals)
{
    public string Format(CommunityBoard board)
    {
        string score = board == CommunityBoard.Supporters
            ? "US$" + (Score / 100m).ToString("N2", CultureInfo.InvariantCulture)
            : GuestAudioUsage.Format(TimeSpan.FromSeconds(Score));
        return $"{Rank}.  {Nickname}  ·  {score}" + (Medals is not { Length: > 0 } ? "" : $"  ·  {string.Join(" and ", Medals)}");
    }
}

public sealed record CommunityPage(bool Available, CommunityRank[] Entries, string Message);

/// <summary>Reads the consented public projections. The desktop never needs a service key.</summary>
public sealed class CommunityClient
{
    public const int PageSize = 50;
    private sealed record Endpoint(string Url, string PublishableKey);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient DefaultHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private readonly HttpClient _http;
    private readonly Uri? _rpcBase;
    private readonly string? _key;

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
                row.Nickname is not { Length: > 0 and <= 64 } || row.Score <= 0 ||
                (board != CommunityBoard.Supporters && row.Score >= (long)TimeSpan.MaxValue.TotalSeconds)))
                return new(false, [], Unavailable);
            return new(true, entries, entries.Length == 0 ? "No matching entries." : "");
        }
        catch (Exception error) when (error is HttpRequestException or JsonException || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            return new(false, [], Unavailable);
        }
    }

    private sealed record BoardResponse([property: JsonPropertyName("open")] bool Open, CommunityRank[]? Entries);
}
