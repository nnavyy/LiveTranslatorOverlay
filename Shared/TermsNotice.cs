using System.Globalization;
using System.Text.RegularExpressions;

namespace LiveTranslatorOverlay.Core;

public enum TermsBlockKind { Title, Heading, Paragraph, Bullet }

public sealed record TermsBlock(TermsBlockKind Kind, string Text);

public static partial class TermsNotice
{
    public static readonly string Revision = ReadResource("TERMS_REVISION.txt").Trim();
    public static string LicenseText => ReadResource("APP_LICENSE.md");
    public static string PrivacyText => ReadResource("PRIVACY.md");
    public static string Text => LicenseText.TrimEnd() + "\n\n" + PrivacyText;

    /// <summary>
    /// Converts the Markdown subset used by the bundled documents (# and ## headings, paragraphs,
    /// "- " bullets) into display blocks. Every window, on every platform, renders these blocks
    /// so that no raw Markdown markup is ever shown.
    /// </summary>
    public static IReadOnlyList<TermsBlock> Blocks(string markdown)
    {
        var blocks = new List<TermsBlock>();
        var paragraph = new List<string>();
        void Flush(TermsBlockKind kind)
        {
            if (paragraph.Count == 0) return;
            string text = Inline(string.Join(' ', paragraph));
            paragraph.Clear();
            if (text.Length > 0) blocks.Add(new TermsBlock(kind, text));
        }
        var open = TermsBlockKind.Paragraph;
        foreach (string raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.All(c => c is '-' or '*' or '_') && line.Length >= 3)
            {
                Flush(open);
                open = TermsBlockKind.Paragraph;
            }
            else if (line[0] == '#')
            {
                Flush(open);
                open = TermsBlockKind.Paragraph;
                int level = line.TakeWhile(c => c == '#').Count();
                string text = Inline(line[level..].Trim().TrimEnd('#').Trim());
                if (text.Length > 0) blocks.Add(new TermsBlock(level == 1 ? TermsBlockKind.Title : TermsBlockKind.Heading, text));
            }
            else if (line.Length > 1 && line[0] is '-' or '*' or '+' && line[1] == ' ')
            {
                Flush(open);
                open = TermsBlockKind.Bullet;
                paragraph.Add(line[2..].Trim());
            }
            else
            {
                if (open == TermsBlockKind.Bullet && raw.Length > 0 && !char.IsWhiteSpace(raw[0])) { Flush(open); open = TermsBlockKind.Paragraph; }
                paragraph.Add(line);
            }
        }
        Flush(open);
        return blocks;
    }

    /// <summary>Removes inline Markdown markup: bold, italics, code spans, and turns [text](url) into "text (url)".</summary>
    public static string Inline(string text)
    {
        text = Link().Replace(text, m => m.Groups[1].Value == m.Groups[2].Value ? m.Groups[2].Value : $"{m.Groups[1].Value} ({m.Groups[2].Value})");
        text = text.Replace("**", "", StringComparison.Ordinal).Replace("__", "", StringComparison.Ordinal)
            .Replace("*", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal);
        return UnderscoreEmphasis().Replace(text, "$1$2").Trim();
    }

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(^|[\s(\[""'])_([^_\s](?:[^_]*[^_\s])?)_(?=$|[\s.,;:!?)\]""'])")]
    private static partial Regex UnderscoreEmphasis();

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
