using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveTranslatorOverlay;
using LiveTranslatorOverlay.Core;

// History window must not move the reader's viewport when entries are appended.
// Returns the number of failed checks (0 = all good). Runs a real HistoryWindow on an STA thread.
static class HistoryChecks
{
    public static int Run()
    {
        int failures = 0;
        void Check(string name, bool ok, string detail)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}: {detail}");
            if (!ok) failures++;
        }

        // Pure policy.
        Check("Policy follows at bottom", HistoryScrollPolicy.ShouldFollowTail(600, 400, 1000), "offset+viewport == extent");
        Check("Policy follows within tolerance", HistoryScrollPolicy.ShouldFollowTail(597, 400, 1000), "3px above bottom");
        Check("Policy stops when scrolled up", !HistoryScrollPolicy.ShouldFollowTail(300, 400, 1000), "300px above bottom");
        Check("Policy follows when nothing scrolls", HistoryScrollPolicy.ShouldFollowTail(0, 400, 380), "extent < viewport");

        // Buffer: entries are returned for incremental append and versions are monotonic across Clear.
        var buffer = new TranslationHistory();
        string? e1 = buffer.Add("a", "b", out long v1);
        buffer.Clear();
        string? e2 = buffer.Add("c", "d", out long v2);
        string? none = buffer.Add("c", "  ", out long v3);
        Check("History versions are monotonic", e1 != null && e2 != null && v2 == v1 + 1 && none == null && v3 == v2,
            $"v1={v1} v2={v2} blank-added={none != null}");

        // Real window.
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { RunWindowChecks(Check); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) Check("HistoryWindow harness", false, error.ToString());
        return failures;
    }

    static void Flush() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    static void RunWindowChecks(Action<string, bool, string> C)
    {

        var history = new TranslationHistory();
        for (int i = 0; i < 200; i++) history.Add($"source line {i}", $"translated line {i}");
        var window = new HistoryWindow(history.ToString(), history.Version) { Width = 400, Height = 500, ShowActivated = false };
        var box = (TextBox)window.FindName("TxtHistory");
        var jump = (Button)window.FindName("BtnJumpLatest");
        window.Show();
        Flush(); Flush();

        bool atBottom() => box.VerticalOffset + box.ViewportHeight >= box.ExtentHeight - 4;
        C("Opens at newest entry", box.ExtentHeight > box.ViewportHeight * 3 && atBottom(),
            $"offset={box.VerticalOffset:F0} viewport={box.ViewportHeight:F0} extent={box.ExtentHeight:F0}");

        // Follow tail while at bottom.
        int Append(int count)
        {
            for (int i = 0; i < count; i++)
            {
                string? entry = history.Add($"new source {history.Version}", $"new translation {history.Version}", out long v);
                window.AppendEntry(entry!, v);
            }
            Flush(); Flush();
            return count;
        }
        Append(3);
        C("Follows newest entry when already at bottom", atBottom(), $"offset={box.VerticalOffset:F0} extent={box.ExtentHeight:F0}");

        // Reader scrolls up.
        box.ScrollToVerticalOffset(box.ExtentHeight / 3);
        Flush();
        double readerOffset = box.VerticalOffset;
        box.Select(40, 30);
        string selected = box.SelectedText;
        int lengthBefore = box.Text.Length;
        Append(5);
        C("Scroll position survives appends while reading", Math.Abs(box.VerticalOffset - readerOffset) < 0.5,
            $"before={readerOffset:F1} after={box.VerticalOffset:F1}");
        C("Entries are appended, not replaced", box.Text.Length > lengthBefore && box.Text.EndsWith(history.ToString()[^40..]),
            $"length {lengthBefore} -> {box.Text.Length}");
        C("Selection survives appends", box.SelectedText == selected && selected.Length == 30, $"'{selected.Replace("\n", "|")}'");
        C("Jump-to-latest shown while scrolled up", jump.Visibility == Visibility.Visible, jump.Visibility.ToString());

        // Duplicate (already in snapshot) entries are ignored.
        int len = box.Text.Length;
        window.AppendEntry("duplicate\n", 1);
        Flush();
        C("Stale/duplicate entry ignored", box.Text.Length == len, $"length {len} -> {box.Text.Length}");

        // Jump to latest resumes following.
        jump.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Flush();
        Append(2);
        C("Jump-to-latest resumes following", atBottom() && jump.Visibility == Visibility.Collapsed,
            $"offset={box.VerticalOffset:F0} extent={box.ExtentHeight:F0}");

        // Old behaviour for comparison: full replace + ScrollToEnd moves a reader who scrolled up.
        box.ScrollToVerticalOffset(box.ExtentHeight / 3);
        Flush();
        double reading = box.VerticalOffset;
        string full = history.ToString();
        var legacy = new TextBox { Width = 380, Height = 440, TextWrapping = TextWrapping.Wrap, FontFamily = box.FontFamily, FontSize = box.FontSize, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsReadOnly = true, AcceptsReturn = true };
        var host = new Window { Content = legacy, ShowActivated = false, Width = 400, Height = 500 };
        host.Show(); legacy.Text = full; Flush(); Flush();
        legacy.ScrollToVerticalOffset(legacy.ExtentHeight / 3); Flush();
        double legacyReading = legacy.VerticalOffset;
        legacy.Text = full + "x\n"; legacy.ScrollToEnd(); Flush();
        Console.WriteLine($"INFO: legacy full-replace moved reader {legacyReading:F0} -> {legacy.VerticalOffset:F0} (old SetHistory behaviour)");
        host.Close();
        window.Close();
    }
}
