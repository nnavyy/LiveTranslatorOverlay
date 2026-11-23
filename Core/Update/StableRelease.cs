using System.Security.Cryptography;
using System.Text.Json;

namespace LiveTranslatorOverlay.Core.Update;

public sealed record StableRelease(Version Version, Uri DownloadUri, long Size, string Sha256, string Channel = "stable")
{
    private const string Downloads = "https://github.com/nnavyy/LiveTranslatorOverlay/releases/download/";
    public const string ManifestUrl = "https://raw.githubusercontent.com/nnavyy/LiveTranslatorOverlay/main/updates/ml-stable.json";

    public static string GetManifestUrl(string channel) => channel?.Trim().ToLowerInvariant() switch
    {
        "beta" => "",
        _ => ManifestUrl
    };

    public static StableRelease? Read(string json, string runtime) =>
        Read(json, runtime, "stable");

    public static StableRelease? Read(string json, string runtime, string expectedChannel)
    {
        string normalizedChannel = string.IsNullOrWhiteSpace(expectedChannel)
            ? "stable"
            : expectedChannel.Trim().ToLowerInvariant();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("line").GetString() != "ml" || root.GetProperty("channel").GetString() != normalizedChannel)
            return null;

        string? text = root.GetProperty("version").GetString();
        if (!Version.TryParse(text, out var version) || version.Build < 0 || version.Revision >= 0 || text != version.ToString(3))
            throw new InvalidDataException("Expected a three-part release version.");

        var fileName = runtime switch
        {
            "win-x64" => "LiveTranslatorOverlay-Windows-x64.zip",
            "osx-arm64" => "LiveTranslatorOverlay-Mac-osx-arm64.zip",
            "osx-x64" => "LiveTranslatorOverlay-Mac-osx-x64.zip",
            _ => throw new ArgumentOutOfRangeException(nameof(runtime))
        };
        if (!root.GetProperty("packages").TryGetProperty(runtime, out var package)) return null;

        long size = package.GetProperty("size").GetInt64();
        string? hash = package.GetProperty("sha256").GetString();
        string? rawUrl = package.GetProperty("url").GetString();
        if (size <= 0 || hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(rawUrl))
            throw new InvalidDataException($"The {normalizedChannel} package metadata is invalid.");

        Uri resolvedUri;
        if (normalizedChannel == "dev")
        {
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) ||
                !(parsed.IsFile || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback) || parsed.Scheme == Uri.UriSchemeHttps) ||
                !parsed.AbsolutePath.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The dev package URL must point to a matching local or HTTPS archive.");
            }
            resolvedUri = parsed;
        }
        else if (normalizedChannel == "beta")
        {
            if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) ||
                !parsed.AbsolutePath.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase) ||
                !(parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback ||
                  parsed.Scheme == Uri.UriSchemeHttps &&
                  (parsed.AbsolutePath.Contains("/storage/v1/object/sign/", StringComparison.OrdinalIgnoreCase) ||
                   parsed.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase) ||
                   parsed.Host.Equals("livetranslator.app", StringComparison.OrdinalIgnoreCase))))
            {
                throw new InvalidDataException("The beta package metadata must use a server-authorized URL.");
            }
            resolvedUri = parsed;
        }
        else
        {
            var expectedUrl = $"{Downloads}v{version}-ml/{fileName}";
            if (rawUrl != expectedUrl)
                throw new InvalidDataException("The stable package metadata is invalid.");
            resolvedUri = new Uri(expectedUrl);
        }

        return new StableRelease(version, resolvedUri, size, hash, normalizedChannel);
    }

    public async Task VerifyAsync(string filePath, CancellationToken token)
    {
        await using var file = File.OpenRead(filePath);
        if (file.Length != Size || !Convert.ToHexString(await SHA256.HashDataAsync(file, token))
                .Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update download does not match the published size and checksum.");
    }
}
