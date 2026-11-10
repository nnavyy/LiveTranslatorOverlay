using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using LiveTranslatorOverlay.Core;

namespace LiveTranslatorMac;

internal sealed class CommunityWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();

    public CommunityWindow(TimeSpan localUsage, bool showSupport)
    {
        Title = "Live Translator Community";
        Width = 720; Height = 630; MinWidth = 560; MinHeight = 440;
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
        var board = new ComboBox { ItemsSource = new[] { "All-time usage", "This month (UTC)", "Supporters" }, SelectedIndex = 0, MinWidth = 154 };
        var search = new TextBox { MaxLength = 24, MinWidth = 132, PlaceholderText = "Nickname" };
        var status = Line("");
        var message = Surface(status, tinted: false);
        var results = new ItemsControl();
        var scoreTitle = Heading("Audio time", 13);
        var header = Surface(RankRow(Heading("Rank", 13), Heading("Nickname", 13), scoreTitle, Heading("Medals", 13)), tinted: true);
        results.ItemTemplate = new FuncDataTemplate<CommunityRank>((row, _) => row == null ? null :
            new Border
            {
                Padding = new Thickness(14, 6),
                BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Outline,
                Child = RankRow(Line(row.Rank.ToString()), Line(row.Nickname),
                    Line(row.ScoreLabel((CommunityBoard)board.SelectedIndex)), Line(row.MedalLabel))
            });
        AutomationProperties.SetName(board, "Ranking category");
        AutomationProperties.SetName(search, "Search by nickname");
        AutomationProperties.SetName(results, "Ranking results");
        var refresh = new Button { Content = "Find" };
        var previous = new Button { Content = "Previous" };
        var next = new Button { Content = "Next" };
        var pages = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { previous, next } };
        var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20), Spacing = 10, Children =
        {
            Heading("Community rankings", 27), Line("Compare opt-in audio translation time or verified contributions."),
            Surface(new StackPanel { Spacing = 4, Children = { Heading(GuestAudioUsage.Format(usage), 21),
                Line("Your audio translation time on this device · Private") } }, tinted: true),
            Line("Board and nickname search"), new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { board, search, refresh } },
            header, new ScrollViewer { Content = results, MaxHeight = 218 }, message, pages
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
            scoreTitle.Text = category == CommunityBoard.Supporters ? "Contributed" : "Audio time";
            status.Text = "Loading rankings…";
            message.IsVisible = true;
            pages.IsVisible = false;
            results.ItemsSource = null;
            try
            {
                var data = await client.ReadAsync(category, search.Text ?? "", page, token);
                if (token.IsCancellationRequested) return;
                offset = page;
                status.Text = data.Message;
                message.IsVisible = data.Message.Length > 0;
                results.ItemsSource = data.Entries;
                pages.IsVisible = data.Available;
                previous.IsEnabled = data.Available && offset > 0;
                next.IsEnabled = data.Available && data.Entries.Length == CommunityClient.PageSize;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { if (!token.IsCancellationRequested) board.IsEnabled = search.IsEnabled = refresh.IsEnabled = true; busy = false; }
        }
        refresh.Click += async (_, _) => await Load(0);
        search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Load(0); };
        previous.Click += async (_, _) => await Load(Math.Max(0, offset - CommunityClient.PageSize));
        next.Click += async (_, _) => await Load(offset + CommunityClient.PageSize);
        board.SelectionChanged += async (_, _) => await Load(0);
        Opened += async (_, _) => await Load(0);
        return new ScrollViewer { Content = panel };
    }

    private Control Offers()
    {
        var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20), Spacing = 10, Children =
        {
            Heading("Support development", 27), Line(SupportCatalog.Intro)
        } };
        var plans = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        foreach (var offer in SupportCatalog.Offers)
        {
            var card = Surface(new StackPanel { Spacing = 5, Children =
            {
                Heading(offer.Title, 16), Heading(offer.Price, offer.Cents.HasValue ? 23 : 15), Line(offer.Description)
            } }, tinted: false);
            if (offer.Cents.HasValue)
            {
                Grid.SetColumn(card, plans.Children.Count);
                plans.Children.Add(card);
            }
            else
            {
                panel.Children.Add(plans);
                panel.Children.Add(card);
            }
        }
        var purchase = new StackPanel { Spacing = 5, Children =
        {
            Heading("Purchase options", 16), Line(SupportCatalog.Closed),
            Line("Secure checkout will open in your browser. The developer-assisted option will use a private request inside the app.")
        } };
        var client = new CommunityClient(AppSettings.DataDirectory);
        if (client.ManualRequestPreview)
        {
            purchase.Children.Add(new Expander
            {
                Header = "Developer-assisted request (staging)",
                Content = ManualRequestForm(client)
            });
        }
        panel.Children.Add(Surface(purchase, tinted: true));
        var terms = new Button { Content = "Terms and privacy" };
        terms.Click += async (_, _) => await MacApplication.CreateTermsWindow().ShowDialog(this);
        var close = new Button { Content = "Close" };
        close.Click += (_, _) => Close();
        panel.Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { terms, close } });
        return new ScrollViewer { Content = panel };
    }

    private Control ManualRequestForm(CommunityClient client)
    {
        var offer = new ComboBox
        {
            ItemsSource = SupportCatalog.Offers.Where(item => item.Cents.HasValue).ToArray(),
            ItemTemplate = new FuncDataTemplate<SupportOffer>((item, _) => Line(item?.Label ?? "")),
            SelectedIndex = 0
        };
        var email = new TextBox { MaxLength = 254 };
        var code = new TextBox { MaxLength = 6 };
        AutomationProperties.SetName(offer, "Lifetime offer");
        AutomationProperties.SetName(email, "Staging account email");
        AutomationProperties.SetName(code, "Six-digit email code");
        var send = new Button { Content = "Send code" };
        var submit = new Button { Content = "Submit private request" };
        var status = Line("");
        async Task Run(Func<CancellationToken, Task<string>> action)
        {
            var token = _lifetime.Token;
            offer.IsEnabled = email.IsEnabled = code.IsEnabled = send.IsEnabled = submit.IsEnabled = false;
            status.Text = "Working…";
            try { status.Text = await action(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally { if (!token.IsCancellationRequested) offer.IsEnabled = email.IsEnabled = code.IsEnabled = send.IsEnabled = submit.IsEnabled = true; }
        }
        send.Click += async (_, _) => await Run(token => client.SendPurchaseCodeAsync(email.Text?.Trim() ?? "", token));
        submit.Click += async (_, _) =>
        {
            if (offer.SelectedItem is not SupportOffer selected) return;
            string digits = code.Text?.Trim() ?? "";
            code.Text = "";
            await Run(token => client.RequestManualPurchaseAsync(email.Text?.Trim() ?? "", digits, selected.Id, token));
        };
        return new StackPanel { Spacing = 8, Children =
        {
            Line("Use a staging account. Your email goes to the sign-in service and your selected offer is saved privately."),
            offer, Line("Staging account email"), email, send, Line("Six-digit email code"), code, submit, status
        } };
    }

    private static readonly IBrush Outline = new SolidColorBrush(Color.FromArgb(85, 96, 119, 148));
    private static readonly IBrush Tint = new SolidColorBrush(Color.FromArgb(27, 80, 138, 196));

    private static Border Surface(Control child, bool tinted)
    {
        var surface = new Border { Child = child, Padding = new Thickness(14, 11) };
        surface.Classes.Add("workspace");
        if (tinted) surface.Background = Tint;
        return surface;
    }

    private static Grid RankRow(params Control[] cells)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,114,132"), ColumnSpacing = 6 };
        for (int index = 0; index < cells.Length; index++)
        {
            Grid.SetColumn(cells[index], index);
            row.Children.Add(cells[index]);
        }
        return row;
    }

    private static TextBlock Heading(string text, double size) => new() { Text = text, FontSize = size, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private static TextBlock Line(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
}
