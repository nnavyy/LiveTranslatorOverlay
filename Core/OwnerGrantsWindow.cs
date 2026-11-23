using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace LiveTranslatorOverlay.Core;

/// <summary>
/// Developer-only window for inspecting user payment/support requests, guest donations,
/// and granting or revoking Lifetime Supporter / Beta Insider access 1-by-1.
/// </summary>
public sealed class OwnerGrantsWindow : Window
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly CancellationTokenSource _lifetime = new();

    private readonly TextBox _urlBox;
    private readonly PasswordBox _serviceKeyBox;
    private readonly ComboBox _statusFilter;
    private readonly DataGrid _accountGrid;
    private readonly DataGrid _guestGrid;
    private readonly DataGrid _insidersGrid;
    private readonly TextBlock _kpiPending;
    private readonly TextBlock _kpiInsiders;
    private readonly TextBlock _kpiGuests;
    private readonly TextBlock _statusText;
    private readonly TextBox _adminNoteBox;

    private AdminListResponse _currentData = new([], [], []);

    public OwnerGrantsWindow(Window owner)
    {
        Owner = owner;
        Title = "Owner Dashboard — Payments & Beta Grants";
        Icon = AppBranding.LoadWindowIcon();
        Width = 860;
        Height = 680;
        MinWidth = 640;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13.5;

        Background = Solid("#121217");
        Foreground = Solid("#F2F2F7");
        if (!SystemParameters.HighContrast)
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.Dark.xaml")
            });
        }

        _urlBox = new TextBox
        {
            Padding = new Thickness(10, 6, 10, 6),
            Background = Solid("#1A1A22"),
            Foreground = Solid("#F2F2F7"),
            BorderBrush = Solid("#2E2E3A")
        };
        _serviceKeyBox = new PasswordBox
        {
            Padding = new Thickness(10, 6, 10, 6),
            Background = Solid("#1A1A22"),
            Foreground = Solid("#F2F2F7"),
            BorderBrush = Solid("#2E2E3A")
        };

        var existingCfg = OwnerAdminConfigStore.Load(AppSettings.DataDirectory);
        if (existingCfg != null)
        {
            _urlBox.Text = existingCfg.SupabaseUrl;
            _serviceKeyBox.Password = existingCfg.ServiceRoleKey;
        }
        else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SUPABASE_URL")))
        {
            _urlBox.Text = Environment.GetEnvironmentVariable("SUPABASE_URL") ?? "";
            _serviceKeyBox.Password = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY") ?? "";
        }

        _statusFilter = new ComboBox
        {
            ItemsSource = new[] { "pending", "all", "verified", "cancelled" },
            SelectedIndex = 0,
            MinWidth = 130,
            VerticalAlignment = VerticalAlignment.Center
        };
        _adminNoteBox = new TextBox
        {
            MaxLength = 200,
            Padding = new Thickness(10, 6, 10, 6),
            Background = Solid("#1A1A22"),
            Foreground = Solid("#F2F2F7"),
            BorderBrush = Solid("#2E2E3A"),
            MinWidth = 240,
            ToolTip = "Optional note recorded in admin_grant_audit"
        };

        _kpiPending = KpiValueText("0");
        _kpiInsiders = KpiValueText("0");
        _kpiGuests = KpiValueText("0");
        _statusText = new TextBlock
        {
            Text = "Ready. Enter Supabase URL + Service Role Key to manage live grants, or inspect local staging below.",
            Foreground = Solid("#B8F2D0"),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        _accountGrid = CreateGrid();
        _accountGrid.Columns.Add(TextCol("Email", nameof(AccountReqRow.Email), 200));
        _accountGrid.Columns.Add(TextCol("Offer", nameof(AccountReqRow.Offer), 92));
        _accountGrid.Columns.Add(TextCol("Amount", nameof(AccountReqRow.AmountLabel), 96));
        _accountGrid.Columns.Add(TextCol("Status", nameof(AccountReqRow.Status), 88));
        _accountGrid.Columns.Add(TextCol("Date", nameof(AccountReqRow.DateLabel), 108));
        _accountGrid.Columns.Add(TextCol("Note", nameof(AccountReqRow.Note), 200));

        _guestGrid = CreateGrid();
        _guestGrid.Columns.Add(TextCol("Guest Handle", nameof(GuestReqRow.GuestTag), 130));
        _guestGrid.Columns.Add(TextCol("Amount", nameof(GuestReqRow.AmountLabel), 100));
        _guestGrid.Columns.Add(TextCol("Status", nameof(GuestReqRow.Status), 92));
        _guestGrid.Columns.Add(TextCol("Date", nameof(GuestReqRow.DateLabel), 112));
        _guestGrid.Columns.Add(TextCol("Device Hash", nameof(GuestReqRow.ShortHash), 140));
        _guestGrid.Columns.Add(TextCol("Note", nameof(GuestReqRow.Note), 180));

        _insidersGrid = CreateGrid();
        _insidersGrid.Columns.Add(TextCol("Email", nameof(InsiderRow.Email), 240));
        _insidersGrid.Columns.Add(TextCol("Verified Total", nameof(InsiderRow.TotalLabel), 120));
        _insidersGrid.Columns.Add(TextCol("Beta Active", nameof(InsiderRow.BetaLabel), 98));
        _insidersGrid.Columns.Add(TextCol("Granted", nameof(InsiderRow.GrantedLabel), 112));
        _insidersGrid.Columns.Add(TextCol("Note", nameof(InsiderRow.Note), 200));

        var saveConnBtn = MakeButton("Save & Load", primary: true);
        var clearConnBtn = MakeButton("Clear Key");
        var refreshBtn = MakeButton("Refresh");
        var openRankBtn = MakeButton("Open Rankings");
        var closeRankBtn = MakeButton("Close Rankings");

        var approveBtn = MakeButton("Approve & Grant", primary: true);
        var approveBetaBtn = MakeButton("Approve + Unlock Beta");
        var revokeBtn = MakeButton("Revoke");
        var verifyGuestBtn = MakeButton("Verify Guest to Supporters", primary: true);
        var revokeBetaBtn = MakeButton("Deactivate Beta");
        var closeBtn = MakeButton("Close");
        closeBtn.IsCancel = true;

        saveConnBtn.Click += async (_, _) => await SaveConnectionAndReloadAsync();
        clearConnBtn.Click += (_, _) =>
        {
            OwnerAdminConfigStore.Clear(AppSettings.DataDirectory);
            _serviceKeyBox.Password = "";
            LoadOfflineStagingPreview();
            _statusText.Text = "Cleared encrypted admin key. Showing local staging ledger.";
        };
        refreshBtn.Click += async (_, _) => await RefreshAsync();
        _statusFilter.SelectionChanged += async (_, _) => { if (IsLoaded) await RefreshAsync(); };
        openRankBtn.Click += async (_, _) => await ToggleRankingsAsync(true);
        closeRankBtn.Click += async (_, _) => await ToggleRankingsAsync(false);
        approveBtn.Click += async (_, _) => await GrantSelectedAccountAsync(forceBeta: null);
        approveBetaBtn.Click += async (_, _) => await GrantSelectedAccountAsync(forceBeta: true);
        revokeBtn.Click += async (_, _) => await RevokeSelectedAccountAsync();
        verifyGuestBtn.Click += async (_, _) => await VerifySelectedGuestAsync();
        revokeBetaBtn.Click += async (_, _) => await RevokeSelectedInsiderBetaAsync();
        closeBtn.Click += (_, _) => Close();

        var rootPanel = new StackPanel { Margin = new Thickness(24, 16, 24, 16) };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        if (AppBranding.CreateLogoControl(28) is { } logo)
        {
            logo.Margin = new Thickness(0, 0, 12, 0);
            header.Children.Add(logo);
        }
        header.Children.Add(new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Payments & Beta Grants", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Solid("#FAFAFA") },
                new TextBlock { Text = "Review user support requests, verify payments 1-by-1, and unlock private Beta access.", FontSize = 12.5, Foreground = Solid("#A1A1AA") }
            }
        });
        rootPanel.Children.Add(header);

        var connGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        connGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        connGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        connGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var urlStack = new StackPanel { Children = { Label("Supabase URL (e.g. https://xxxx.supabase.co)"), _urlBox } };
        var keyStack = new StackPanel { Children = { Label("Service Role Key (stored locally via DPAPI)"), _serviceKeyBox } };
        Grid.SetColumn(urlStack, 0);
        Grid.SetColumn(keyStack, 2);
        connGrid.Children.Add(urlStack);
        connGrid.Children.Add(keyStack);

        var connActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        saveConnBtn.Margin = new Thickness(0, 0, 8, 0);
        clearConnBtn.Margin = new Thickness(0, 0, 8, 0);
        openRankBtn.Margin = new Thickness(0, 0, 8, 0);
        connActions.Children.Add(saveConnBtn);
        connActions.Children.Add(clearConnBtn);
        connActions.Children.Add(openRankBtn);
        connActions.Children.Add(closeRankBtn);
        rootPanel.Children.Add(Card(new StackPanel { Children = { connGrid, connActions } }));

        var kpiGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < 5; i++)
            kpiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(12) });
        var k1 = KpiTile("Account Requests Loaded", _kpiPending);
        var k2 = KpiTile("Verified Beta Insiders", _kpiInsiders);
        var k3 = KpiTile("Guest Donations Loaded", _kpiGuests);
        Grid.SetColumn(k1, 0);
        Grid.SetColumn(k2, 2);
        Grid.SetColumn(k3, 4);
        kpiGrid.Children.Add(k1);
        kpiGrid.Children.Add(k2);
        kpiGrid.Children.Add(k3);
        rootPanel.Children.Add(kpiGrid);

        var filterBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var leftFilter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var statusLbl = Label("Filter:");
        statusLbl.VerticalAlignment = VerticalAlignment.Center;
        statusLbl.Margin = new Thickness(0, 0, 8, 0);
        _statusFilter.Margin = new Thickness(0, 0, 12, 0);
        var noteLbl = Label("Admin note:");
        noteLbl.VerticalAlignment = VerticalAlignment.Center;
        noteLbl.Margin = new Thickness(0, 0, 8, 0);
        leftFilter.Children.Add(statusLbl);
        leftFilter.Children.Add(_statusFilter);
        leftFilter.Children.Add(noteLbl);
        leftFilter.Children.Add(_adminNoteBox);
        DockPanel.SetDock(refreshBtn, Dock.Right);
        filterBar.Children.Add(refreshBtn);
        filterBar.Children.Add(leftFilter);
        rootPanel.Children.Add(filterBar);

        var acctPanel = new StackPanel
        {
            Children =
            {
                _accountGrid,
                new WrapPanel
                {
                    Margin = new Thickness(0, 10, 0, 0),
                    Children = { WithMargin(approveBtn), WithMargin(approveBetaBtn), revokeBtn }
                }
            }
        };
        var guestPanel = new StackPanel
        {
            Children =
            {
                _guestGrid,
                new WrapPanel { Margin = new Thickness(0, 10, 0, 0), Children = { verifyGuestBtn } }
            }
        };
        var insidersPanel = new StackPanel
        {
            Children =
            {
                _insidersGrid,
                new WrapPanel { Margin = new Thickness(0, 10, 0, 0), Children = { revokeBetaBtn } }
            }
        };

        var tabs = new TabControl
        {
            Background = Solid("#121217"),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 12),
            Items =
            {
                new TabItem { Header = "Account Requests", Content = acctPanel },
                new TabItem { Header = "Guest Donations (User#XXXXX)", Content = guestPanel },
                new TabItem { Header = "Verified Insiders (Beta)", Content = insidersPanel }
            }
        };
        rootPanel.Children.Add(tabs);

        var statusSurface = Card(_statusText);
        statusSurface.Margin = new Thickness(0);
        rootPanel.Children.Add(statusSurface);

        var scroll = new ScrollViewer
        {
            Content = rootPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        SmoothScrollBehavior.Attach(scroll);

        var footer = new Border
        {
            Background = Solid("#16161D"),
            BorderBrush = Solid("#2D2D3A"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 12, 24, 12),
            Child = new DockPanel { LastChildFill = false, Children = { AlignRight(closeBtn) } }
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        var layout = new DockPanel { LastChildFill = true, Children = { footer, scroll } };
        Content = layout;

        SourceInitialized += (_, _) => CaptureExclusion.ExcludeFromCapture(this);
        Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
        Loaded += async (_, _) => await RefreshAsync();
    }

    private (string Url, string Key)? ReadCredentials()
    {
        string url = (_urlBox.Text ?? "").Trim().TrimEnd('/');
        string key = (_serviceKeyBox.Password ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) return null;
        return (url, key);
    }

    private async Task SaveConnectionAndReloadAsync()
    {
        var creds = ReadCredentials();
        if (creds == null)
        {
            _statusText.Text = "Enter both Supabase URL and Service Role Key first.";
            return;
        }
        OwnerAdminConfigStore.Save(AppSettings.DataDirectory, new OwnerAdminConfig(creds.Value.Url, creds.Value.Key));
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var creds = ReadCredentials();
        if (creds == null)
        {
            LoadOfflineStagingPreview();
            return;
        }

        try
        {
            string status = _statusFilter.SelectedItem as string ?? "pending";
            var res = await CallAdminRpcAsync<AdminListResponse>(creds.Value, "lt_admin_list_requests", new { p_status = status, p_limit = 100 });
            if (res == null)
            {
                _statusText.Text = "Could not load requests from Supabase (check URL, Service Role Key, and migration 20261123210000).";
                return;
            }
            _currentData = res;
            _accountGrid.ItemsSource = (res.AccountRequests ?? []).Select(AccountReqRow.From).ToArray();
            _guestGrid.ItemsSource = (res.GuestDonations ?? []).Select(GuestReqRow.From).ToArray();
            _insidersGrid.ItemsSource = (res.VerifiedInsiders ?? []).Select(InsiderRow.From).ToArray();
            _kpiPending.Text = (res.AccountRequests?.Length ?? 0).ToString(CultureInfo.InvariantCulture);
            _kpiInsiders.Text = (res.VerifiedInsiders?.Length ?? 0).ToString(CultureInfo.InvariantCulture);
            _kpiGuests.Text = (res.GuestDonations?.Length ?? 0).ToString(CultureInfo.InvariantCulture);
            _statusText.Text = $"Synced from Supabase ({status}) at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Admin request error: {ex.Message}";
        }
    }

    private void LoadOfflineStagingPreview()
    {
        var session = AccountSessionStore.Load(AppSettings.DataDirectory);
        var guest = GuestDonationLedgerStore.Load(AppSettings.DataDirectory);

        var acctRows = session?.History?
            .Select(h => new AccountReqRow(h.Id, session.Email, h.OfferId, h.AmountLabel, h.Status, h.CreatedUtc.ToString("yyyy-MM-dd"), "Local staging"))
            .ToArray() ?? [];
        var guestRows = guest?.History?
            .Select(h => new GuestReqRow(h.Id, guest.GuestTag, h.AmountLabel, h.Status, h.CreatedUtc.ToString("yyyy-MM-dd"), Security.HardwareFingerprint.FormatShortHash(guest.HardwareFingerprintHash), "Local staging"))
            .ToArray() ?? [];
        var insiders = session is { BetaAccessUnlocked: true }
            ? new[] { new InsiderRow("", session.Email, AccountSessionStore.FormatUsd(session.TotalDonatedCents), "Yes (local)", session.SignedInUtc.ToString("yyyy-MM-dd"), "Local staging") }
            : [];

        _accountGrid.ItemsSource = acctRows;
        _guestGrid.ItemsSource = guestRows;
        _insidersGrid.ItemsSource = insiders;
        _kpiPending.Text = acctRows.Length.ToString(CultureInfo.InvariantCulture);
        _kpiInsiders.Text = insiders.Length.ToString(CultureInfo.InvariantCulture);
        _kpiGuests.Text = guestRows.Length.ToString(CultureInfo.InvariantCulture);
        _statusText.Text = "Offline staging view (enter Supabase URL + Service Role Key above to manage live payments 1-by-1).";
    }

    private async Task GrantSelectedAccountAsync(bool? forceBeta)
    {
        if (_accountGrid.SelectedItem is not AccountReqRow row || !Guid.TryParse(row.RequestId, out _))
        {
            _statusText.Text = "Select an account request row with a Supabase UUID first.";
            return;
        }
        var creds = ReadCredentials();
        if (creds == null) { _statusText.Text = "Save live Supabase credentials first."; return; }
        var res = await CallAdminRpcAsync<JsonElement>(creds.Value, "lt_admin_grant_support", new
        {
            p_request_id = row.RequestId,
            p_grant_beta = forceBeta,
            p_admin_note = (_adminNoteBox.Text ?? "").Trim()
        });
        _statusText.Text = res.ValueKind == JsonValueKind.Object ? $"Approved {row.Email} ({row.Offer}, {row.AmountLabel})." : "Grant failed.";
        await RefreshAsync();
    }

    private async Task RevokeSelectedAccountAsync()
    {
        if (_accountGrid.SelectedItem is not AccountReqRow row || !Guid.TryParse(row.RequestId, out _))
        {
            _statusText.Text = "Select an account request row first.";
            return;
        }
        var creds = ReadCredentials();
        if (creds == null) { _statusText.Text = "Save live Supabase credentials first."; return; }
        await CallAdminRpcAsync<JsonElement>(creds.Value, "lt_admin_revoke_support", new
        {
            p_request_id = row.RequestId,
            p_reason = string.IsNullOrWhiteSpace(_adminNoteBox.Text) ? "Revoked by developer" : _adminNoteBox.Text.Trim()
        });
        _statusText.Text = $"Revoked {row.Email} ({row.Offer}).";
        await RefreshAsync();
    }

    private async Task VerifySelectedGuestAsync()
    {
        if (_guestGrid.SelectedItem is not GuestReqRow row || !Guid.TryParse(row.RequestId, out _))
        {
            _statusText.Text = "Select a guest donation row first.";
            return;
        }
        var creds = ReadCredentials();
        if (creds == null) { _statusText.Text = "Save live Supabase credentials first."; return; }
        await CallAdminRpcAsync<JsonElement>(creds.Value, "lt_admin_grant_guest_donation", new
        {
            p_request_id = row.RequestId,
            p_admin_note = (_adminNoteBox.Text ?? "").Trim()
        });
        _statusText.Text = $"Verified guest donation for {row.GuestTag} ({row.AmountLabel}) onto Supporters board.";
        await RefreshAsync();
    }

    private async Task RevokeSelectedInsiderBetaAsync()
    {
        if (_insidersGrid.SelectedItem is not InsiderRow row || !Guid.TryParse(row.UserId, out _))
        {
            _statusText.Text = "Select a verified Insider row first.";
            return;
        }
        var creds = ReadCredentials();
        if (creds == null) { _statusText.Text = "Save live Supabase credentials first."; return; }
        await CallAdminRpcAsync<JsonElement>(creds.Value, "lt_admin_set_beta_access", new
        {
            p_user_id = row.UserId,
            p_active = false,
            p_note = string.IsNullOrWhiteSpace(_adminNoteBox.Text) ? "Deactivated by developer" : _adminNoteBox.Text.Trim()
        });
        _statusText.Text = $"Deactivated Beta grant for {row.Email}.";
        await RefreshAsync();
    }

    private async Task ToggleRankingsAsync(bool open)
    {
        var creds = ReadCredentials();
        if (creds == null) { _statusText.Text = "Save live Supabase credentials first."; return; }
        await CallAdminRpcAsync<JsonElement>(creds.Value, "lt_admin_set_rankings_open", new { p_open = open });
        _statusText.Text = open ? "Public Community Rankings are now OPEN." : "Public Community Rankings are now CLOSED.";
    }

    private async Task<T?> CallAdminRpcAsync<T>((string Url, string Key) creds, string rpcName, object payload)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{creds.Url}/rest/v1/rpc/{rpcName}");
        req.Headers.Add("apikey", creds.Key);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", creds.Key);
        req.Content = JsonContent.Create(payload);
        using var resp = await SharedHttp.SendAsync(req, _lifetime.Token);
        if (!resp.IsSuccessStatusCode)
        {
            string err = await resp.Content.ReadAsStringAsync(_lifetime.Token);
            _statusText.Text = $"HTTP {(int)resp.StatusCode}: {err}";
            return default;
        }
        return await resp.Content.ReadFromJsonAsync<T>(Json, _lifetime.Token);
    }

    private static DataGrid CreateGrid() => new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserAddRows = false,
        CanUserDeleteRows = false,
        SelectionMode = DataGridSelectionMode.Single,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HorizontalGridLinesBrush = Solid("#2C2C38"),
        Background = Solid("#18181F"),
        Foreground = Solid("#F2F2F7"),
        BorderBrush = Solid("#2D2D3A"),
        BorderThickness = new Thickness(1),
        Height = 200,
        RowHeight = 32
    };

    private static DataGridTextColumn TextCol(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = width
    };

    private static Border Card(UIElement child) => new()
    {
        Background = Solid("#18181F"),
        BorderBrush = Solid("#2D2D3A"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(16),
        Margin = new Thickness(0, 0, 0, 12),
        Child = child
    };

    private static Border KpiTile(string title, TextBlock valueBlock) => Card(new StackPanel
    {
        Children =
        {
            new TextBlock { Text = title, FontSize = 11.5, Foreground = Solid("#A1A1AA"), Margin = new Thickness(0, 0, 0, 4) },
            valueBlock
        }
    });

    private static TextBlock KpiValueText(string text) => new()
    {
        Text = text,
        FontSize = 22,
        FontWeight = FontWeights.SemiBold,
        Foreground = Solid("#FAFAFA")
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Solid("#A1A1AA"),
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static Button MakeButton(string text, bool primary = false) => new()
    {
        Content = text,
        Padding = new Thickness(14, 7, 14, 7),
        Background = Solid(primary ? "#0A84FF" : "#262632"),
        Foreground = Solid("#FAFAFA"),
        BorderBrush = Solid(primary ? "#3B9CFF" : "#3A3A4A"),
        BorderThickness = new Thickness(1),
        FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static Button WithMargin(Button b) { b.Margin = new Thickness(0, 0, 8, 0); return b; }
    private static Button AlignRight(Button b) { DockPanel.SetDock(b, Dock.Right); return b; }
    private static SolidColorBrush Solid(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

    private sealed record AdminListResponse(
        [property: JsonPropertyName("accountRequests")] AdminAccountDto[]? AccountRequests,
        [property: JsonPropertyName("guestDonations")] AdminGuestDto[]? GuestDonations,
        [property: JsonPropertyName("verifiedInsiders")] AdminInsiderDto[]? VerifiedInsiders);

    private sealed record AdminAccountDto(string? RequestId, string? UserId, string? Email, string? Offer, long Cents, string? Note, string? Status, DateTimeOffset CreatedAt);
    private sealed record AdminGuestDto(string? RequestId, string? GuestTag, long Cents, string? Note, string? Status, string? HardwareFingerprintHash, DateTimeOffset CreatedAt);
    private sealed record AdminInsiderDto(string? UserId, string? Email, long VerifiedTotalCents, bool ExplicitBetaActive, DateTimeOffset? BetaGrantedAt, string? BetaNote);

    private sealed record AccountReqRow(string RequestId, string Email, string Offer, string AmountLabel, string Status, string DateLabel, string Note)
    {
        public static AccountReqRow From(AdminAccountDto d) => new(
            d.RequestId ?? "", d.Email ?? "", d.Offer ?? "", AccountSessionStore.FormatUsd(d.Cents),
            d.Status ?? "", d.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"), d.Note ?? "");
    }

    private sealed record GuestReqRow(string RequestId, string GuestTag, string AmountLabel, string Status, string DateLabel, string ShortHash, string Note)
    {
        public static GuestReqRow From(AdminGuestDto d) => new(
            d.RequestId ?? "", d.GuestTag ?? "", AccountSessionStore.FormatUsd(d.Cents),
            d.Status ?? "", d.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"),
            string.IsNullOrWhiteSpace(d.HardwareFingerprintHash) ? "" : Security.HardwareFingerprint.FormatShortHash(d.HardwareFingerprintHash),
            d.Note ?? "");
    }

    private sealed record InsiderRow(string UserId, string Email, string TotalLabel, string BetaLabel, string GrantedLabel, string Note)
    {
        public static InsiderRow From(AdminInsiderDto d) => new(
            d.UserId ?? "", d.Email ?? "", AccountSessionStore.FormatUsd(d.VerifiedTotalCents),
            d.ExplicitBetaActive ? "Yes" : "No",
            d.BetaGrantedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "—",
            d.BetaNote ?? "");
    }
}
