using System.Windows;
using System.Windows.Controls;

namespace LiveTranslatorOverlay.Core;

public sealed class CommunityWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();

    public CommunityWindow(Window owner, TimeSpan localUsage, bool showSupport)
    {
        Owner = owner;
        Title = "Live Translator Community";
        Width = 620; Height = 560; MinWidth = 420; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Rankings", Content = Rankings(localUsage) });
        tabs.Items.Add(new TabItem { Header = "Support development", Content = Offers() });
        tabs.SelectedIndex = showSupport ? 1 : 0;
        Content = tabs;
        SourceInitialized += (_, _) => CaptureExclusion.ExcludeFromCapture(this);
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private UIElement Rankings(TimeSpan usage)
    {
        var client = new CommunityClient(AppSettings.DataDirectory);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Line("Community rankings", 20));
        panel.Children.Add(Line($"Your audio translation on this device: {GuestAudioUsage.Format(usage)}. This total is private."));
        var board = new ComboBox { ItemsSource = new[] { "All-time audio usage", "This month (UTC)", "Supporters" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(board, "Ranking category");
        panel.Children.Add(board);
        var search = new TextBox { MaxLength = 24, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Search by nickname" };
        System.Windows.Automation.AutomationProperties.SetName(search, "Search by nickname");
        panel.Children.Add(search);
        var buttons = new WrapPanel();
        var refresh = Button("Search / refresh");
        var previous = Button("Previous");
        var next = Button("Next");
        buttons.Children.Add(refresh); buttons.Children.Add(previous); buttons.Children.Add(next);
        panel.Children.Add(buttons);
        var status = Line("");
        panel.Children.Add(status);
        var results = new ListBox { MaxHeight = 260, MinHeight = 72 };
        System.Windows.Automation.AutomationProperties.SetName(results, "Ranking results");
        panel.Children.Add(results);
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
                var data = await client.ReadAsync(category, search.Text, page, token);
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
        Loaded += async (_, _) => await Load(0);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement Offers()
    {
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(Line("Support development", 20));
        panel.Children.Add(Line(SupportCatalog.Intro));
        panel.Children.Add(Line(SupportCatalog.Closed));
        foreach (var offer in SupportCatalog.Offers)
        {
            var heading = Line(offer.Label, 15);
            heading.FontWeight = FontWeights.SemiBold;
            panel.Children.Add(heading);
            panel.Children.Add(Line(offer.Description));
        }
        var actions = new WrapPanel();
        var terms = Button("Terms and privacy");
        terms.Click += (_, _) => App.ShowTerms(this);
        var close = Button("Close");
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        actions.Children.Add(terms); actions.Children.Add(close);
        panel.Children.Add(actions);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static TextBlock Line(string text, double size = 13) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
    };

    private static Button Button(string text) => new() { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 8) };
}
