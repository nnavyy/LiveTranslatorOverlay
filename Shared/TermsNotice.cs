using System.Globalization;

namespace LiveTranslatorOverlay.Core;

public static class TermsNotice
{
    public static readonly string Revision = ReadResource("TERMS_REVISION.txt").Trim();
    public static string LicenseText => ReadResource("APP_LICENSE.md");
    public static string PrivacyText => ReadResource("PRIVACY.md");
    public static string Text => LicenseText.TrimEnd() + "\n\n" + PrivacyText;

    public static IEnumerable<(string Heading, string Body)> Sections(string markdown)
    {
        string heading = "";
        var body = new List<string>();
        foreach (string line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith('#'))
            {
                if (heading.Length > 0 || body.Count > 0) yield return (heading, string.Join('\n', body).Trim());
                heading = line.TrimStart('#', ' ');
                body.Clear();
            }
            else if (line.Trim() != "---")
            {
                body.Add(line.TrimEnd().Replace("**", "", StringComparison.Ordinal));
            }
        }
        if (heading.Length > 0 || body.Count > 0) yield return (heading, string.Join('\n', body).Trim());
    }

    public static bool HasAccepted(string dataDirectory)
    {
        try
        {
            var lines = File.ReadAllLines(ReceiptPath(dataDirectory));
            return lines is [var revision, var timestamp] && revision == Revision &&
                DateTimeOffset.TryParseExact(timestamp, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static void Accept(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        string temporary = Path.Combine(dataDirectory, $"terms-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllLines(temporary, [Revision, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)]);
            File.Move(temporary, ReceiptPath(dataDirectory), overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string ReceiptPath(string dataDirectory) => Path.Combine(dataDirectory, "terms-accepted.txt");

    private static string ReadResource(string name)
    {
        using var stream = typeof(TermsNotice).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The application is missing {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
