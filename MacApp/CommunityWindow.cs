using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using LiveTranslatorOverlay.Core;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace LiveTranslatorMac;

internal sealed class CommunityWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();

    public CommunityWindow(TimeSpan localUsage, bool showSupport)
    {
        Title = "Live Translator Community";
        Width = 720; Height = 680; MinWidth = 560; MinHeight = 440;
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
        var podium = new ContentControl { IsVisible = false };
        var standings = new StackPanel
        {
            Spacing = 6, Children = { Heading("Full rankings", 16), header, new ScrollViewer { Content = results, MaxHeight = 218 } }
        };
        var pages = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { previous, next } };
        var badge = PodiumAvatar(PodiumBrush(CommunityPodium.StageFill), PodiumBrush(CommunityPodium.StageInk));
        DockPanel.SetDock(badge, Dock.Right);
        var intro = new DockPanel { Children =
        {
            badge, new StackPanel { Spacing = 4, Children =
            {
                Heading("Community rankings", 27), Line($"Your time on this device · {GuestAudioUsage.Format(usage)} · Private")
            } }
        } };
        var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20), Spacing = 10, Children =
        {
            intro,
            Line("Board and nickname search"), new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { board, search, refresh } },
            podium, standings, message, pages
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
            string query = search.Text ?? "";
            scoreTitle.Text = category == CommunityBoard.Supporters ? "Contributed" : "Audio time";
            status.Text = "Loading rankings…";
            message.IsVisible = true;
            podium.Content = null;
            podium.IsVisible = false;
            standings.IsVisible = false;
            pages.IsVisible = false;
            results.ItemsSource = null;
            try
            {
                var data = await client.ReadAsync(category, query, page, token);
                if (token.IsCancellationRequested) return;
                offset = page;
                status.Text = data.Message;
                message.IsVisible = data.Message.Length > 0;
                var leaders = data.PodiumEntries(query, page);
                if (leaders.Count > 0) podium.Content = Podium(leaders, category);
                podium.IsVisible = leaders.Count > 0;
                results.ItemsSource = data.Entries;
                standings.IsVisible = data.Entries.Length > 0;
                previous.IsEnabled = data.Available && offset > 0;
                next.IsEnabled = data.Available && data.Entries.Length == CommunityClient.PageSize;
                pages.IsVisible = previous.IsEnabled || next.IsEnabled;
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

    private Border Podium(IReadOnlyList<CommunityRank> leaders, CommunityBoard category)
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        var row = new Grid { MaxWidth = leaders.Count * 184, ColumnSpacing = 10 };
        var stageInk = PodiumBrush(dark ? "#F5EEFF" : CommunityPodium.StageInk);
        var faceInk = PodiumBrush(CommunityPodium.StageInk);
        for (int index = 0; index < leaders.Count; index++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var leader = leaders[index];
            var (height, fill, ink) = CommunityPodium.Step(leader.Rank);
            var stepFill = PodiumBrush(fill);
            var stepInk = PodiumBrush(ink);
            var avatar = PodiumAvatar(stepFill, faceInk);
            var name = PodiumText(leader.Nickname, 14, stageInk);
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(name, leader.Nickname);
            var step = new Border
            {
                Background = stepFill, CornerRadius = new CornerRadius(12, 12, 4, 4), MinHeight = height,
                Padding = new Thickness(8, 6), Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { PodiumText($"#{leader.Rank}", 15, stepInk), PodiumText(leader.ScoreLabel(category), 19, stepInk) }
                }
            };
            var entry = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Spacing = 5, Children = { avatar, name, step } };
            Grid.SetColumn(entry, CommunityPodium.Column(index, leaders.Count));
            row.Children.Add(entry);
        }
        var heading = Heading("Top 3", 22);
        heading.Foreground = stageInk;
        var unit = Line(category == CommunityBoard.Supporters ? "Verified contributions" : "Audio translation time");
        unit.Foreground = stageInk;
        return new Border
        {
            Background = PodiumBrush(dark ? "#302542" : CommunityPodium.StageFill),
            BorderBrush = PodiumBrush(dark ? "#554269" : CommunityPodium.StageOutline),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 12),
            Child = new StackPanel { Spacing = 4, Children = { heading, unit, row } }
        };
    }

    private static Border PodiumAvatar(IBrush fill, IBrush ink)
    {
        var face = new Canvas { Width = 44, Height = 44 };
        face.Children.Add(new ShapePath
        {
            Data = Geometry.Parse(CommunityPodium.AvatarFace), Fill = fill, Stroke = ink, StrokeThickness = 2
        });
        face.Children.Add(new ShapePath
        {
            Data = Geometry.Parse(CommunityPodium.AvatarDetails), Stroke = ink, StrokeThickness = 2.6, StrokeLineCap = PenLineCap.Round
        });
        return new Border
        {
            Child = face, Background = PodiumBrush(CommunityPodium.AvatarFill), CornerRadius = new CornerRadius(22),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
        };
    }

    private static IBrush PodiumBrush(string color) => new SolidColorBrush(Color.Parse(color));

    private static TextBlock PodiumText(string text, double size, IBrush ink) => new()
    {
        Text = text, FontSize = size, Foreground = ink, FontWeight = FontWeight.SemiBold,
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
    };

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
    private static readonly IBrush Tint = new SolidColorBrush(Color.FromArgb(27, 133, 96, 197));

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
