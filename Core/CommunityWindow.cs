using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace LiveTranslatorOverlay.Core;

public sealed class CommunityWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();

    public CommunityWindow(Window owner, TimeSpan localUsage, bool showSupport)
    {
        Owner = owner;
        Title = "Live Translator Community";
        Width = 720; Height = 630; MinWidth = 560; MinHeight = 440;
        FontSize = 14;
        Background = SystemColors.WindowBrush;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var tabs = new TabControl { Background = SystemColors.WindowBrush };
        tabs.Items.Add(new TabItem { Header = "Rankings", Padding = new Thickness(14, 7, 14, 7), Content = Rankings(localUsage) });
        tabs.Items.Add(new TabItem { Header = "Support development", Padding = new Thickness(14, 7, 14, 7), Content = Offers() });
        tabs.SelectedIndex = showSupport ? 1 : 0;
        Content = tabs;
        SourceInitialized += (_, _) => CaptureExclusion.ExcludeFromCapture(this);
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private UIElement Rankings(TimeSpan usage)
    {
        var client = new CommunityClient(AppSettings.DataDirectory);
        var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };
        panel.Children.Add(Heading("Community rankings", 27));
        panel.Children.Add(Line("Compare opt-in audio translation time or verified contributions."));
        panel.Children.Add(Surface(new StackPanel { Children =
        {
            Heading(GuestAudioUsage.Format(usage), 21),
            Line("Your audio translation time on this device · Private", bottom: 0)
        } }, tinted: true));
        var board = new ComboBox { ItemsSource = new[] { "All-time usage", "This month (UTC)", "Supporters" }, SelectedIndex = 0, MinWidth = 154, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 0, 8, 8) };
        System.Windows.Automation.AutomationProperties.SetName(board, "Ranking category");
        var search = new TextBox { MaxLength = 24, MinWidth = 132, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 8, 8) };
        System.Windows.Automation.AutomationProperties.SetName(search, "Search by nickname");
        var refresh = Button("Find");
        var filters = new WrapPanel { Children = { board, search, refresh } };
        panel.Children.Add(Line("Board and nickname search", bottom: 4));
        panel.Children.Add(filters);
        var previous = Button("Previous");
        var next = Button("Next");
        var status = Line("", bottom: 0);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        var scoreColumn = Column("Audio time", "Score", 114);
        var results = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserSortColumns = false, CanUserResizeColumns = false, CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = SystemColors.ControlLightBrush, Background = SystemColors.WindowBrush,
            MaxHeight = 250, RowHeight = 32, Margin = new Thickness(0, 4, 0, 10)
        };
        results.Columns.Add(Column("Rank", "Rank", 48));
        results.Columns.Add(new DataGridTextColumn { Header = "Nickname", Binding = new Binding("Nickname"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        results.Columns.Add(scoreColumn);
        results.Columns.Add(Column("Medals", "Medals", 132));
        System.Windows.Automation.AutomationProperties.SetName(results, "Ranking results");
        panel.Children.Add(results);
        var message = Surface(status, tinted: false);
        panel.Children.Add(message);
        var pages = new WrapPanel { Children = { previous, next } };
        panel.Children.Add(pages);
        int offset = 0;
        bool busy = false;
        async Task Load(int page)
        {
            if (busy || board.SelectedIndex < 0) return;
            var token = _lifetime.Token;
            busy = true;
            board.IsEnabled = search.IsEnabled = refresh.IsEnabled = previous.IsEnabled = next.IsEnabled = false;
            var category = (CommunityBoard)board.SelectedIndex;
            scoreColumn.Header = category == CommunityBoard.Supporters ? "Contributed" : "Audio time";
            status.Text = "Loading rankings…";
            message.Visibility = Visibility.Visible;
            pages.Visibility = Visibility.Collapsed;
            results.ItemsSource = null;
            try
            {
                var data = await client.ReadAsync(category, search.Text, page, token);
                if (token.IsCancellationRequested) return;
                offset = page;
                status.Text = data.Message;
                message.Visibility = data.Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                results.ItemsSource = data.Entries.Select(row => new { row.Rank, row.Nickname, Score = row.ScoreLabel(category), Medals = row.MedalLabel }).ToArray();
                pages.Visibility = data.Available ? Visibility.Visible : Visibility.Collapsed;
                previous.IsEnabled = data.Available && offset > 0;
                next.IsEnabled = data.Available && data.Entries.Length == CommunityClient.PageSize;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { if (!token.IsCancellationRequested) board.IsEnabled = search.IsEnabled = refresh.IsEnabled = true; busy = false; }
        }
        refresh.Click += async (_, _) => await Load(0);
        search.KeyDown += async (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) await Load(0); };
        previous.Click += async (_, _) => await Load(Math.Max(0, offset - CommunityClient.PageSize));
        next.Click += async (_, _) => await Load(offset + CommunityClient.PageSize);
        board.SelectionChanged += async (_, _) => await Load(0);
        Loaded += async (_, _) => await Load(0);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement Offers()
    {
        var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20) };
        panel.Children.Add(Heading("Support development", 27));
        panel.Children.Add(Line(SupportCatalog.Intro));
        var plans = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        foreach (var offer in SupportCatalog.Offers)
        {
            var details = new StackPanel { Children =
            {
                Heading(offer.Title, 16), Heading(offer.Price, offer.Cents.HasValue ? 23 : 15),
                Line(offer.Description, bottom: 0)
            } };
            var card = Surface(details, tinted: false);
            if (offer.Cents.HasValue)
            {
                if (plans.Children.Count == 0) card.Margin = new Thickness(0, 0, 10, 10);
                plans.Children.Add(card);
            }
            else
            {
                panel.Children.Add(plans);
                panel.Children.Add(card);
            }
        }
        var purchase = new StackPanel { Children =
        {
            Heading("Purchase options", 16), Line(SupportCatalog.Closed),
            Line("Secure checkout will open in your browser. The developer-assisted option will use a private request inside the app.", bottom: 0)
        } };
        var client = new CommunityClient(AppSettings.DataDirectory);
        if (client.ManualRequestPreview)
        {
            purchase.Children.Add(new Expander
            {
                Header = "Developer-assisted request (staging)",
                Margin = new Thickness(0, 12, 0, 0),
                Content = ManualRequestForm(client)
            });
        }
        panel.Children.Add(Surface(purchase, tinted: true));
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

    private UIElement ManualRequestForm(CommunityClient client)
    {
        var lifetimeOffers = SupportCatalog.Offers.Where(offer => offer.Cents.HasValue).ToArray();
        var offer = new ComboBox { ItemsSource = lifetimeOffers, DisplayMemberPath = nameof(SupportOffer.Label), SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        var email = new TextBox { MaxLength = 254, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 8) };
        var code = new TextBox { MaxLength = 6, Padding = email.Padding, Margin = email.Margin };
        System.Windows.Automation.AutomationProperties.SetName(offer, "Lifetime offer");
        System.Windows.Automation.AutomationProperties.SetName(email, "Staging account email");
        System.Windows.Automation.AutomationProperties.SetName(code, "Six-digit email code");
        var send = Button("Send code");
        var submit = Button("Submit private request");
        var status = Line("", bottom: 0);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        async Task Run(Func<CancellationToken, Task<string>> action)
        {
            var token = _lifetime.Token;
            offer.IsEnabled = email.IsEnabled = code.IsEnabled = send.IsEnabled = submit.IsEnabled = false;
            status.Text = "Working…";
            try { status.Text = await action(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { if (!token.IsCancellationRequested) offer.IsEnabled = email.IsEnabled = code.IsEnabled = send.IsEnabled = submit.IsEnabled = true; }
        }
        send.Click += async (_, _) => await Run(token => client.SendPurchaseCodeAsync(email.Text.Trim(), token));
        submit.Click += async (_, _) =>
        {
            if (offer.SelectedItem is not SupportOffer selected) return;
            string digits = code.Text.Trim();
            code.Clear();
            await Run(token => client.RequestManualPurchaseAsync(email.Text.Trim(), digits, selected.Id, token));
        };
        return new StackPanel { Margin = new Thickness(8, 10, 8, 0), Children =
        {
            Line("Use a staging account. Your email goes to the sign-in service and your selected offer is saved privately."),
            offer, Line("Staging account email", bottom: 4), email, new WrapPanel { Children = { send } },
            Line("Six-digit email code", bottom: 4), code, new WrapPanel { Children = { submit } }, status
        } };
    }

    private static DataGridTextColumn Column(string title, string path, double width) =>
        new() { Header = title, Binding = new Binding(path), Width = width };

    private static Border Surface(UIElement child, bool tinted) => new()
    {
        Child = child, Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 10),
        CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
        Background = tinted && !SystemParameters.HighContrast ? new SolidColorBrush(Color.FromRgb(237, 245, 252)) : SystemColors.WindowBrush,
        BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(207, 220, 232))
    };

    private static TextBlock Heading(string text, double size)
    {
        var heading = Line(text, size, 5);
        heading.FontWeight = FontWeights.SemiBold;
        return heading;
    }

    private static TextBlock Line(string text, double size = 14, double bottom = 10) => new()
    {
        Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom)
    };

    private static Button Button(string text) => new() { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 8) };
}
