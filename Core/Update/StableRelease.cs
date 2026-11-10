using System.Security.Cryptography;
using System.Text.Json;

namespace LiveTranslatorOverlay.Core.Update;

public sealed record StableRelease(Version Version, Uri DownloadUri, long Size, string Sha256)
{
    private const string Downloads = "https://github.com/nnavyy/LiveTranslatorOverlay/releases/download/";
    public const string ManifestUrl = "https://raw.githubusercontent.com/nnavyy/LiveTranslatorOverlay/main/updates/ml-stable.json";

    public static StableRelease? Read(string json, string runtime)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("line").GetString() != "ml" || root.GetProperty("channel").GetString() != "stable")
            return null;

        string? text = root.GetProperty("version").GetString();
        if (!Version.TryParse(text, out var version) || version.Build < 0 || version.Revision >= 0 || text != version.ToString(3))
            throw new InvalidDataException("Expected a stable three-part release version.");

        var fileName = runtime switch
        {
            "win-x64" => "LiveTranslatorOverlay-Windows-x64.zip",
            "osx-arm64" => "LiveTranslatorOverlay-Mac-osx-arm64.zip",
            "osx-x64" => "LiveTranslatorOverlay-Mac-osx-x64.zip",
            _ => throw new ArgumentOutOfRangeException(nameof(runtime))
        };
        if (!root.GetProperty("packages").TryGetProperty(runtime, out var package)) return null;
        var url = new Uri($"{Downloads}v{version}-ml/{fileName}");
        long size = package.GetProperty("size").GetInt64();
        string? hash = package.GetProperty("sha256").GetString();
        if (size <= 0 || hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit) ||
            package.GetProperty("url").GetString() != url.AbsoluteUri)
            throw new InvalidDataException("The stable package metadata is invalid.");
        return new StableRelease(version, url, size, hash);
    }

    public async Task VerifyAsync(string filePath, CancellationToken token)
    {
        await using var file = File.OpenRead(filePath);
        if (file.Length != Size || !Convert.ToHexString(await SHA256.HashDataAsync(file, token))
                .Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update download does not match the published size and checksum.");
    }
}
