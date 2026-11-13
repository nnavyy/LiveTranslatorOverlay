using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ShapePath = System.Windows.Shapes.Path;

namespace LiveTranslatorOverlay.Core;

public sealed class CommunityWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();

    public CommunityWindow(Window owner, TimeSpan localUsage, bool showSupport)
    {
        Owner = owner;
        Title = "Live Translator Community";
        var workArea = SystemParameters.WorkArea;
        double maxWidth = workArea.Width > 32 ? workArea.Width - 32 : 740;
        double maxHeight = workArea.Height > 32 ? workArea.Height - 32 : 760;
        MinWidth = Math.Min(480, maxWidth);
        MinHeight = Math.Min(400, maxHeight);
        Width = Math.Clamp(740, MinWidth, maxWidth);
        Height = Math.Clamp(760, MinHeight, maxHeight);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 14;
        if (!SystemParameters.HighContrast)
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml")
            });
            Resources.MergedDictionaries.Add(CommunityStyles);
        }
        Background = PodiumBrush("#1C1C22", SystemColors.WindowBrush);
        Foreground = PodiumBrush("#F2F2F7", SystemColors.WindowTextBrush);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var tabs = new TabControl { Background = Background };
        ApplyStyle(tabs, "CommunityTabControl");
        var rankingsTab = new TabItem { Header = "Rankings", Content = Rankings(localUsage) };
        var supportTab = new TabItem { Header = "Support development", Content = Offers() };
        ApplyStyle(rankingsTab, "CommunityTabItem");
        ApplyStyle(supportTab, "CommunityTabItem");
        tabs.Items.Add(rankingsTab);
        tabs.Items.Add(supportTab);
        tabs.SelectedIndex = showSupport ? 1 : 0;

        var footer = FooterBar();
        DockPanel.SetDock(footer, Dock.Bottom);
        var layout = new DockPanel { LastChildFill = true };
        layout.Children.Add(footer);
        layout.Children.Add(tabs);
        Content = layout;

        SourceInitialized += (_, _) => CaptureExclusion.ExcludeFromCapture(this);
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private Border FooterBar()
    {
        var terms = ActionButton("Terms and privacy", bottomMargin: 0);
        terms.Click += (_, _) => App.ShowTerms(this);
        var close = ActionButton("Close", bottomMargin: 0, rightMargin: 0);
        close.MinWidth = 88;
        close.IsCancel = true;
        close.ToolTip = "Close window (Esc)";
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        var bar = new DockPanel { LastChildFill = false };
        bar.Children.Add(terms);
        bar.Children.Add(close);
        return new Border
        {
            Background = PodiumBrush("#18181E", SystemColors.WindowBrush),
            BorderBrush = PodiumBrush("#2E2E38", SystemColors.WindowTextBrush),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 12, 24, 12),
            Child = bar
        };
    }

    private UIElement Rankings(TimeSpan usage)
    {
        var client = new CommunityClient(AppSettings.DataDirectory);
        var panel = new StackPanel { Margin = new Thickness(24, 10, 24, 10) };

        var badge = PodiumAvatar(
            PodiumBrush(CommunityPodium.AvatarFill, SystemColors.WindowBrush),
            PodiumBrush(CommunityPodium.AvatarInk, SystemColors.WindowTextBrush));
        badge.Margin = new Thickness(16, 0, 0, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(badge, Dock.Right);
        var subtitle = MutedLine($"Your audio translation time · {GuestAudioUsage.Format(usage)} · Private to this device", size: 13, bottom: 0);
        panel.Children.Add(new DockPanel
        {
            Margin = new Thickness(0, 0, 0, 10),
            Children =
            {
                badge,
                new StackPanel { Children = { Heading("Community rankings", 24, bottom: 2), subtitle } }
            }
        });

        panel.Children.Add(MutedLine("Board and nickname search", size: 12, bottom: 6, semiBold: true));
        var board = new ComboBox
        {
            ItemsSource = new[] { "All-time usage", "This month (UTC)", "Supporters" },
            SelectedIndex = 0,
            MinWidth = 156,
            Margin = new Thickness(0, 0, 8, 0)
        };
        ApplyStyle(board, "CommunityComboBox");
        System.Windows.Automation.AutomationProperties.SetName(board, "Ranking category");

        var search = new TextBox
        {
            MaxLength = 24,
            MinWidth = 120,
            Padding = new Thickness(10, 6, 30, 6)
        };
        ApplyStyle(search, "CommunityTextBox");
        System.Windows.Automation.AutomationProperties.SetName(search, "Search by nickname");

        var placeholder = new TextBlock
        {
            Text = "Search nickname…",
            Foreground = PodiumBrush("#8C8C9A", SystemColors.GrayTextBrush),
            FontSize = 13,
            Margin = new Thickness(12, 0, 30, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var clearSearch = new Button
        {
            Content = "✕",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            FontSize = 12,
            ToolTip = "Clear search"
        };
        ApplyStyle(clearSearch, "CommunityIconButton");
        System.Windows.Automation.AutomationProperties.SetName(clearSearch, "Clear search");

        var searchHost = new Grid { Margin = new Thickness(0, 0, 8, 0) };
        searchHost.Children.Add(search);
        searchHost.Children.Add(placeholder);
        searchHost.Children.Add(clearSearch);

        var refresh = ActionButton("Find", primary: true, bottomMargin: 0, rightMargin: 0);
        refresh.MinWidth = 72;

        var filters = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(board, 0);
        Grid.SetColumn(searchHost, 1);
        Grid.SetColumn(refresh, 2);
        filters.Children.Add(board);
        filters.Children.Add(searchHost);
        filters.Children.Add(refresh);
        panel.Children.Add(filters);

        var profile = new Popup
        {
            AllowsTransparency = true,
            StaysOpen = false,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6
        };
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

        var loadingBar = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 3,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = PodiumBrush("#0A84FF", SystemColors.HighlightBrush),
            Background = PodiumBrush("#32323E", SystemColors.ControlDarkBrush),
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed
        };
        var status = Line("", bottom: 0);
        status.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        var statusAction = ActionButton("Retry", bottomMargin: 0, rightMargin: 0);
        statusAction.Margin = new Thickness(12, 0, 0, 0);
        statusAction.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(statusAction, Dock.Right);
        var statusRow = new DockPanel { LastChildFill = true };
        statusRow.Children.Add(statusAction);
        statusRow.Children.Add(status);
        var message = Surface(new StackPanel { Children = { loadingBar, statusRow } }, tinted: false);
        message.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(message);

        var scoreColumn = Column("Audio time", "Score", 124);
        var results = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserSortColumns = false,
            CanUserResizeColumns = false,
            CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = PodiumBrush("#2D2D38", SystemColors.ControlLightBrush),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = PodiumBrush("#202127", SystemColors.WindowBrush),
            Foreground = Foreground,
            BorderThickness = new Thickness(0),
            MaxHeight = 180,
            RowHeight = 36
        };
        ApplyStyle(results, "CommunityDataGrid");
        results.Columns.Add(Column("Rank", "Rank", 64, muted: true));
        results.Columns.Add(NicknameColumn(OpenProfile));
        results.Columns.Add(scoreColumn);
        System.Windows.Automation.AutomationProperties.SetName(results, "Ranking results");

        var tableBorder = new Border
        {
            Background = PodiumBrush("#202127", SystemColors.WindowBrush),
            BorderBrush = PodiumBrush("#383844", SystemColors.WindowTextBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(1),
            Child = results
        };

        var rangeLabel = MutedLine("", size: 12.5, bottom: 0);
        rangeLabel.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(rangeLabel, Dock.Right);
        var standingsHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        standingsHeader.Children.Add(rangeLabel);
        standingsHeader.Children.Add(Heading("Full rankings", 16, bottom: 0));
        var standings = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
            Children = { standingsHeader, tableBorder }
        };
        panel.Children.Add(standings);

        var previous = ActionButton("Previous", bottomMargin: 0, rightMargin: 8);
        var next = ActionButton("Next", bottomMargin: 0, rightMargin: 0);
        var pageLabel = MutedLine("", size: 12.5, bottom: 0);
        pageLabel.VerticalAlignment = VerticalAlignment.Center;
        pageLabel.Margin = new Thickness(12, 0, 0, 0);
        var pages = new WrapPanel { Margin = new Thickness(0, 2, 0, 0), Children = { previous, next, pageLabel } };
        panel.Children.Add(pages);

        int offset = 0;
        bool busy = false;
        Action? currentStatusAction = null;
        statusAction.Click += (_, _) => currentStatusAction?.Invoke();

        void UpdateSearchChrome()
        {
            bool hasText = search.Text.Length > 0;
            placeholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            clearSearch.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => UpdateSearchChrome();

        void SetMessageStyle(bool isError)
        {
            if (isError)
            {
                message.Background = PodiumBrush("#361F25", SystemColors.WindowBrush);
                message.BorderBrush = PodiumBrush("#8A4550", SystemColors.WindowTextBrush);
                status.Foreground = PodiumBrush("#FFD6DC", SystemColors.WindowTextBrush);
            }
            else
            {
                message.Background = PodiumBrush("#24242C", SystemColors.WindowBrush);
                message.BorderBrush = PodiumBrush("#383844", SystemColors.WindowTextBrush);
                status.Foreground = PodiumBrush("#D8D8E2", SystemColors.WindowTextBrush);
            }
        }

        async Task Load(int page)
        {
            if (busy || board.SelectedIndex < 0) return;
            var token = _lifetime.Token;
            busy = true;
            board.IsEnabled = search.IsEnabled = refresh.IsEnabled = clearSearch.IsEnabled = previous.IsEnabled = next.IsEnabled = false;
            var category = (CommunityBoard)board.SelectedIndex;
            string query = search.Text;
            scoreColumn.Header = category == CommunityBoard.Supporters ? "Contributed" : "Audio time";
            SetMessageStyle(isError: false);
            loadingBar.Visibility = Visibility.Visible;
            statusAction.Visibility = Visibility.Collapsed;
            currentStatusAction = null;
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
                loadingBar.Visibility = Visibility.Collapsed;
                bool isError = !data.Available && data.Message == CommunityClient.Unavailable;
                SetMessageStyle(isError);
                status.Text = data.Message;
                if (isError)
                {
                    statusAction.Content = "Retry";
                    statusAction.Visibility = Visibility.Visible;
                    currentStatusAction = async () => await Load(offset);
                }
                else if (data.Available && data.Entries.Length == 0 && !string.IsNullOrWhiteSpace(query))
                {
                    statusAction.Content = "Clear search";
                    statusAction.Visibility = Visibility.Visible;
                    currentStatusAction = async () => { search.Clear(); await Load(0); };
                }
                message.Visibility = data.Message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                var leaders = data.PodiumEntries(query, page);
                if (leaders.Count > 0) podium.Content = Podium(leaders, category, OpenProfile);
                results.ItemsSource = data.Entries.Select(entry => new { entry.Rank, entry.Nickname, Score = entry.ScoreLabel(category), Entry = entry }).ToArray();
                standings.Visibility = data.Entries.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                rangeLabel.Text = data.Entries.Length > 0 ? $"Showing {offset + 1}–{offset + data.Entries.Length}" : "";
                previous.IsEnabled = data.Available && offset > 0;
                next.IsEnabled = data.Available && data.Entries.Length == CommunityClient.PageSize;
                int pageNumber = (offset / CommunityClient.PageSize) + 1;
                pageLabel.Text = $"Page {pageNumber}";
                pages.Visibility = previous.IsEnabled || next.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    loadingBar.Visibility = Visibility.Collapsed;
                    board.IsEnabled = search.IsEnabled = refresh.IsEnabled = clearSearch.IsEnabled = true;
                }
                busy = false;
            }
        }

        clearSearch.Click += async (_, _) => { search.Clear(); search.Focus(); await Load(0); };
        refresh.Click += async (_, _) => await Load(0);
        search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Load(0); };
        previous.Click += async (_, _) => await Load(Math.Max(0, offset - CommunityClient.PageSize));
        next.Click += async (_, _) => await Load(offset + CommunityClient.PageSize);
        board.SelectionChanged += async (_, _) => await Load(0);
        Loaded += async (_, _) => await Load(0);
        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private DataGridTemplateColumn NicknameColumn(Action<CommunityRank, Button> openProfile)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("Nickname"));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

        var button = new FrameworkElementFactory(typeof(Button));
        button.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        button.SetValue(HorizontalContentAlignmentProperty, HorizontalAlignment.Left);
        button.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        button.SetValue(MarginProperty, new Thickness(6, 0, 6, 0));
        button.SetBinding(TagProperty, new Binding("Entry"));
        button.SetBinding(ToolTipProperty, new Binding("Nickname") { StringFormat = "View profile · {0}" });
        button.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding("Nickname"));
        if (!SystemParameters.HighContrast && TryFindResource("CommunityLinkButton") is Style linkStyle)
            button.SetValue(StyleProperty, linkStyle);
        else
            button.SetValue(StyleProperty, HighContrastLinkStyle());
        button.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Button { Tag: CommunityRank entry } origin) openProfile(entry, origin);
        }));
        button.AppendChild(text);

        return new DataGridTemplateColumn
        {
            Header = "Nickname",
            CellTemplate = new DataTemplate { VisualTree = button },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        };
    }

    private Border Podium(IReadOnlyList<CommunityRank> leaders, CommunityBoard category, Action<CommunityRank, Button> openProfile)
    {
        var row = new Grid { MaxWidth = leaders.Count * 192 };
        var stageInk = PodiumBrush(CommunityPodium.StageInk, SystemColors.WindowTextBrush);
        var mutedStageInk = PodiumBrush("#CEC2E6", SystemColors.WindowTextBrush);
        var avatarInk = PodiumBrush(CommunityPodium.AvatarInk, SystemColors.WindowTextBrush);

        for (int index = 0; index < leaders.Count; index++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var leader = leaders[index];
            var (height, fill, ink) = CommunityPodium.Step(leader.Rank);
            var stepFill = PodiumBrush(fill, SystemColors.ControlBrush);
            var stepInk = PodiumBrush(ink, SystemColors.ControlTextBrush);
            var avatar = PodiumAvatar(stepInk, avatarInk, size: 40);
            avatar.Margin = new Thickness(0, 0, 0, 4);

            var name = new TextBlock
            {
                Text = leader.Nickname,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = PodiumBrush("#E9DAFF", SystemColors.HotTrackBrush),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var profile = new Button
            {
                Content = name,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = $"View profile · {leader.Nickname}"
            };
            if (!SystemParameters.HighContrast && TryFindResource("CommunityLinkButton") is Style linkStyle)
                profile.Style = linkStyle;
            else
                profile.Style = HighContrastLinkStyle();
            System.Windows.Automation.AutomationProperties.SetName(profile, leader.Nickname);
            profile.Click += (_, _) => openProfile(leader, profile);

            var step = new Border
            {
                Background = stepFill,
                BorderBrush = stepInk,
                BorderThickness = SystemParameters.HighContrast ? new Thickness(1) : new Thickness(0),
                CornerRadius = new CornerRadius(10, 10, 4, 4),
                MinHeight = height,
                Padding = new Thickness(8, 6, 8, 6),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        PodiumText($"#{leader.Rank}", 14, stepInk),
                        PodiumText(leader.ScoreLabel(category), 17, stepInk)
                    }
                }
            };
            var entry = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(6, 0, 6, 0),
                Children = { avatar, profile, step }
            };
            Grid.SetColumn(entry, CommunityPodium.Column(index, leaders.Count));
            row.Children.Add(entry);
        }

        var heading = Heading("Top 3", 18, bottom: 0);
        heading.Foreground = stageInk;
        heading.VerticalAlignment = VerticalAlignment.Center;
        var unit = MutedLine(category == CommunityBoard.Supporters ? "Verified contributions" : "Audio translation time", size: 12.5, bottom: 0);
        unit.Foreground = mutedStageInk;
        unit.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(unit, Dock.Right);
        var stageHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        stageHeader.Children.Add(unit);
        stageHeader.Children.Add(heading);

        var stage = new Border
        {
            Background = PodiumBrush(CommunityPodium.StageFill, SystemColors.WindowBrush),
            BorderBrush = PodiumBrush(CommunityPodium.StageOutline, SystemColors.WindowTextBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = new StackPanel { Children = { stageHeader, row } }
        };
        if (SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
        {
            var shift = new TranslateTransform();
            row.RenderTransform = shift;
            var duration = TimeSpan.FromMilliseconds(220);
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
            row.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 1, duration) { FillBehavior = FillBehavior.Stop });
        }
        return stage;
    }

    private static Border Profile(CommunityRank entry, Action closeProfile)
    {
        var close = new Button
        {
            Content = "Close",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "Close profile (Esc)"
        };
        if (!SystemParameters.HighContrast) close.Style = (Style)CommunityStyles["CommunityButton"];
        System.Windows.Automation.AutomationProperties.SetName(close, "Close profile");
        close.Click += (_, _) => closeProfile();

        var avatar = PodiumAvatar(
            PodiumBrush(CommunityPodium.AvatarFill, SystemColors.WindowBrush),
            PodiumBrush(CommunityPodium.AvatarInk, SystemColors.WindowTextBrush),
            size: 40);
        avatar.Margin = new Thickness(0, 0, 12, 0);

        var nameText = new TextBlock
        {
            Text = entry.Nickname,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = PodiumBrush(CommunityPodium.StageInk, SystemColors.WindowTextBrush),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            ToolTip = entry.Nickname
        };
        var rankLine = new TextBlock
        {
            Text = $"Rank #{entry.Rank}",
            FontSize = 12.5,
            Foreground = PodiumBrush("#CEC2E6", SystemColors.WindowTextBrush),
            Margin = new Thickness(0, 2, 0, 0)
        };
        var nameStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { nameText, rankLine }
        };

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(avatar, 0);
        Grid.SetColumn(nameStack, 1);
        Grid.SetColumn(close, 2);
        header.Children.Add(avatar);
        header.Children.Add(nameStack);
        header.Children.Add(close);

        var divider = new Border
        {
            Height = 1,
            Background = PodiumBrush("#463A5C", SystemColors.ControlLightBrush),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var contributionsLabel = new TextBlock
        {
            Text = "Verified contributions",
            FontSize = 12,
            Foreground = PodiumBrush("#CEC2E6", SystemColors.WindowTextBrush),
            Margin = new Thickness(0, 0, 0, 2)
        };
        var contributionsValue = Heading(entry.SupportLabel, 18, bottom: 12);

        var badgesLabel = new TextBlock
        {
            Text = "Badges",
            FontSize = 12,
            Foreground = PodiumBrush("#CEC2E6", SystemColors.WindowTextBrush),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var badges = new WrapPanel();
        var detail = new TextBlock
        {
            Text = "",
            FontSize = 12.5,
            Foreground = PodiumBrush("#CEC2E6", SystemColors.WindowTextBrush),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };

        foreach (var badge in CommunityBadge.For(entry))
        {
            var icon = BadgeIcon(badge, 20);
            icon.Margin = new Thickness(0, 0, 6, 0);
            var label = new TextBlock
            {
                Text = badge.Name,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { icon, label }
            };
            var tile = new Button
            {
                Content = content,
                Margin = new Thickness(0, 0, 8, 6),
                ToolTip = badge.Name
            };
            if (!SystemParameters.HighContrast) tile.Style = (Style)CommunityStyles["CommunityBadgeTile"];
            System.Windows.Automation.AutomationProperties.SetName(tile, badge.Name);
            tile.Click += (_, _) =>
            {
                detail.Text = $"{badge.Name} medal — voluntary lifetime support";
                detail.Visibility = Visibility.Visible;
            };
            badges.Children.Add(tile);
        }
        if (badges.Children.Count == 0)
        {
            badges.Children.Add(new TextBlock
            {
                Text = "No public badges",
                FontSize = 13,
                Foreground = PodiumBrush("#B8B0CA", SystemColors.GrayTextBrush)
            });
        }

        var card = new Border
        {
            Width = 344,
            Background = PodiumBrush(CommunityPodium.StageFill, SystemColors.WindowBrush),
            BorderBrush = PodiumBrush("#5E4E75", SystemColors.WindowTextBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Children = { header, divider, contributionsLabel, contributionsValue, badgesLabel, badges, detail }
            }
        };
        if (!SystemParameters.HighContrast)
        {
            card.Resources.MergedDictionaries.Add(CommunityStyles);
            card.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 4, Opacity = 0.48 };
        }
        card.SetValue(System.Windows.Documents.TextElement.ForegroundProperty,
            PodiumBrush(CommunityPodium.StageInk, SystemColors.WindowTextBrush));
        card.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            closeProfile();
            args.Handled = true;
        };
        card.Loaded += (_, _) => close.Focus();
        return card;
    }

    private UIElement Offers()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };

        var title = Heading("Support development", 24, bottom: 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(0, 0, 12, 4);
        var licensePill = PillBadge("MIT Open Source · Free forever", "#1D3329", "#3A6B52", "#B8F2D0");
        licensePill.Margin = new Thickness(0, 0, 0, 4);
        licensePill.VerticalAlignment = VerticalAlignment.Center;
        var titleWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        titleWrap.Children.Add(title);
        titleWrap.Children.Add(licensePill);
        panel.Children.Add(titleWrap);

        var intro = Line(SupportCatalog.Intro, size: 13.5, bottom: 16);
        intro.Foreground = PodiumBrush("#D4D4DE", SystemColors.WindowTextBrush);
        panel.Children.Add(intro);

        var lifetimeGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        lifetimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lifetimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        lifetimeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var lifetimeCards = new List<Border>();
        foreach (var offer in SupportCatalog.Offers)
        {
            if (offer.Cents.HasValue)
            {
                var card = LifetimeOfferCard(offer);
                int col = lifetimeCards.Count == 0 ? 0 : 2;
                Grid.SetColumn(card, col);
                lifetimeGrid.Children.Add(card);
                lifetimeCards.Add(card);
            }
            else
            {
                panel.Children.Add(lifetimeGrid);
                panel.Children.Add(MutedLine(SupportCatalog.IndependenceNote, size: 12.5, bottom: 12));
                panel.Children.Add(DonationCard(offer));
            }
        }

        var purchaseHeader = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var purchaseHeading = Heading("Purchase options", 15.5, bottom: 0);
        purchaseHeading.VerticalAlignment = VerticalAlignment.Center;
        purchaseHeading.Margin = new Thickness(0, 0, 10, 2);
        var previewPill = PillBadge("Preview only", "#3B3052", "#68558A", "#E9DAFF");
        previewPill.Margin = new Thickness(0, 0, 0, 2);
        previewPill.VerticalAlignment = VerticalAlignment.Center;
        purchaseHeader.Children.Add(purchaseHeading);
        purchaseHeader.Children.Add(previewPill);

        var closedLine = Line(SupportCatalog.Closed, size: 13.5, bottom: 4);
        closedLine.FontWeight = FontWeights.SemiBold;
        var availabilityLine = MutedLine(SupportCatalog.AvailabilityNote, size: 13, bottom: 0);
        availabilityLine.Foreground = PodiumBrush("#CEC2E6", SystemColors.WindowTextBrush);

        var purchase = new StackPanel { Children = { purchaseHeader, closedLine, availabilityLine } };
        var client = new CommunityClient(AppSettings.DataDirectory);
        if (client.ManualRequestPreview)
        {
            var expander = new Expander
            {
                Header = "Developer-assisted request (staging preview)",
                Margin = new Thickness(0, 12, 0, 0),
                Content = ManualRequestForm(client)
            };
            ApplyStyle(expander, "CommunityExpander");
            purchase.Children.Add(expander);
        }
        var purchaseSurface = Surface(purchase, tinted: true);
        purchaseSurface.Margin = new Thickness(0);
        panel.Children.Add(purchaseSurface);

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private static Border LifetimeOfferCard(SupportOffer offer)
    {
        var badge = CommunityBadge.ForOffer(offer.Id);
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        if (badge != null)
        {
            var icon = BadgeIcon(badge, 20);
            icon.Margin = new Thickness(0, 0, 8, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(icon);
        }
        var title = Heading(offer.Title, 15.5, bottom: 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(title);

        var price = Heading(offer.Price, 22, bottom: 2);
        if (badge != null) price.Foreground = PodiumBrush(badge.Color, SystemColors.WindowTextBrush);
        var subtitle = MutedLine("Voluntary · One-time", size: 12, bottom: 8);
        var description = Line(offer.Description, size: 13, bottom: 0);
        description.Foreground = PodiumBrush("#D4D4DE", SystemColors.WindowTextBrush);

        var card = Surface(new StackPanel { Children = { titleRow, price, subtitle, description } }, tinted: false);
        card.Margin = new Thickness(0);
        return card;
    }

    private static Border DonationCard(SupportOffer offer)
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var price = Heading(offer.Price, 14.5, bottom: 0);
        price.Foreground = PodiumBrush("#E9DAFF", SystemColors.WindowTextBrush);
        price.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(price, Dock.Right);
        header.Children.Add(price);
        var title = Heading(offer.Title, 15.5, bottom: 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);

        var description = Line(offer.Description, size: 13, bottom: 0);
        description.Foreground = PodiumBrush("#D4D4DE", SystemColors.WindowTextBrush);
        var card = Surface(new StackPanel { Children = { header, description } }, tinted: false);
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    private UIElement ManualRequestForm(CommunityClient client)
    {
        var lifetimeOffers = SupportCatalog.Offers.Where(offer => offer.Cents.HasValue).ToArray();
        var offer = new ComboBox
        {
            ItemsSource = lifetimeOffers,
            DisplayMemberPath = nameof(SupportOffer.Label),
            SelectedIndex = 0,
            Margin = new Thickness(0, 0, 0, 10)
        };
        ApplyStyle(offer, "CommunityComboBox");
        var email = new TextBox { MaxLength = 254, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 8) };
        ApplyStyle(email, "CommunityTextBox");
        var code = new TextBox { MaxLength = 6, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 8) };
        ApplyStyle(code, "CommunityTextBox");

        System.Windows.Automation.AutomationProperties.SetName(offer, "Lifetime offer");
        System.Windows.Automation.AutomationProperties.SetName(email, "Staging account email");
        System.Windows.Automation.AutomationProperties.SetName(code, "Six-digit email code");

        var send = ActionButton("Send code", bottomMargin: 10, rightMargin: 0);
        var submit = ActionButton("Submit request", primary: true, bottomMargin: 8, rightMargin: 0);
        var status = MutedLine("", size: 13, bottom: 0);
        status.Foreground = PodiumBrush("#E5E5F0", SystemColors.WindowTextBrush);
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);

        async Task Run(Func<CancellationToken, Task<string>> action)
        {
            var token = _lifetime.Token;
            offer.IsEnabled = email.IsEnabled = code.IsEnabled = send.IsEnabled = submit.IsEnabled = false;
            status.Text = "Working…";
            try { status.Text = await action(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                if (!token.IsCancellationRequested)
                    offer.IsEnabled = email.IsEnabled = code.IsEnabled = send.IsEnabled = submit.IsEnabled = true;
            }
        }
        send.Click += async (_, _) => await Run(token => client.SendPurchaseCodeAsync(email.Text.Trim(), token));
        submit.Click += async (_, _) =>
        {
            if (offer.SelectedItem is not SupportOffer selected) return;
            string digits = code.Text.Trim();
            code.Clear();
            await Run(token => client.RequestManualPurchaseAsync(email.Text.Trim(), digits, selected.Id, token));
        };

        var notice = MutedLine("Staging preview only — no payment is charged. Your email is sent to the sign-in service to verify your request.", size: 12.5, bottom: 10);
        notice.Foreground = PodiumBrush("#CEC2E6", SystemColors.WindowTextBrush);

        return new StackPanel
        {
            Margin = new Thickness(4, 10, 4, 2),
            Children =
            {
                notice,
                MutedLine("Lifetime offer", size: 12, bottom: 4, semiBold: true),
                offer,
                MutedLine("Staging account email", size: 12, bottom: 4, semiBold: true),
                email,
                new WrapPanel { Children = { send } },
                MutedLine("Six-digit email code", size: 12, bottom: 4, semiBold: true),
                code,
                new WrapPanel { Children = { submit } },
                status
            }
        };
    }

    private static Canvas BadgeIcon(CommunityBadge badge, double size)
    {
        double scale = size / 28.0;
        var icon = new Canvas
        {
            Width = size,
            Height = size,
            RenderTransform = new ScaleTransform(scale, scale)
        };
        icon.Children.Add(new ShapePath
        {
            Data = Geometry.Parse(badge.Body),
            Fill = PodiumBrush(badge.Color, SystemColors.HotTrackBrush)
        });
        icon.Children.Add(new ShapePath
        {
            Data = Geometry.Parse(badge.Detail),
            Fill = PodiumBrush("#302241", SystemColors.ControlBrush)
        });
        return icon;
    }

    private static Border PillBadge(string text, string fill, string border, string ink) => new()
    {
        Background = PodiumBrush(fill, SystemColors.ControlBrush),
        BorderBrush = PodiumBrush(border, SystemColors.WindowTextBrush),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 3, 8, 3),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = PodiumBrush(ink, SystemColors.ControlTextBrush)
        }
    };

    private static DataGridTextColumn Column(string title, string path, double width, bool muted = false)
    {
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
        text.Setters.Add(new Setter(MarginProperty, new Thickness(12, 0, 12, 0)));
        text.Setters.Add(new Setter(FontSizeProperty, 13.5));
        if (muted)
            text.Setters.Add(new Setter(ForegroundProperty, PodiumBrush("#B4B4BE", SystemColors.WindowTextBrush)));
        return new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = width, ElementStyle = text };
    }

    private static Border PodiumAvatar(Brush fill, Brush ink, double size = 44)
    {
        double scale = size / 44.0;
        var face = new Canvas
        {
            Width = 44,
            Height = 44,
            RenderTransformOrigin = new Point(0, 0),
            RenderTransform = new ScaleTransform(scale, scale)
        };
        face.Children.Add(new ShapePath
        {
            Data = Geometry.Parse(CommunityPodium.AvatarFace),
            Fill = fill,
            Stroke = ink,
            StrokeThickness = 2
        });
        face.Children.Add(new ShapePath
        {
            Data = Geometry.Parse(CommunityPodium.AvatarDetails),
            Stroke = ink,
            StrokeThickness = 2.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        });
        return new Border
        {
            Child = face,
            Width = size,
            Height = size,
            Background = PodiumBrush(CommunityPodium.AvatarFill, SystemColors.WindowBrush),
            CornerRadius = new CornerRadius(size / 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
    }

    private static Brush PodiumBrush(string color, Brush highContrast) =>
        SystemParameters.HighContrast ? highContrast : (Brush)new BrushConverter().ConvertFromInvariantString(color)!;

    private static TextBlock PodiumText(string text, double size, Brush ink) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = ink,
        FontWeight = FontWeights.SemiBold,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private static Border Surface(UIElement child, bool tinted) => new()
    {
        Child = child,
        Padding = new Thickness(16, 12, 16, 12),
        Margin = new Thickness(0, 0, 0, 8),
        CornerRadius = new CornerRadius(12),
        BorderThickness = new Thickness(1),
        Background = PodiumBrush(tinted ? CommunityPodium.StageFill : "#24242C", SystemColors.WindowBrush),
        BorderBrush = PodiumBrush(tinted ? CommunityPodium.StageOutline : "#383844", SystemColors.WindowTextBrush)
    };

    private static TextBlock Heading(string text, double size, double bottom = 4)
    {
        var heading = Line(text, size, bottom);
        heading.FontWeight = FontWeights.SemiBold;
        return heading;
    }

    private static TextBlock MutedLine(string text, double size = 13, double bottom = 8, bool semiBold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = semiBold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = PodiumBrush("#B4B4BE", SystemColors.WindowTextBrush),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, bottom)
    };

    private static TextBlock Line(string text, double size = 14, double bottom = 8) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, bottom)
    };

    private Button ActionButton(string text, bool primary = false, double bottomMargin = 8, double rightMargin = 8)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, rightMargin, bottomMargin)
        };
        if (!SystemParameters.HighContrast)
            ApplyStyle(button, primary ? "CommunityPrimaryButton" : "CommunityButton");
        else
            button.Padding = new Thickness(12, 6, 12, 6);
        return button;
    }

    private void ApplyStyle(FrameworkElement element, string resourceKey)
    {
        if (SystemParameters.HighContrast) return;
        if (TryFindResource(resourceKey) is Style style) element.Style = style;
    }

    private static Style HighContrastLinkStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(ForegroundProperty, SystemColors.HotTrackBrush));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(4, 2, 4, 2)));
        return style;
    }

    private static ResourceDictionary? _communityStyles;
    private static ResourceDictionary CommunityStyles => _communityStyles ??= (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="CommunityButton" TargetType="Button">
            <Setter Property="Background" Value="#2C2C33"/>
            <Setter Property="Foreground" Value="#F2F2F7"/>
            <Setter Property="BorderBrush" Value="#42424E"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="14,7"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8">
                    <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#383842"/>
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#5C5C6C"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#23232A"/>
                    </Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#8FC4FF"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Opacity" Value="0.45"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style x:Key="CommunityPrimaryButton" TargetType="Button" BasedOn="{StaticResource CommunityButton}">
            <Setter Property="Background" Value="#0A84FF"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderBrush" Value="#2D97FF"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Style.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="#2492FF"/>
                <Setter Property="BorderBrush" Value="#60B2FF"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter Property="Background" Value="#006EE6"/>
              </Trigger>
              <Trigger Property="IsEnabled" Value="False">
                <Setter Property="Opacity" Value="0.45"/>
              </Trigger>
            </Style.Triggers>
          </Style>

          <Style x:Key="CommunityBadgeTile" TargetType="Button" BasedOn="{StaticResource CommunityButton}">
            <Setter Property="Background" Value="#382E4C"/>
            <Setter Property="Foreground" Value="#F3E9FF"/>
            <Setter Property="BorderBrush" Value="#5C4B78"/>
            <Setter Property="Padding" Value="10,6"/>
            <Style.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="#46395F"/>
                <Setter Property="BorderBrush" Value="#7A659E"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter Property="Background" Value="#2F2540"/>
              </Trigger>
            </Style.Triggers>
          </Style>

          <Style x:Key="CommunityIconButton" TargetType="Button">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="#B4B4BE"/>
            <Setter Property="BorderBrush" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6">
                    <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#383844"/>
                      <Setter Property="Foreground" Value="#F2F2F7"/>
                    </Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#8FC4FF"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style x:Key="CommunityLinkButton" TargetType="Button">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="#E9DAFF"/>
            <Setter Property="BorderBrush" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="6,4"/>
            <Setter Property="FontSize" Value="13.5"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6">
                    <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#352B4B"/>
                      <Setter Property="Foreground" Value="#F7F0FF"/>
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#2A213C"/>
                    </Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#8FC4FF"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style x:Key="CommunityTextBox" TargetType="TextBox">
            <Setter Property="Background" Value="#23232A"/>
            <Setter Property="Foreground" Value="#F2F2F7"/>
            <Setter Property="CaretBrush" Value="#0A84FF"/>
            <Setter Property="BorderBrush" Value="#3A3A46"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="FontSize" Value="13.5"/>
            <Setter Property="MinHeight" Value="34"/>
            <Setter Property="VerticalContentAlignment" Value="Center"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="TextBox">
                  <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8">
                    <ScrollViewer x:Name="PART_ContentHost" Margin="0"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#545464"/>
                    </Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#0A84FF"/>
                      <Setter TargetName="Bd" Property="Background" Value="#1C1C23"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Opacity" Value="0.5"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style x:Key="CommunityComboBoxItem" TargetType="ComboBoxItem">
            <Setter Property="SnapsToDevicePixels" Value="True"/>
            <Setter Property="Foreground" Value="#E5E5EA"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Padding" Value="10,7"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ComboBoxItem">
                  <Border x:Name="ItemBorder" Background="{TemplateBinding Background}" CornerRadius="6" Margin="4,1" Padding="{TemplateBinding Padding}">
                    <ContentPresenter VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsHighlighted" Value="True">
                      <Setter TargetName="ItemBorder" Property="Background" Value="#0A84FF"/>
                      <Setter Property="Foreground" Value="White"/>
                    </Trigger>
                    <Trigger Property="IsSelected" Value="True">
                      <Setter TargetName="ItemBorder" Property="Background" Value="#260A84FF"/>
                      <Setter Property="FontWeight" Value="SemiBold"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <ControlTemplate x:Key="CommunityComboToggle" TargetType="ToggleButton">
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition/>
                <ColumnDefinition Width="28"/>
              </Grid.ColumnDefinitions>
              <Border x:Name="Border" Grid.ColumnSpan="2" CornerRadius="8" Background="#23232A" BorderBrush="#3A3A46" BorderThickness="1"/>
              <Path x:Name="Arrow" Grid.Column="1" Fill="#B4B4BE" HorizontalAlignment="Center" VerticalAlignment="Center"
                    Data="M 0 0 L 4.5 4.5 L 9 0 L 7.8 -1.1 L 4.5 2.2 L 1.2 -1.1 Z"/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="Border" Property="Background" Value="#2B2B34"/>
                <Setter TargetName="Border" Property="BorderBrush" Value="#545464"/>
                <Setter TargetName="Arrow" Property="Fill" Value="#F2F2F7"/>
              </Trigger>
              <Trigger Property="IsChecked" Value="True">
                <Setter TargetName="Border" Property="Background" Value="#1E1E25"/>
                <Setter TargetName="Border" Property="BorderBrush" Value="#0A84FF"/>
                <Setter TargetName="Arrow" Property="Fill" Value="#0A84FF"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>

          <Style x:Key="CommunityComboBox" TargetType="ComboBox">
            <Setter Property="SnapsToDevicePixels" Value="True"/>
            <Setter Property="OverridesDefaultStyle" Value="True"/>
            <Setter Property="Foreground" Value="#F2F2F7"/>
            <Setter Property="FontSize" Value="13.5"/>
            <Setter Property="MinHeight" Value="34"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="ItemContainerStyle" Value="{StaticResource CommunityComboBoxItem}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="ComboBox">
                  <Grid>
                    <ToggleButton Name="ToggleButton" Style="{x:Null}" OverridesDefaultStyle="True"
                                  Template="{StaticResource CommunityComboToggle}" Focusable="False"
                                  HorizontalAlignment="Stretch" VerticalAlignment="Stretch"
                                  IsChecked="{Binding Path=IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"
                                  ClickMode="Press"/>
                    <ContentPresenter Name="ContentSite" IsHitTestVisible="False"
                                      Content="{TemplateBinding SelectionBoxItem}"
                                      ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                      ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"
                                      Margin="11,6,30,6" VerticalAlignment="Center" HorizontalAlignment="Left"/>
                    <Popup Name="Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}"
                           AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
                      <Grid Name="DropDown" SnapsToDevicePixels="True" MinWidth="{TemplateBinding ActualWidth}" MaxHeight="240" Margin="0,4,0,8">
                        <Border Background="#F51C1C22" BorderThickness="1" BorderBrush="#3A3A46" CornerRadius="10" Padding="2,4">
                          <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Contained"/>
                        </Border>
                      </Grid>
                    </Popup>
                  </Grid>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                      <Setter Property="BorderBrush" Value="#8FC4FF"/>
                    </Trigger>
                    <Trigger Property="IsEnabled" Value="False">
                      <Setter Property="Opacity" Value="0.5"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style x:Key="CommunityTabControl" TargetType="TabControl">
            <Setter Property="OverridesDefaultStyle" Value="True"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="0"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="TabControl">
                  <DockPanel>
                    <Border DockPanel.Dock="Top" Background="#18181E" BorderBrush="#2E2E38" BorderThickness="0,0,0,1" Padding="24,4,24,0">
                      <StackPanel Orientation="Horizontal" IsItemsHost="True"/>
                    </Border>
                    <ContentPresenter ContentSource="SelectedContent"/>
                  </DockPanel>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style x:Key="CommunityTabItem" TargetType="TabItem">
            <Setter Property="OverridesDefaultStyle" Value="True"/>
            <Setter Property="Foreground" Value="#A8A8B3"/>
            <Setter Property="FontSize" Value="14"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Padding" Value="4,10"/>
            <Setter Property="Margin" Value="0,0,22,0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="TabItem">
                  <Border x:Name="Bd" Background="Transparent" BorderBrush="Transparent" BorderThickness="0,0,0,2" Padding="{TemplateBinding Padding}">
                    <ContentPresenter ContentSource="Header" RecognizesAccessKey="False" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter Property="Foreground" Value="#F2F2F7"/>
                    </Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True">
                      <Setter TargetName="Bd" Property="Background" Value="#18FFFFFF"/>
                    </Trigger>
                    <Trigger Property="IsSelected" Value="True">
                      <Setter Property="Foreground" Value="#FFFFFF"/>
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#0A84FF"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style x:Key="CommunityExpander" TargetType="Expander">
            <Setter Property="OverridesDefaultStyle" Value="True"/>
            <Setter Property="Foreground" Value="#F3E9FF"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Expander">
                  <Border Background="#231E30" BorderBrush="#4E4068" BorderThickness="1" CornerRadius="10" Padding="12,8">
                    <StackPanel>
                      <ToggleButton x:Name="HeaderSite" Style="{x:Null}" OverridesDefaultStyle="True" Cursor="Hand" FocusVisualStyle="{x:Null}"
                                    HorizontalAlignment="Stretch" HorizontalContentAlignment="Stretch"
                                    IsChecked="{Binding IsExpanded, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                        <ToggleButton.Template>
                          <ControlTemplate TargetType="ToggleButton">
                            <Border x:Name="HBd" Background="Transparent" CornerRadius="6" Padding="2,4">
                              <DockPanel>
                                <Path x:Name="Arrow" DockPanel.Dock="Right" Fill="#CEC2E6" VerticalAlignment="Center" Margin="8,0,2,0"
                                      Data="M 0 0 L 4.5 4.5 L 9 0 L 7.8 -1.1 L 4.5 2.2 L 1.2 -1.1 Z"/>
                                <ContentPresenter Content="{Binding Header, RelativeSource={RelativeSource AncestorType=Expander}}"
                                                  VerticalAlignment="Center" TextElement.FontWeight="SemiBold" TextElement.FontSize="13.5"/>
                              </DockPanel>
                            </Border>
                            <ControlTemplate.Triggers>
                              <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="HBd" Property="Background" Value="#14FFFFFF"/>
                              </Trigger>
                              <Trigger Property="IsKeyboardFocused" Value="True">
                                <Setter TargetName="HBd" Property="Background" Value="#20FFFFFF"/>
                              </Trigger>
                              <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="Arrow" Property="Data" Value="M 0 4.5 L 4.5 0 L 9 4.5 L 7.8 5.6 L 4.5 2.3 L 1.2 5.6 Z"/>
                              </Trigger>
                            </ControlTemplate.Triggers>
                          </ControlTemplate>
                        </ToggleButton.Template>
                      </ToggleButton>
                      <ContentPresenter x:Name="ExpandSite" Visibility="Collapsed"/>
                    </StackPanel>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsExpanded" Value="True">
                      <Setter TargetName="ExpandSite" Property="Visibility" Value="Visible"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style x:Key="CommunityDataGridHeader" TargetType="DataGridColumnHeader">
            <Setter Property="OverridesDefaultStyle" Value="True"/>
            <Setter Property="Background" Value="#272731"/>
            <Setter Property="Foreground" Value="#B4B4BE"/>
            <Setter Property="FontSize" Value="12.5"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Padding" Value="12,7,12,7"/>
            <Setter Property="BorderBrush" Value="#383844"/>
            <Setter Property="BorderThickness" Value="0,0,0,1"/>
            <Setter Property="HorizontalContentAlignment" Value="Left"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="DataGridColumnHeader">
                  <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                    <ContentPresenter VerticalAlignment="Center" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/>
                  </Border>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style x:Key="CommunityDataGridRow" TargetType="DataGridRow">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Style.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter Property="Background" Value="#292934"/>
              </Trigger>
              <Trigger Property="IsSelected" Value="True">
                <Setter Property="Background" Value="#2C253D"/>
              </Trigger>
            </Style.Triggers>
          </Style>

          <Style x:Key="CommunityDataGridCell" TargetType="DataGridCell">
            <Setter Property="IsTabStop" Value="False"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="#F2F2F7"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="DataGridCell">
                  <Border Background="{TemplateBinding Background}" Padding="0">
                    <ContentPresenter VerticalAlignment="Center"/>
                  </Border>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>

          <Style x:Key="CommunityDataGrid" TargetType="DataGrid">
            <Setter Property="KeyboardNavigation.TabNavigation" Value="Continue"/>
            <Setter Property="ColumnHeaderStyle" Value="{StaticResource CommunityDataGridHeader}"/>
            <Setter Property="RowStyle" Value="{StaticResource CommunityDataGridRow}"/>
            <Setter Property="CellStyle" Value="{StaticResource CommunityDataGridCell}"/>
          </Style>
        </ResourceDictionary>
        """);
}
