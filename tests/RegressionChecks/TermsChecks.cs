using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using LiveTranslatorOverlay;
using LiveTranslatorOverlay.Core;

// The bundled Terms of Service and Privacy Policy must display as clean text: no Markdown
// markup (**, __, backticks, raw # headings) in the shared blocks or in the Windows reader.
// Returns the number of failed checks (0 = all good).
static class TermsChecks
{
    public static int Run()
    {
        int failures = 0;
        void Check(string name, bool ok, string detail)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}: {detail}");
            if (!ok) failures++;
        }
        static bool Dirty(string text) =>
            text.Contains("**") || text.Contains("__") || text.Contains('`') || text.Contains('*') ||
            text.Split('\n').Any(line => line.TrimStart().StartsWith('#'));

        var sample = TermsNotice.Blocks("# Title\n\nSome **bold**, __under__, _italic_ with `code`, `WDA_EXCLUDEFROMCAPTURE` and [site](https://example.com).\nSecond line.\n\n## 1. Part\n- First *item*\n  continued\n- Second\n\n---\nTail");
        string joined = string.Join("|", sample.Select(b => $"{b.Kind}:{b.Text}"));
        Check("Blocks strip inline markup and join lines", joined ==
            "Title:Title|Paragraph:Some bold, under, italic with code, WDA_EXCLUDEFROMCAPTURE and site (https://example.com). Second line.|Heading:1. Part|Bullet:First item continued|Bullet:Second|Paragraph:Tail", joined);

        foreach (var (label, markdown) in new[] { ("Terms of Service", TermsNotice.LicenseText), ("Privacy Policy", TermsNotice.PrivacyText) })
        {
            Check($"{label} source uses only plain Markdown", !markdown.Contains("**") && !markdown.Contains("__") && !markdown.Contains('`')
                && !markdown.Contains('<') && !markdown.Contains('|') && !markdown.Split('\n').Any(l => l.TrimStart().StartsWith("* ")), "no bold, code, HTML or tables");
            var blocks = TermsNotice.Blocks(markdown);
            Check($"{label} blocks are clean", blocks.Count > 10 && blocks.All(b => b.Text.Length > 0 && !Dirty(b.Text)),
                $"{blocks.Count} blocks, {blocks.Count(b => b.Kind == TermsBlockKind.Heading)} headings, {blocks.Count(b => b.Kind == TermsBlockKind.Bullet)} bullets");
            Check($"{label} has one title and structure", blocks.Count(b => b.Kind == TermsBlockKind.Title) == 1 &&
                blocks[0].Kind == TermsBlockKind.Title && blocks.Any(b => b.Kind == TermsBlockKind.Heading) && blocks.Any(b => b.Kind == TermsBlockKind.Bullet),
                blocks[0].Text);
            Check($"{label} matches the bundled revision", markdown.Contains(TermsNotice.Revision, StringComparison.Ordinal), TermsNotice.Revision);
            Check($"{label} is consistent with open source", !markdown.Contains("maintained privately", StringComparison.OrdinalIgnoreCase)
                && !markdown.Contains("All rights reserved", StringComparison.OrdinalIgnoreCase)
                && !markdown.Contains("proprietary", StringComparison.OrdinalIgnoreCase)
                && !markdown.Contains("source is private", StringComparison.OrdinalIgnoreCase)
                && !markdown.Contains("Anthropic", StringComparison.OrdinalIgnoreCase)
                && !markdown.Contains("US$13", StringComparison.OrdinalIgnoreCase)
                && markdown.Contains("MIT", StringComparison.Ordinal), "MIT, no private/proprietary/Anthropic wording");
        }
        Check("Terms revision was bumped", TermsNotice.Revision == "ml-2", TermsNotice.Revision);
        string receiptDir = Path.Combine(Path.GetTempPath(), "LiveTranslatorTermsRev-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(receiptDir);
            File.WriteAllLines(Path.Combine(receiptDir, "terms-accepted.txt"), ["ml-1", DateTimeOffset.UtcNow.ToString("O")]);
            Check("Previous revision ml-1 requires re-acceptance", !TermsNotice.HasAccepted(receiptDir), "ml-1 rejected for ml-2");
        }
        finally { if (Directory.Exists(receiptDir)) Directory.Delete(receiptDir, true); }

        string? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var (label, markdown) in new[] { ("Terms of Service", TermsNotice.LicenseText), ("Privacy Policy", TermsNotice.PrivacyText) })
                {
                    var reader = App.CreateTermsReader(markdown, label);
                    string shown = new TextRange(reader.Document.ContentStart, reader.Document.ContentEnd).Text;
                    bool lists = reader.Document.Blocks.OfType<List>().Any(l => l.ListItems.Count > 0 && l.MarkerStyle == TextMarkerStyle.Disc);
                    bool headings = reader.Document.Blocks.OfType<Paragraph>().Any(p => p.FontWeight == FontWeights.SemiBold && p.FontSize >= 16);
                    if (Dirty(shown) || !lists || !headings || !reader.IsReadOnly) error = $"{label}: dirty={Dirty(shown)} lists={lists} headings={headings}";
                }
                var view = App.CreateTermsView(1);
                view.Measure(new Size(420, 320));
                view.Arrange(new Rect(0, 0, 420, 320));
                if (view.ActualWidth != 420) error ??= "terms view did not lay out";
            }
            catch (Exception ex) { error = ex.ToString(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Check("Windows reader shows styled text without markup", error == null, error ?? "headings, bullet lists, no ** __ ` #");
        return failures;
    }
}