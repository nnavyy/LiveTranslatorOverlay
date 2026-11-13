using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using LiveTranslatorOverlay.Core;
using System.Runtime.InteropServices;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace LiveTranslatorMac;

internal sealed class CommunityWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CommunityClient _rankingsClient;
    private readonly CommunityClient _offersClient;

    public CommunityWindow(TimeSpan localUsage, bool showSupport)
        : this(localUsage, showSupport, client: null)
    {
    }

    internal CommunityWindow(TimeSpan localUsage, bool showSupport, CommunityClient? client)
    {
        _rankingsClient = client ?? new CommunityClient(AppSettings.DataDirectory);
        _offersClient = client ?? new CommunityClient(AppSettings.DataDirectory);

        Title = "Live Translator Community";
        RequestedThemeVariant = ThemeVariant.Dark;
        MinWidth = 480;
        MinHeight = 400;
        Width = 740;
        Height = 760;
        FontSize = 14;
        Background = SolidBrush("#1C1C22");
        Foreground = SolidBrush("#F2F2F7");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        AddWindowStyles();

        var tabs = new TabControl
        {
            Classes = { "community-tabs" },
            ItemsSource = new[]
            {
                new TabItem { Header = "Rankings", Classes = { "community-tab" }, Content = Rankings(localUsage) },
                new TabItem { Header = "Support development", Classes = { "community-tab" }, Content = Offers() }
            },
            SelectedIndex = showSupport ? 1 : 0
        };

        var footer = FooterBar();
        DockPanel.SetDock(footer, Dock.Bottom);
        var layout = new DockPanel { LastChildFill = true };
        layout.Children.Add(footer);
        layout.Children.Add(tabs);
        Content = layout;

        Opened += (_, _) => ClampToWorkingArea();
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }

    private void ClampToWorkingArea()
    {
        try
        {
            var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
            if (screen is not { } area || area.WorkingArea.Width <= 0 || area.WorkingArea.Height <= 0) return;
            double scale = area.Scaling > 0 ? area.Scaling : 1.0;
            double maxWidth = Math.Max(320, area.WorkingArea.Width / scale - 32);
            double maxHeight = Math.Max(320, area.WorkingArea.Height / scale - 32);
            MinWidth = Math.Min(480, maxWidth);
            MinHeight = Math.Min(400, maxHeight);
            Width = Math.Clamp(Width, MinWidth, maxWidth);
            Height = Math.Clamp(Height, MinHeight, maxHeight);
        }
        catch (Exception) { }
    }

    private Border FooterBar()
    {
        var terms = ActionButton("Terms and privacy");
        terms.Click += async (_, _) => await MacApplication.CreateTermsWindow().ShowDialog(this);
        var close = ActionButton("Close");
        close.MinWidth = 88;
        close.IsCancel = true;
        ToolTip.SetTip(close, "Close window (Esc)");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        var bar = new DockPanel { LastChildFill = false };
        bar.Children.Add(terms);
        bar.Children.Add(close);
        return new Border
        {
            Background = SolidBrush("#18181E"),
            BorderBrush = SolidBrush("#2E2E38"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 12),
            Child = bar
        };
    }

    private Control Rankings(TimeSpan usage)
    {
        var client = _rankingsClient;
        var panel = new StackPanel { Margin = new Thickness(24, 12, 24, 14) };

        var badge = PodiumAvatar(SolidBrush(CommunityPodium.AvatarFill), SolidBrush(CommunityPodium.AvatarInk), size: 44);
        badge.Margin = new Thickness(16, 0, 0, 0);
        badge.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(badge, Dock.Right);
        var subtitle = MutedLine($"Your audio translation time · {GuestAudioUsage.Format(usage)} · Private to this device", size: 13, bottom: 0);
        panel.Children.Add(new DockPanel
        {
            Margin = new Thickness(0, 0, 0, 12),
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
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Classes = { "community-combo" }
        };
        AutomationProperties.SetName(board, "Ranking category");

        var clearSearch = new Button
        {
            Content = "✕",
            IsVisible = false,
            Width = 24,
            Height = 24,
            MinWidth = 24,
            MinHeight = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Classes = { "community-icon" }
        };
        AutomationProperties.SetName(clearSearch, "Clear search");
        ToolTip.SetTip(clearSearch, "Clear search");

        var search = new TextBox
        {
            MaxLength = 24,
            MinWidth = 120,
            PlaceholderText = "Search nickname…",
            Padding = new Thickness(10, 6, 4, 6),
            VerticalAlignment = VerticalAlignment.Center,
            InnerRightContent = clearSearch,
            Classes = { "community-input" }
        };
        AutomationProperties.SetName(search, "Search by nickname");

        var refresh = ActionButton("Find", primary: true);
        refresh.MinWidth = 72;
        refresh.VerticalAlignment = VerticalAlignment.Center;

        var filters = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetColumn(board, 0);
        Grid.SetColumn(search, 1);
        Grid.SetColumn(refresh, 2);
        filters.Children.Add(board);
        filters.Children.Add(search);
        filters.Children.Add(refresh);
        panel.Children.Add(filters);

        var profile = new Popup
        {
            IsLightDismissEnabled = true,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6
        };
        void OpenProfile(CommunityRank entry, Button origin)
        {
            profile.IsOpen = false;
            profile.PlacementTarget = origin;
            profile.Child = new ThemeVariantScope
            {
                RequestedThemeVariant = ThemeVariant.Dark,
                Child = Profile(entry, () =>
                {
                    profile.IsOpen = false;
                    origin.Focus();
                })
            };
            profile.IsOpen = true;
        }
        profile.Closed += (_, _) => profile.Child = null;

        var podium = new ContentControl { IsVisible = false, Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(podium);

        var loadingBar = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 3,
            MinHeight = 3,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = SolidBrush("#0A84FF"),
            Background = SolidBrush("#32323E"),
            BorderThickness = new Thickness(0),
            IsVisible = false
        };
        var status = Line("", bottom: 0);
        status.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var statusAction = ActionButton("Retry");
        statusAction.Margin = new Thickness(12, 0, 0, 0);
        statusAction.VerticalAlignment = VerticalAlignment.Center;
        statusAction.IsVisible = false;
        DockPanel.SetDock(statusAction, Dock.Right);
        var statusRow = new DockPanel { LastChildFill = true };
        statusRow.Children.Add(statusAction);
        statusRow.Children.Add(status);
        var message = Surface(new StackPanel { Children = { loadingBar, statusRow } }, tinted: false);
        message.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(message);

        var scoreTitle = HeaderCell("Audio time");
        var headerRow = RankRow(HeaderCell("Rank"), HeaderCell("Nickname"), scoreTitle);
        var tableHeader = new Border
        {
            Background = SolidBrush("#272731"),
            BorderBrush = SolidBrush("#383844"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 7),
            Child = headerRow
        };

        var currentCategory = CommunityBoard.AllTime;
        var results = new ItemsControl();
        results.ItemTemplate = new FuncDataTemplate<CommunityRank>((row, _) =>
        {
            if (row == null) return null;
            var rankText = new TextBlock
            {
                Text = row.Rank.ToString(),
                FontSize = 13.5,
                Foreground = SolidBrush("#B4B4BE"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var scoreText = new TextBlock
            {
                Text = row.ScoreLabel(currentCategory),
                FontSize = 13.5,
                Foreground = SolidBrush("#F2F2F7"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var rowCells = RankRow(rankText, ProfileButton(row, OpenProfile, centered: false), scoreText);
            return new Border
            {
                MinHeight = 36,
                Padding = new Thickness(12, 2),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = SolidBrush("#2D2D38"),
                Classes = { "community-row" },
                Child = rowCells
            };
        });
        AutomationProperties.SetName(results, "Ranking results");

        var resultsScroll = new ScrollViewer
        {
            Content = results,
            MaxHeight = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var tableBorder = new Border
        {
            Background = SolidBrush("#202127"),
            BorderBrush = SolidBrush("#383844"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Child = new StackPanel { Children = { tableHeader, resultsScroll } }
        };

        var rangeLabel = MutedLine("", size: 12.5, bottom: 0);
        rangeLabel.VerticalAlignment = VerticalAlignment.Bottom;
        DockPanel.SetDock(rangeLabel, Dock.Right);
        var standingsHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        standingsHeader.Children.Add(rangeLabel);
        standingsHeader.Children.Add(Heading("Full rankings", 16, bottom: 0));

        var standings = new StackPanel
        {
            IsVisible = false,
            Margin = new Thickness(0, 0, 0, 10),
            Children = { standingsHeader, tableBorder }
        };
        panel.Children.Add(standings);

        var previous = ActionButton("Previous");
        var next = ActionButton("Next");
        var pageLabel = MutedLine("", size: 12.5, bottom: 0);
        pageLabel.VerticalAlignment = VerticalAlignment.Center;
        pageLabel.Margin = new Thickness(4, 0, 0, 0);
        var pages = new WrapPanel
        {
            IsVisible = false,
            ItemSpacing = 8,
            LineSpacing = 8,
            Margin = new Thickness(0, 2, 0, 0),
            Children = { previous, next, pageLabel }
        };
        panel.Children.Add(pages);

        int offset = 0;
        bool busy = false;
        Action? currentStatusAction = null;
        statusAction.Click += (_, _) => currentStatusAction?.Invoke();

        void UpdateSearchChrome()
        {
            clearSearch.IsVisible = !string.IsNullOrEmpty(search.Text);
        }
        search.TextChanged += (_, _) => UpdateSearchChrome();

        void SetMessageStyle(bool isError)
        {
            if (isError)
            {
                message.Background = SolidBrush("#361F25");
                message.BorderBrush = SolidBrush("#8A4550");
                status.Foreground = SolidBrush("#FFD6DC");
            }
            else
            {
                message.Background = SolidBrush("#24242C");
                message.BorderBrush = SolidBrush("#383844");
                status.Foreground = SolidBrush("#D8D8E2");
            }
        }

        async Task Load(int page)
        {
            if (busy || board.SelectedIndex < 0) return;
            var token = _lifetime.Token;
            busy = true;
            board.IsEnabled = search.IsEnabled = refresh.IsEnabled = clearSearch.IsEnabled = previous.IsEnabled = next.IsEnabled = false;
            var category = (CommunityBoard)board.SelectedIndex;
            currentCategory = category;
            string query = search.Text ?? "";
            scoreTitle.Text = category == CommunityBoard.Supporters ? "Contributed" : "Audio time";
            SetMessageStyle(isError: false);
            loadingBar.IsVisible = true;
            statusAction.IsVisible = false;
            currentStatusAction = null;
            status.Text = "Loading rankings…";
            message.IsVisible = true;
            profile.IsOpen = false;
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
                loadingBar.IsVisible = false;
                bool isError = !data.Available && data.Message == CommunityClient.Unavailable;
                SetMessageStyle(isError);
                status.Text = data.Message;
                if (isError)
                {
                    statusAction.Content = "Retry";
                    statusAction.IsVisible = true;
                    currentStatusAction = async () => await Load(page);
                }
                else if (data.Available && data.Entries.Length == 0 && !string.IsNullOrWhiteSpace(query))
                {
                    statusAction.Content = "Clear search";
                    statusAction.IsVisible = true;
                    currentStatusAction = async () => { search.Text = ""; search.Focus(); await Load(0); };
                }
                message.IsVisible = data.Message.Length > 0;
                var leaders = data.PodiumEntries(query, page);
                if (leaders.Count > 0) podium.Content = Podium(leaders, category, OpenProfile);
                podium.IsVisible = leaders.Count > 0;
                resultsScroll.MaxHeight = podium.IsVisible ? 156 : 360;
                results.ItemsSource = data.Entries;
                standings.IsVisible = data.Entries.Length > 0;
                rangeLabel.Text = data.Entries.Length > 0 ? $"Showing {offset + 1}–{offset + data.Entries.Length}" : "";
                previous.IsEnabled = data.Available && offset > 0;
                next.IsEnabled = data.Available && data.Entries.Length == CommunityClient.PageSize;
                int pageNumber = (offset / CommunityClient.PageSize) + 1;
                pageLabel.Text = $"Page {pageNumber}";
                pages.IsVisible = previous.IsEnabled || next.IsEnabled;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception)
            {
                if (token.IsCancellationRequested) return;
                loadingBar.IsVisible = false;
                SetMessageStyle(isError: true);
                status.Text = CommunityClient.Unavailable;
                statusAction.Content = "Retry";
                statusAction.IsVisible = true;
                currentStatusAction = async () => await Load(page);
                message.IsVisible = true;
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    loadingBar.IsVisible = false;
                    board.IsEnabled = search.IsEnabled = refresh.IsEnabled = clearSearch.IsEnabled = true;
                }
                busy = false;
            }
        }

        clearSearch.Click += async (_, _) => { search.Text = ""; search.Focus(); await Load(0); };
        refresh.Click += async (_, _) => await Load(0);
        search.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Load(0); };
        previous.Click += async (_, _) => await Load(Math.Max(0, offset - CommunityClient.PageSize));
        next.Click += async (_, _) => await Load(offset + CommunityClient.PageSize);
        board.SelectionChanged += async (_, _) => await Load(0);
        Opened += async (_, _) => await Load(0);

        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        return new Grid { Children = { scroll, profile } };
    }

    private static Border Podium(IReadOnlyList<CommunityRank> leaders, CommunityBoard category, Action<CommunityRank, Button> openProfile)
    {
        var row = new Grid
        {
            MaxWidth = leaders.Count * 192,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnSpacing = 12
        };
        var stageInk = SolidBrush(CommunityPodium.StageInk);
        var mutedStageInk = SolidBrush("#CEC2E6");
        var avatarInk = SolidBrush(CommunityPodium.AvatarInk);

        for (int index = 0; index < leaders.Count; index++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var leader = leaders[index];
            var (height, fill, ink) = CommunityPodium.Step(leader.Rank);
            var stepFill = SolidBrush(fill);
            var stepInk = SolidBrush(ink);
            var avatar = PodiumAvatar(stepInk, avatarInk, size: 40);
            avatar.Margin = new Thickness(0, 0, 0, 4);

            var profile = ProfileButton(leader, openProfile, centered: true);
            profile.Margin = new Thickness(0, 0, 0, 4);

            var step = new Border
            {
                Background = stepFill,
                CornerRadius = new CornerRadius(10, 10, 4, 4),
                MinHeight = height,
                Padding = new Thickness(8, 6),
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

        if (AnimatePodium())
        {
            row.Opacity = 0.75;
            row.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new CubicEaseOut() }
            };
            row.Loaded += (_, _) => row.Opacity = 1;
        }

        return new Border
        {
            Background = SolidBrush(CommunityPodium.StageFill),
            BorderBrush = SolidBrush(CommunityPodium.StageOutline),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 10),
            Child = new StackPanel { Children = { stageHeader, row } }
        };
    }

    private static bool AnimatePodium()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try { return NativeReduceMotion() == 0; }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    [DllImport("libLiveTranslatorAudio.dylib", EntryPoint = "lt_reduce_motion", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeReduceMotion();

    private static Button ProfileButton(CommunityRank entry, Action<CommunityRank, Button> openProfile, bool centered)
    {
        var name = new TextBlock
        {
            Text = entry.Nickname,
            FontSize = 13.5,
            FontWeight = centered ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = SolidBrush("#E9DAFF"),
            TextAlignment = centered ? TextAlignment.Center : TextAlignment.Left,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var button = new Button
        {
            Content = name,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = centered ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = centered ? new Thickness(0) : new Thickness(-6, 0, 0, 0),
            Padding = new Thickness(6, 4),
            MinHeight = 28,
            Classes = { "community-link" }
        };
        AutomationProperties.SetName(button, entry.Nickname);
        ToolTip.SetTip(button, $"View profile · {entry.Nickname}");
        button.Click += (_, _) => openProfile(entry, button);
        return button;
    }

    private static Border Profile(CommunityRank entry, Action closeProfile)
    {
        var close = ActionButton("Close");
        close.Padding = new Thickness(12, 5);
        close.MinHeight = 30;
        close.VerticalAlignment = VerticalAlignment.Top;
        AutomationProperties.SetName(close, "Close profile");
        ToolTip.SetTip(close, "Close profile (Esc)");
        close.Click += (_, _) => closeProfile();

        var avatar = PodiumAvatar(SolidBrush(CommunityPodium.AvatarFill), SolidBrush(CommunityPodium.AvatarInk), size: 40);

        var nameText = new TextBlock
        {
            Text = entry.Nickname,
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            Foreground = SolidBrush(CommunityPodium.StageInk),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        ToolTip.SetTip(nameText, entry.Nickname);

        var rankLine = new TextBlock
        {
            Text = $"Rank #{entry.Rank}",
            FontSize = 12.5,
            Foreground = SolidBrush("#CEC2E6")
        };

        var nameStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children = { nameText, rankLine }
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetColumn(avatar, 0);
        Grid.SetColumn(nameStack, 1);
        Grid.SetColumn(close, 2);
        header.Children.Add(avatar);
        header.Children.Add(nameStack);
        header.Children.Add(close);

        var divider = new Border
        {
            Height = 1,
            Background = SolidBrush("#463A5C"),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var contributionsLabel = new TextBlock
        {
            Text = "Verified contributions",
            FontSize = 12,
            Foreground = SolidBrush("#CEC2E6"),
            Margin = new Thickness(0, 0, 0, 2)
        };
        var contributionsValue = Heading(entry.SupportLabel, 18, bottom: 12);
        contributionsValue.Foreground = SolidBrush(CommunityPodium.StageInk);

        var badgesLabel = new TextBlock
        {
            Text = "Badges",
            FontSize = 12,
            Foreground = SolidBrush("#CEC2E6"),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var badges = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        var detail = new TextBlock
        {
            Text = "",
            FontSize = 12.5,
            Foreground = SolidBrush("#CEC2E6"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            IsVisible = false
        };

        foreach (var badge in CommunityBadge.For(entry))
        {
            var icon = BadgeIcon(badge, 20);
            var label = new TextBlock
            {
                Text = badge.Name,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { icon, label }
            };
            var tile = new Button
            {
                Content = content,
                MinHeight = 32,
                Padding = new Thickness(10, 6),
                Classes = { "community-badge" }
            };
            AutomationProperties.SetName(tile, badge.Name);
            ToolTip.SetTip(tile, badge.Name);
            tile.Click += (_, _) =>
            {
                detail.Text = $"{badge.Name} medal — voluntary lifetime support";
                detail.IsVisible = true;
            };
            badges.Children.Add(tile);
        }
        if (badges.Children.Count == 0)
        {
            badges.Children.Add(new TextBlock
            {
                Text = "No public badges",
                FontSize = 13,
                Foreground = SolidBrush("#B8B0CA")
            });
        }

        var card = new Border
        {
            Width = 344,
            MaxWidth = 344,
            Background = SolidBrush(CommunityPodium.StageFill),
            BorderBrush = SolidBrush("#5E4E75"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 18,
                OffsetY = 4,
                Color = Color.FromArgb(122, 0, 0, 0)
            }),
            Child = new StackPanel
            {
                Children = { header, divider, contributionsLabel, contributionsValue, badgesLabel, badges, detail }
            }
        };
        card.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            closeProfile();
            args.Handled = true;
        };
        card.Loaded += (_, _) => close.Focus();
        return card;
    }

    private Control Offers()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };

        var title = Heading("Support development", 24, bottom: 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        var licensePill = PillBadge("MIT Open Source · Free forever", "#1D3329", "#3A6B52", "#B8F2D0");
        licensePill.VerticalAlignment = VerticalAlignment.Center;
        var titleWrap = new WrapPanel
        {
            ItemSpacing = 12,
            LineSpacing = 4,
            Margin = new Thickness(0, 0, 0, 8),
            Children = { title, licensePill }
        };
        panel.Children.Add(titleWrap);

        var intro = Line(SupportCatalog.Intro, size: 13.5, bottom: 16);
        intro.Foreground = SolidBrush("#D4D4DE");
        panel.Children.Add(intro);

        var lifetimeGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 0, 0, 8)
        };
        int col = 0;
        foreach (var offer in SupportCatalog.Offers)
        {
            if (offer.Cents.HasValue)
            {
                var card = LifetimeOfferCard(offer);
                Grid.SetColumn(card, col++);
                lifetimeGrid.Children.Add(card);
            }
            else
            {
                panel.Children.Add(lifetimeGrid);
                panel.Children.Add(MutedLine(SupportCatalog.IndependenceNote, size: 12.5, bottom: 12));
                panel.Children.Add(DonationCard(offer));
            }
        }

        var purchaseHeader = new WrapPanel
        {
            ItemSpacing = 10,
            LineSpacing = 4,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var purchaseHeading = Heading("Purchase options", 15.5, bottom: 0);
        purchaseHeading.VerticalAlignment = VerticalAlignment.Center;
        var previewPill = PillBadge("Preview only", "#3B3052", "#68558A", "#E9DAFF");
        previewPill.VerticalAlignment = VerticalAlignment.Center;
        purchaseHeader.Children.Add(purchaseHeading);
        purchaseHeader.Children.Add(previewPill);

        var closedLine = Line(SupportCatalog.Closed, size: 13.5, bottom: 4);
        closedLine.FontWeight = FontWeight.SemiBold;
        var availabilityLine = MutedLine(SupportCatalog.AvailabilityNote, size: 13, bottom: 0);
        availabilityLine.Foreground = SolidBrush("#CEC2E6");

        var purchase = new StackPanel { Children = { purchaseHeader, closedLine, availabilityLine } };
        var client = _offersClient;
        if (client.ManualRequestPreview)
        {
            purchase.Children.Add(new Expander
            {
                Header = "Developer-assisted request (staging preview)",
                Margin = new Thickness(0, 12, 0, 0),
                Classes = { "community-expander" },
                Content = ManualRequestForm(client)
            });
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
        var titleRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 6)
        };
        if (badge != null)
        {
            var icon = BadgeIcon(badge, 20);
            icon.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(icon, 0);
            titleRow.Children.Add(icon);
        }
        var title = Heading(offer.Title, 15.5, bottom: 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 1);
        titleRow.Children.Add(title);

        var price = Heading(offer.Price, 22, bottom: 2);
        if (badge != null) price.Foreground = SolidBrush(badge.Color);
        var subtitle = MutedLine("Voluntary · One-time", size: 12, bottom: 8);
        var description = Line(offer.Description, size: 13, bottom: 0);
        description.Foreground = SolidBrush("#D4D4DE");

        var card = Surface(new StackPanel { Children = { titleRow, price, subtitle, description } }, tinted: false);
        card.Margin = new Thickness(0);
        return card;
    }

    private static Border DonationCard(SupportOffer offer)
    {
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Margin = new Thickness(0, 0, 0, 6)
        };
        var title = Heading(offer.Title, 15.5, bottom: 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        var price = Heading(offer.Price, 14.5, bottom: 0);
        price.Foreground = SolidBrush("#E9DAFF");
        price.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0);
        Grid.SetColumn(price, 1);
        header.Children.Add(title);
        header.Children.Add(price);

        var description = Line(offer.Description, size: 13, bottom: 0);
        description.Foreground = SolidBrush("#D4D4DE");
        var card = Surface(new StackPanel { Children = { header, description } }, tinted: false);
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    private Control ManualRequestForm(CommunityClient client)
    {
        var lifetimeOffers = SupportCatalog.Offers.Where(item => item.Cents.HasValue).ToArray();
        var offer = new ComboBox
        {
            ItemsSource = lifetimeOffers,
            ItemTemplate = new FuncDataTemplate<SupportOffer>((item, _) => new TextBlock
            {
                Text = item?.Label ?? "",
                FontSize = 13.5,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            }),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 10),
            Classes = { "community-combo" }
        };
        var email = new TextBox
        {
            MaxLength = 254,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 0, 0, 8),
            Classes = { "community-input" }
        };
        var code = new TextBox
        {
            MaxLength = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 0, 0, 8),
            Classes = { "community-input" }
        };
        AutomationProperties.SetName(offer, "Lifetime offer");
        AutomationProperties.SetName(email, "Staging account email");
        AutomationProperties.SetName(code, "Six-digit email code");

        var send = ActionButton("Send code");
        send.HorizontalAlignment = HorizontalAlignment.Left;
        send.Margin = new Thickness(0, 0, 0, 10);
        var submit = ActionButton("Submit request", primary: true);
        submit.HorizontalAlignment = HorizontalAlignment.Left;
        submit.Margin = new Thickness(0, 0, 0, 8);

        var status = MutedLine("", size: 13, bottom: 0);
        status.Foreground = SolidBrush("#E5E5F0");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        async Task Run(Func<CancellationToken, Task<string>> action)
        {
            var token = _lifetime.Token;
            offer.IsEnabled = email.IsEnabled = code.IsEnabled = send.IsEnabled = submit.IsEnabled = false;
            status.Text = "Working…";
            try { status.Text = await action(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { if (!token.IsCancellationRequested) status.Text = "Could not complete the request. Please try again."; }
            finally
            {
                if (!token.IsCancellationRequested)
                    offer.IsEnabled = email.IsEnabled = code.IsEnabled = send.IsEnabled = submit.IsEnabled = true;
            }
        }
        send.Click += async (_, _) => await Run(token => client.SendPurchaseCodeAsync(email.Text?.Trim() ?? "", token));
        submit.Click += async (_, _) =>
        {
            if (offer.SelectedItem is not SupportOffer selected) return;
            string digits = code.Text?.Trim() ?? "";
            code.Text = "";
            await Run(token => client.RequestManualPurchaseAsync(email.Text?.Trim() ?? "", digits, selected.Id, token));
        };

        var notice = MutedLine("Staging preview only — no payment is charged. Your email is sent to the sign-in service to verify your request.", size: 12.5, bottom: 10);
        notice.Foreground = SolidBrush("#CEC2E6");

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
                send,
                MutedLine("Six-digit email code", size: 12, bottom: 4, semiBold: true),
                code,
                submit,
                status
            }
        };
    }

    private static Viewbox BadgeIcon(CommunityBadge badge, double size)
    {
        var canvas = new Canvas { Width = 28, Height = 28 };
        canvas.Children.Add(new ShapePath { Data = Geometry.Parse(badge.Body), Fill = SolidBrush(badge.Color) });
        canvas.Children.Add(new ShapePath { Data = Geometry.Parse(badge.Detail), Fill = SolidBrush("#302241") });
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    private static Border PillBadge(string text, string fill, string border, string ink) => new()
    {
        Background = SolidBrush(fill),
        BorderBrush = SolidBrush(border),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(8, 3),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = SolidBrush(ink)
        }
    };

    private static Border PodiumAvatar(IBrush fill, IBrush ink, double size)
    {
        var face = new Canvas { Width = 44, Height = 44 };
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
            StrokeLineCap = PenLineCap.Round
        });
        return new Border
        {
            Child = new Viewbox { Width = size, Height = size, Child = face },
            Width = size,
            Height = size,
            Background = SolidBrush(CommunityPodium.AvatarFill),
            CornerRadius = new CornerRadius(size / 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
    }

    private static IBrush SolidBrush(string color) => new SolidColorBrush(Color.Parse(color));

    private static TextBlock PodiumText(string text, double size, IBrush ink) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = ink,
        FontWeight = FontWeight.SemiBold,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
        TextTrimming = TextTrimming.CharacterEllipsis
    };

    private static Border Surface(Control child, bool tinted) => new()
    {
        Child = child,
        Padding = new Thickness(16, 12),
        Margin = new Thickness(0, 0, 0, 8),
        CornerRadius = new CornerRadius(12),
        BorderThickness = new Thickness(1),
        Background = SolidBrush(tinted ? CommunityPodium.StageFill : "#24242C"),
        BorderBrush = SolidBrush(tinted ? CommunityPodium.StageOutline : "#383844")
    };

    private static Grid RankRow(params Control[] cells)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("64,*,124"), ColumnSpacing = 8 };
        for (int index = 0; index < cells.Length; index++)
        {
            Grid.SetColumn(cells[index], index);
            cells[index].VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(cells[index]);
        }
        return row;
    }

    private static TextBlock HeaderCell(string text) => new()
    {
        Text = text,
        FontSize = 12.5,
        FontWeight = FontWeight.SemiBold,
        Foreground = SolidBrush("#B4B4BE")
    };

    private static TextBlock Heading(string text, double size, double bottom = 4) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeight.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, bottom)
    };

    private static TextBlock MutedLine(string text, double size = 13, double bottom = 8, bool semiBold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = semiBold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = SolidBrush("#B4B4BE"),
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

    private static Button ActionButton(string text, bool primary = false) => new()
    {
        Content = text,
        Classes = { primary ? "community-primary" : "community-btn" }
    };

    private void AddWindowStyles()
    {
        Styles.Add(new Style(x => x.OfType<TabControl>().Class("community-tabs"))
        {
            Setters =
            {
                new Setter(TemplateProperty, new FuncControlTemplate<TabControl>((control, _) =>
                {
                    var strip = new ItemsPresenter
                    {
                        Name = "PART_ItemsPresenter",
                        ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal })
                    };
                    var header = new Border
                    {
                        Background = SolidBrush("#18181E"),
                        BorderBrush = SolidBrush("#2E2E38"),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(24, 4, 24, 0),
                        Child = strip
                    };
                    DockPanel.SetDock(header, Dock.Top);
                    var presenter = new ContentPresenter
                    {
                        Name = "PART_SelectedContentHost",
                        [!ContentPresenter.ContentProperty] = control[!TabControl.SelectedContentProperty],
                        [!ContentPresenter.ContentTemplateProperty] = control[!TabControl.SelectedContentTemplateProperty]
                    };
                    return new DockPanel { Children = { header, presenter } };
                }))
            }
        });

        Styles.Add(new Style(x => x.OfType<TabItem>().Class("community-tab"))
        {
            Setters =
            {
                new Setter(ForegroundProperty, SolidBrush("#A8A8B3")),
                new Setter(BackgroundProperty, Brushes.Transparent),
                new Setter(BorderBrushProperty, Brushes.Transparent),
                new Setter(BorderThicknessProperty, new Thickness(0, 0, 0, 2)),
                new Setter(FontSizeProperty, 14.0),
                new Setter(FontWeightProperty, FontWeight.SemiBold),
                new Setter(PaddingProperty, new Thickness(4, 10)),
                new Setter(MarginProperty, new Thickness(0, 0, 22, 0)),
                new Setter(MinHeightProperty, 38.0),
                new Setter(CursorProperty, new Cursor(StandardCursorType.Hand)),
                new Setter(TemplateProperty, new FuncControlTemplate<TabItem>((item, _) => new Border
                {
                    [!Border.BackgroundProperty] = item[!BackgroundProperty],
                    [!Border.BorderBrushProperty] = item[!BorderBrushProperty],
                    [!Border.BorderThicknessProperty] = item[!BorderThicknessProperty],
                    [!Decorator.PaddingProperty] = item[!PaddingProperty],
                    Child = new ContentPresenter
                    {
                        Name = "PART_ContentPresenter",
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        [!ContentPresenter.ContentProperty] = item[!HeaderedContentControl.HeaderProperty],
                        [!ContentPresenter.ContentTemplateProperty] = item[!HeaderedContentControl.HeaderTemplateProperty]
                    }
                }))
            }
        });
        Styles.Add(new Style(x => x.OfType<TabItem>().Class("community-tab").Class(":pointerover"))
        {
            Setters = { new Setter(ForegroundProperty, SolidBrush("#F2F2F7")) }
        });
        Styles.Add(new Style(x => x.OfType<TabItem>().Class("community-tab").Class(":focus-visible"))
        {
            Setters = { new Setter(BackgroundProperty, SolidBrush("#18FFFFFF")) }
        });
        Styles.Add(new Style(x => x.OfType<TabItem>().Class("community-tab").Class(":selected"))
        {
            Setters =
            {
                new Setter(ForegroundProperty, Brushes.White),
                new Setter(BorderBrushProperty, SolidBrush("#0A84FF"))
            }
        });

        AddButtonStyle("community-btn", "#2C2C33", "#F2F2F7", "#42424E", "#383842", "#5C5C6C", "#23232A", 8, new Thickness(14, 7), 13, FontWeight.Normal);
        AddButtonStyle("community-primary", "#0A84FF", "#FFFFFF", "#2D97FF", "#2492FF", "#60B2FF", "#006EE6", 8, new Thickness(14, 7), 13, FontWeight.SemiBold);
        AddButtonStyle("community-badge", "#382E4C", "#F3E9FF", "#5C4B78", "#46395F", "#7A659E", "#2F2540", 8, new Thickness(10, 6), 13, FontWeight.SemiBold);
        AddButtonStyle("community-link", "Transparent", "#E9DAFF", "Transparent", "#352B4B", "#352B4B", "#2A213C", 6, new Thickness(6, 4), 13.5, FontWeight.Normal, hoverFg: "#F7F0FF");
        AddButtonStyle("community-icon", "Transparent", "#B4B4BE", "Transparent", "#383844", "#383844", "#2B2B34", 6, new Thickness(0), 12, FontWeight.Normal, hoverFg: "#F2F2F7");

        Styles.Add(new Style(x => x.OfType<Border>().Class("community-row"))
        {
            Setters = { new Setter(Border.BackgroundProperty, Brushes.Transparent) }
        });
        Styles.Add(new Style(x => x.OfType<Border>().Class("community-row").Class(":pointerover"))
        {
            Setters = { new Setter(Border.BackgroundProperty, SolidBrush("#292934")) }
        });

        Styles.Add(new Style(x => x.OfType<TextBox>().Class("community-input"))
        {
            Setters =
            {
                new Setter(BackgroundProperty, SolidBrush("#23232A")),
                new Setter(ForegroundProperty, SolidBrush("#F2F2F7")),
                new Setter(BorderBrushProperty, SolidBrush("#3A3A46")),
                new Setter(BorderThicknessProperty, new Thickness(1)),
                new Setter(CornerRadiusProperty, new CornerRadius(8)),
                new Setter(FontSizeProperty, 13.5),
                new Setter(MinHeightProperty, 34.0),
                new Setter(TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Center)
            }
        });
        Styles.Add(new Style(x => x.OfType<TextBox>().Class("community-input").Class(":pointerover"))
        {
            Setters = { new Setter(BorderBrushProperty, SolidBrush("#545464")) }
        });
        Styles.Add(new Style(x => x.OfType<TextBox>().Class("community-input").Class(":focus"))
        {
            Setters =
            {
                new Setter(BorderBrushProperty, SolidBrush("#0A84FF")),
                new Setter(BackgroundProperty, SolidBrush("#1C1C23"))
            }
        });

        Styles.Add(new Style(x => x.OfType<ComboBox>().Class("community-combo"))
        {
            Setters =
            {
                new Setter(BackgroundProperty, SolidBrush("#23232A")),
                new Setter(ForegroundProperty, SolidBrush("#F2F2F7")),
                new Setter(BorderBrushProperty, SolidBrush("#3A3A46")),
                new Setter(BorderThicknessProperty, new Thickness(1)),
                new Setter(CornerRadiusProperty, new CornerRadius(8)),
                new Setter(FontSizeProperty, 13.5),
                new Setter(MinHeightProperty, 34.0),
                new Setter(PaddingProperty, new Thickness(11, 6, 8, 6))
            }
        });

        Styles.Add(new Style(x => x.OfType<Expander>().Class("community-expander"))
        {
            Setters =
            {
                new Setter(BackgroundProperty, SolidBrush("#231E30")),
                new Setter(ForegroundProperty, SolidBrush("#F3E9FF")),
                new Setter(BorderBrushProperty, SolidBrush("#4E4068")),
                new Setter(BorderThicknessProperty, new Thickness(1)),
                new Setter(CornerRadiusProperty, new CornerRadius(10))
            }
        });
    }

    private void AddButtonStyle(
        string className, string bg, string fg, string border,
        string hoverBg, string hoverBorder, string pressBg,
        double radius, Thickness padding, double fontSize, FontWeight weight, string? hoverFg = null)
    {
        var template = new FuncControlTemplate<Button>((btn, _) => new Border
        {
            CornerRadius = new CornerRadius(radius),
            [!Border.BackgroundProperty] = btn[!BackgroundProperty],
            [!Border.BorderBrushProperty] = btn[!BorderBrushProperty],
            [!Border.BorderThicknessProperty] = btn[!BorderThicknessProperty],
            [!Decorator.PaddingProperty] = btn[!PaddingProperty],
            Child = new ContentPresenter
            {
                [!ContentPresenter.ContentProperty] = btn[!ContentControl.ContentProperty],
                [!ContentPresenter.ContentTemplateProperty] = btn[!ContentControl.ContentTemplateProperty],
                [!ContentPresenter.HorizontalAlignmentProperty] = btn[!HorizontalContentAlignmentProperty],
                VerticalAlignment = VerticalAlignment.Center
            }
        });

        Styles.Add(new Style(x => x.OfType<Button>().Class(className))
        {
            Setters =
            {
                new Setter(BackgroundProperty, SolidBrush(bg)),
                new Setter(ForegroundProperty, SolidBrush(fg)),
                new Setter(BorderBrushProperty, SolidBrush(border)),
                new Setter(BorderThicknessProperty, new Thickness(1)),
                new Setter(PaddingProperty, padding),
                new Setter(FontSizeProperty, fontSize),
                new Setter(FontWeightProperty, weight),
                new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(MinWidthProperty, 0.0),
                new Setter(MinHeightProperty, 34.0),
                new Setter(CursorProperty, new Cursor(StandardCursorType.Hand)),
                new Setter(TemplateProperty, template)
            }
        });

        var hover = new Style(x => x.OfType<Button>().Class(className).Class(":pointerover"))
        {
            Setters =
            {
                new Setter(BackgroundProperty, SolidBrush(hoverBg)),
                new Setter(BorderBrushProperty, SolidBrush(hoverBorder))
            }
        };
        if (hoverFg != null) hover.Setters.Add(new Setter(ForegroundProperty, SolidBrush(hoverFg)));
        Styles.Add(hover);

        Styles.Add(new Style(x => x.OfType<Button>().Class(className).Class(":pressed"))
        {
            Setters = { new Setter(BackgroundProperty, SolidBrush(pressBg)) }
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class(className).Class(":focus-visible"))
        {
            Setters = { new Setter(BorderBrushProperty, SolidBrush("#8FC4FF")) }
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class(className).Class(":disabled"))
        {
            Setters = { new Setter(OpacityProperty, 0.45) }
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class(className).Class(":disabled").Template().OfType<ContentPresenter>())
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, Brushes.Transparent),
                new Setter(ContentPresenter.ForegroundProperty, SolidBrush(fg))
            }
        });
    }
}
