using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using LiveTranslatorOverlay.Core;

namespace LiveTranslatorMac;

internal sealed class CommunityWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();

    public CommunityWindow(TimeSpan localUsage, bool showSupport)
    {
        Title = "Live Translator Community";
        Width = 620; Height = 560; MinWidth = 420; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Rankings", Content = Rankings(localUsage) },
                new TabItem { Header = "Support development", Content = Offers() }
            },
            SelectedIndex = showSupport ? 1 : 0
        };
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private Control Rankings(TimeSpan usage)
    {
        var client = new CommunityClient(AppSettings.DataDirectory);
        var board = new ComboBox { ItemsSource = new[] { "All-time audio usage", "This month (UTC)", "Supporters" }, SelectedIndex = 0 };
        var search = new TextBox { MaxLength = 24, PlaceholderText = "Search by nickname" };
        var status = Line("");
        var results = new ListBox { MaxHeight = 260, MinHeight = 72 };
        AutomationProperties.SetName(board, "Ranking category");
        AutomationProperties.SetName(search, "Search by nickname");
        AutomationProperties.SetName(results, "Ranking results");
        var refresh = new Button { Content = "Search / refresh" };
        var previous = new Button { Content = "Previous" };
        var next = new Button { Content = "Next" };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10, Children =
        {
            Line("Community rankings", 20), Line($"Your audio translation on this device: {GuestAudioUsage.Format(usage)}. This total is private."),
            board, search, new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { refresh, previous, next } }, status, results
        } };
        int offset = 0;
        bool busy = false;
        async Task Load(int page)
        {
            if (busy || board.SelectedIndex < 0) return;
            var token = _lifetime.Token;
            busy = true;
            board.IsEnabled = search.IsEnabled = refresh.IsEnabled = previous.IsEnabled = next.IsEnabled = false;
            var category = (CommunityBoard)board.SelectedIndex;
            status.Text = "Loading rankings…";
            results.ItemsSource = null;
            try
            {
                var data = await client.ReadAsync(category, search.Text ?? "", page, token);
                if (token.IsCancellationRequested) return;
                offset = page;
                status.Text = data.Message;
                results.ItemsSource = data.Entries.Select(row => row.Format(category)).ToArray();
                previous.IsEnabled = data.Available && offset > 0;
                next.IsEnabled = data.Available && data.Entries.Length == CommunityClient.PageSize;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { if (!token.IsCancellationRequested) board.IsEnabled = search.IsEnabled = refresh.IsEnabled = true; busy = false; }
        }
        refresh.Click += async (_, _) => await Load(0);
        previous.Click += async (_, _) => await Load(Math.Max(0, offset - CommunityClient.PageSize));
        next.Click += async (_, _) => await Load(offset + CommunityClient.PageSize);
        board.SelectionChanged += async (_, _) => await Load(0);
        Opened += async (_, _) => await Load(0);
        return new ScrollViewer { Content = panel };
    }

    private Control Offers()
    {
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10, Children =
        {
            Line("Support development", 20), Line(SupportCatalog.Intro), Line(SupportCatalog.Closed)
        } };
        foreach (var offer in SupportCatalog.Offers)
        {
            var heading = Line(offer.Label, 15);
            heading.FontWeight = FontWeight.SemiBold;
            panel.Children.Add(heading);
            panel.Children.Add(Line(offer.Description));
        }
        var terms = new Button { Content = "Terms and privacy" };
        terms.Click += async (_, _) => await MacApplication.CreateTermsWindow().ShowDialog(this);
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();
        panel.Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { terms, close } });
        return new ScrollViewer { Content = panel };
    }

    private static TextBlock Line(string text, double size = 13) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
}
