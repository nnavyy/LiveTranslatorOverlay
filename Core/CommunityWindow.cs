using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShapePath = System.Windows.Shapes.Path;

namespace LiveTranslatorOverlay.Core;

public sealed class CommunityWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();

    public CommunityWindow(Window owner, TimeSpan localUsage, bool showSupport)
    {
        Owner = owner;
        Title = "Live Translator Community";
        Width = 720; Height = 680; MinWidth = 560; MinHeight = 440;
        FontSize = 14;
        if (!SystemParameters.HighContrast)
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml")
            });
        }
        Background = PodiumBrush("#202124", SystemColors.WindowBrush);
        Foreground = PodiumBrush("#F2F2F7", SystemColors.WindowTextBrush);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var tabs = new TabControl { Background = Background };
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
        var badge = PodiumAvatar(PodiumBrush(CommunityPodium.AvatarFill, SystemColors.WindowBrush),
            PodiumBrush(CommunityPodium.AvatarInk, SystemColors.WindowTextBrush));
        DockPanel.SetDock(badge, Dock.Right);
        panel.Children.Add(new DockPanel { Children =
        {
            badge, new StackPanel { Children =
            {
                Heading("Community rankings", 27),
                Line($"Your audio translation time · {GuestAudioUsage.Format(usage)} · Private")
            } }
        } });
        var board = new ComboBox { ItemsSource = new[] { "All-time usage", "This month (UTC)", "Supporters" }, SelectedIndex = 0, MinWidth = 154, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(0, 0, 8, 8) };
        System.Windows.Automation.AutomationProperties.SetName(board, "Ranking category");
        var search = new TextBox { MaxLength = 24, MinWidth = 132, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 8, 8) };
        System.Windows.Automation.AutomationProperties.SetName(search, "Search by nickname");
        var refresh = ActionButton("Find");
        var filters = new WrapPanel { Children = { board, search, refresh } };
        panel.Children.Add(Line("Board and nickname search", bottom: 4));
        panel.Children.Add(filters);
        var profile = new Popup { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Bottom, VerticalOffset = 6 };
        panel.Children.Add(profile);
        void OpenProfile(CommunityRank entry, Button origin)
        {
            profile.IsOpen = false;
            profile.PlacementTarget = origin;
            profile.Child = Profile(entry, () =>
            {
                profile.IsOpen = false;
                origin.Focus();
            });
            profile.IsOpen = true;
        }
        profile.Closed += (_, _) => profile.Child = null;
        var podium = new ContentControl();
        panel.Children.Add(podium);
        var previous = ActionButton("Previous");
        var next = ActionButton("Next");
        var status = Line("", bottom: 0);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        var scoreColumn = Column("Audio time", "Score", 128);
        var results = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserSortColumns = false, CanUserResizeColumns = false, CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = PodiumBrush("#383842", SystemColors.ControlLightBrush),
            Background = Background, Foreground = Foreground,
            MaxHeight = 250, RowHeight = 36, Margin = new Thickness(0, 4, 0, 10)
        };
        results.Columns.Add(Column("Rank", "Rank", 66));
        var nickname = new FrameworkElementFactory(typeof(Button));
        nickname.SetBinding(Button.ContentProperty, new Binding("Nickname"));
        nickname.SetBinding(Button.TagProperty, new Binding("Entry"));
        nickname.SetValue(Button.StyleProperty, ProfileButtonStyle());
        nickname.SetValue(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
        nickname.AddHandler(Button.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Button { Tag: CommunityRank entry } button) OpenProfile(entry, button);
        }));
        results.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Nickname", CellTemplate = new DataTemplate { VisualTree = nickname },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        results.Columns.Add(scoreColumn);
        System.Windows.Automation.AutomationProperties.SetName(results, "Ranking results");
        var standings = new StackPanel { Children = { Heading("Full rankings", 16), results } };
        panel.Children.Add(standings);
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
            string query = search.Text;
            scoreColumn.Header = category == CommunityBoard.Supporters ? "Contributed" : "Audio time";
            status.Text = "Loading rankings…";
            message.Visibility = Visibility.Visible;
            profile.IsOpen = false;
            podium.Content = null;
            standings.Visibility = Visibility.Collapsed;
            pages.Visibility = Visibility.Collapsed;
            results.ItemsSource = null;
            try
            {
                var data = await client.ReadAsync(category, query, page, token);
                if (token.IsCancellationRequested) return;
                offset = page;
                status.Text = data.Message;
                message.Visibility = data.Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                var leaders = data.PodiumEntries(query, page);
                if (leaders.Count > 0) podium.Content = Podium(leaders, category, OpenProfile);
                results.ItemsSource = data.Entries.Select(entry => new { entry.Rank, entry.Nickname, Score = entry.ScoreLabel(category), Entry = entry }).ToArray();
                standings.Visibility = data.Entries.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                previous.IsEnabled = data.Available && offset > 0;
                next.IsEnabled = data.Available && data.Entries.Length == CommunityClient.PageSize;
                pages.Visibility = previous.IsEnabled || next.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
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

    private static Border Podium(IReadOnlyList<CommunityRank> leaders, CommunityBoard category, Action<CommunityRank, Button> openProfile)
    {
        var row = new Grid { MaxWidth = leaders.Count * 184 };
        var stageInk = PodiumBrush(CommunityPodium.StageInk, SystemColors.WindowTextBrush);
        var avatarInk = PodiumBrush(CommunityPodium.AvatarInk, SystemColors.WindowTextBrush);
        for (int index = 0; index < leaders.Count; index++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var leader = leaders[index];
            var (height, fill, ink) = CommunityPodium.Step(leader.Rank);
            var stepFill = PodiumBrush(fill, SystemColors.ControlBrush);
            var stepInk = PodiumBrush(ink, SystemColors.ControlTextBrush);
            var avatar = PodiumAvatar(stepInk, avatarInk);
            var name = PodiumText(leader.Nickname, 14, stageInk);
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var profile = new Button { Content = name, Style = ProfileButtonStyle() };
            System.Windows.Automation.AutomationProperties.SetName(profile, leader.Nickname);
            profile.Click += (_, _) => openProfile(leader, profile);
            var step = new Border
            {
                Background = stepFill, BorderBrush = stepInk, BorderThickness = SystemParameters.HighContrast ? new Thickness(1) : new Thickness(0),
                CornerRadius = new CornerRadius(12, 12, 4, 4), MinHeight = height, Padding = new Thickness(8, 6, 8, 6),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { PodiumText($"#{leader.Rank}", 15, stepInk), PodiumText(leader.ScoreLabel(category), 19, stepInk) }
                }
            };
            var entry = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(5, 0, 5, 0), Children = { avatar, profile, step } };
            Grid.SetColumn(entry, CommunityPodium.Column(index, leaders.Count));
            row.Children.Add(entry);
        }
        var heading = Heading("Top 3", 22);
        heading.Foreground = stageInk;
        var unit = Line(category == CommunityBoard.Supporters ? "Verified contributions" : "Audio translation time", bottom: 1);
        unit.Foreground = stageInk;
        var stage = new Border
        {
            Background = PodiumBrush(CommunityPodium.StageFill, SystemColors.WindowBrush),
            BorderBrush = PodiumBrush(CommunityPodium.StageOutline, SystemColors.WindowTextBrush),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 4, 0, 15),
            Child = new StackPanel { Children = { heading, unit, row } }
        };
        if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
        {
            var shift = new TranslateTransform();
            row.RenderTransform = shift;
            var duration = TimeSpan.FromMilliseconds(220);
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            });
            row.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 1, duration) { FillBehavior = FillBehavior.Stop });
        }
        return stage;
    }

    private static Border Profile(CommunityRank entry, Action closeProfile)
    {
        var close = ActionButton("Close profile");
        close.Margin = new Thickness(8, 0, 0, 0);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => closeProfile();
        var avatar = PodiumAvatar(PodiumBrush(CommunityPodium.AvatarFill, SystemColors.WindowBrush),
            PodiumBrush(CommunityPodium.AvatarInk, SystemColors.WindowTextBrush));
        avatar.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(avatar, Dock.Left);
        DockPanel.SetDock(close, Dock.Right);
        var header = new DockPanel { Children = { avatar, close, Heading(entry.Nickname, 22) } };
        var badges = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var detail = Line("", size: 12, bottom: 0);
        detail.Visibility = Visibility.Collapsed;
        foreach (var badge in CommunityBadge.For(entry))
        {
            var icon = new Canvas { Width = 28, Height = 28 };
            icon.Children.Add(new ShapePath { Data = Geometry.Parse(badge.Body), Fill = PodiumBrush(badge.Color, SystemColors.HotTrackBrush) });
            icon.Children.Add(new ShapePath { Data = Geometry.Parse(badge.Detail), Fill = PodiumBrush("#302241", SystemColors.ControlBrush) });
            var tile = new Button
            {
                Content = icon, Width = 46, Height = 46, Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 8, 6), ToolTip = badge.Name
            };
            System.Windows.Automation.AutomationProperties.SetName(tile, badge.Name);
            tile.Click += (_, _) =>
            {
                detail.Text = badge.Name;
                detail.Visibility = Visibility.Visible;
            };
            badges.Children.Add(tile);
        }
        if (badges.Children.Count == 0) badges.Children.Add(Line("No public badges", bottom: 0));
        var card = Surface(new StackPanel { Children =
        {
            header, Line("Verified contributions", size: 12, bottom: 2),
            Heading(entry.SupportLabel, 19), Heading("Badges", 14), badges, detail
        } }, tinted: true);
        card.Width = 328;
        card.Margin = new Thickness(0);
        card.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,
            PodiumBrush(CommunityPodium.StageInk, SystemColors.WindowTextBrush));
        card.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Escape) return;
            closeProfile();
            args.Handled = true;
        };
        card.Loaded += (_, _) => close.Focus();
        return card;
    }

    private static Style ProfileButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(ForegroundProperty, PodiumBrush("#E9DAFF", SystemColors.HotTrackBrush)));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 2, 6, 2)));
        style.Setters.Add(new Setter(ToolTipProperty, "View profile"));
        return style;
    }

    private static Border PodiumAvatar(Brush fill, Brush ink)
    {
        var face = new Canvas { Width = 44, Height = 44 };
        face.Children.Add(new ShapePath
        {
            Data = Geometry.Parse(CommunityPodium.AvatarFace), Fill = fill, Stroke = ink, StrokeThickness = 2
        });
        face.Children.Add(new ShapePath
        {
            Data = Geometry.Parse(CommunityPodium.AvatarDetails), Stroke = ink, StrokeThickness = 2.6,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        });
        return new Border
        {
            Child = face, Background = PodiumBrush(CommunityPodium.AvatarFill, SystemColors.WindowBrush),
            CornerRadius = new CornerRadius(22), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
        };
    }

    private static Brush PodiumBrush(string color, Brush highContrast) =>
        SystemParameters.HighContrast ? highContrast : (Brush)new BrushConverter().ConvertFromInvariantString(color)!;

    private static TextBlock PodiumText(string text, double size, Brush ink) => new()
    {
        Text = text, FontSize = size, Foreground = ink, FontWeight = FontWeights.SemiBold,
        TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
    };

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
        var terms = ActionButton("Terms and privacy");
        terms.Click += (_, _) => App.ShowTerms(this);
        var close = ActionButton("Close");
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
        var send = ActionButton("Send code");
        var submit = ActionButton("Submit private request");
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

    private static DataGridTextColumn Column(string title, string path, double width)
    {
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
        text.Setters.Add(new Setter(MarginProperty, new Thickness(4, 0, 0, 0)));
        return new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = width, ElementStyle = text };
    }

    private static Border Surface(UIElement child, bool tinted) => new()
    {
        Child = child, Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 10),
        CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
        Background = PodiumBrush(tinted ? CommunityPodium.StageFill : "#28282E", SystemColors.WindowBrush),
        BorderBrush = PodiumBrush(tinted ? CommunityPodium.StageOutline : "#41414A", SystemColors.WindowTextBrush)
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

    private static Button ActionButton(string text) => new() { Content = text, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 8) };
}
