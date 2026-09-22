using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Feedback;
using LiveTranslatorOverlay.Core.Localization;

namespace LiveTranslatorOverlay;

public partial class FeedbackWindow : Window
{
    private const string TargetEmail = "nandazhafran@gmail.com";
    private const string FormSubmitEndpoint = "https://formsubmit.co/ajax/nandazhafran@gmail.com";
    private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    private string? _lastStatusKey;
    private bool _isHistoryActive = false;

    public FeedbackWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        LocalizationManager.Instance.LanguageChanged += (s, e) => Dispatcher.Invoke(ApplyLocalization);
        if (CaptureExclusion.IsExclusionEnabled)
        {
            CaptureExclusion.SetExclusion(this, true);
        }

        UpdateTabCounts();
    }

    public void ApplyLocalization()
    {
        var loc = LocalizationManager.Instance;
        this.Title = loc.Get("FeedbackTitle");
        if (TxtTitle != null) TxtTitle.Text = loc.Get("FeedbackTitle");
        if (TxtSubtitle != null) TxtSubtitle.Text = loc.Get("FeedbackSubtitle");
        if (BtnTabSubmit != null) BtnTabSubmit.Content = loc.Get("FeedbackTabSubmit");
        if (LblType != null) LblType.Text = loc.Get("FeedbackType");
        if (ItemTypeBug != null) ItemTypeBug.Content = loc.Get("FeedbackTypeBug");
        if (ItemTypeFeature != null) ItemTypeFeature.Content = loc.Get("FeedbackTypeFeature");
        if (ItemTypeGeneral != null) ItemTypeGeneral.Content = loc.Get("FeedbackTypeGeneral");
        if (LblContact != null) LblContact.Text = loc.Get("FeedbackContact");
        if (LblSubject != null) LblSubject.Text = loc.Get("FeedbackSubject");
        if (LblMessage != null) LblMessage.Text = loc.Get("FeedbackMessage");
        if (BtnSend != null) BtnSend.Content = loc.Get("FeedbackSend");
        if (BtnMailto != null) BtnMailto.Content = loc.Get("FeedbackMailto");
        if (BtnClose != null) BtnClose.Content = loc.Get("FeedbackClose");
        if (BtnRefreshStatus != null) BtnRefreshStatus.Content = loc.Get("FeedbackRefreshStatus");
        if (TxtHistoryEmpty != null) TxtHistoryEmpty.Text = loc.Get("FeedbackHistoryEmpty");
        if (TxtDeviceIdDisplay != null) TxtDeviceIdDisplay.Text = $"Device: {AppSettings.GetOrCreateDeviceId()}";

        UpdateTabCounts();

        if (!string.IsNullOrEmpty(_lastStatusKey) && TxtStatus != null)
        {
            TxtStatus.Text = loc.Get(_lastStatusKey);
        }

        if (_isHistoryActive)
        {
            RenderHistoryList();
        }
    }

    private void UpdateTabCounts()
    {
        var loc = LocalizationManager.Instance;
        int count = FeedbackHistoryManager.Load().Count;
        if (BtnTabHistory != null)
        {
            BtnTabHistory.Content = loc.Get("FeedbackTabHistory", count);
        }
    }

    private void BtnTabSubmit_Click(object sender, RoutedEventArgs e)
    {
        _isHistoryActive = false;
        GridSubmit.Visibility = Visibility.Visible;
        GridHistory.Visibility = Visibility.Collapsed;

        BtnTabSubmit.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC"));
        BtnTabSubmit.Foreground = Brushes.White;

        BtnTabHistory.BorderBrush = Brushes.Transparent;
        BtnTabHistory.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888"));

        BtnSend.Visibility = Visibility.Visible;
        BtnMailto.Visibility = Visibility.Visible;
        TxtStatus.Text = "";
        _lastStatusKey = null;
    }

    private void BtnTabHistory_Click(object sender, RoutedEventArgs e)
    {
        _isHistoryActive = true;
        GridSubmit.Visibility = Visibility.Collapsed;
        GridHistory.Visibility = Visibility.Visible;

        BtnTabHistory.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC"));
        BtnTabHistory.Foreground = Brushes.White;

        BtnTabSubmit.BorderBrush = Brushes.Transparent;
        BtnTabSubmit.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#888888"));

        BtnSend.Visibility = Visibility.Collapsed;
        BtnMailto.Visibility = Visibility.Collapsed;
        TxtStatus.Text = "";
        _lastStatusKey = null;

        RenderHistoryList();
    }

    private void RenderHistoryList()
    {
        PanelHistoryItems.Children.Clear();
        var items = FeedbackHistoryManager.Load();
        var loc = LocalizationManager.Instance;

        if (items.Count == 0)
        {
            TxtHistoryEmpty.Visibility = Visibility.Visible;
            return;
        }

        TxtHistoryEmpty.Visibility = Visibility.Collapsed;

        foreach (var item in items)
        {
            var card = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222226")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333338")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var stack = new StackPanel();

            // Header row
            var headerDock = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            // Date on right
            var txtDate = new TextBlock
            {
                Text = item.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777777")),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(txtDate, Dock.Right);
            headerDock.Children.Add(txtDate);

            // Left badges stack
            var badgePanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Type badge
            var typeBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E2E33")),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 0)
            };
            typeBorder.Child = new TextBlock
            {
                Text = item.Type,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC")),
                FontSize = 10
            };
            badgePanel.Children.Add(typeBorder);

            // Status badge
            string statusKey = item.Status switch
            {
                "Fixed" => "FeedbackStatusFixed",
                "InProgress" => "FeedbackStatusInProgress",
                _ => "FeedbackStatusPending"
            };
            string statusColorBg = item.Status switch
            {
                "Fixed" => "#064E3B",
                "InProgress" => "#1E3A8A",
                _ => "#78350F"
            };
            string statusColorFg = item.Status switch
            {
                "Fixed" => "#6EE7B7",
                "InProgress" => "#93C5FD",
                _ => "#FCD34D"
            };

            var statusBorder = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusColorBg)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2)
            };
            statusBorder.Child = new TextBlock
            {
                Text = loc.Get(statusKey),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusColorFg)),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold
            };
            badgePanel.Children.Add(statusBorder);

            headerDock.Children.Add(badgePanel);
            stack.Children.Add(headerDock);

            // Subject
            var txtSubject = new TextBlock
            {
                Text = item.Subject,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 2, 0, 2),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(txtSubject);

            // Description
            var txtDesc = new TextBlock
            {
                Text = item.Description,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA")),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stack.Children.Add(txtDesc);

            // Footer row (ID, AppVersion, Resolution Note)
            var footerDock = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };

            if (!string.IsNullOrWhiteSpace(item.ResolutionNote))
            {
                var txtNote = new TextBlock
                {
                    Text = item.ResolutionNote,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399")),
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(txtNote, Dock.Right);
                footerDock.Children.Add(txtNote);
            }

            var txtId = new TextBlock
            {
                Text = $"{item.Id} ({item.AppVersion})",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666")),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            footerDock.Children.Add(txtId);

            stack.Children.Add(footerDock);

            card.Child = stack;
            PanelHistoryItems.Children.Add(card);
        }
    }

    private async void BtnRefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        BtnRefreshStatus.IsEnabled = false;
        TxtStatus.Foreground = Brushes.LightSkyBlue;
        TxtStatus.Text = loc.Get("FeedbackRefreshing");

        try
        {
            await FeedbackHistoryManager.RefreshStatusesAsync("v1.1.0");
            RenderHistoryList();
            TxtStatus.Foreground = Brushes.LightGreen;
            TxtStatus.Text = loc.Get("FeedbackRefreshed");
            _lastStatusKey = "FeedbackRefreshed";
        }
        catch
        {
            TxtStatus.Foreground = Brushes.OrangeRed;
            TxtStatus.Text = loc.Get("FeedbackError");
            _lastStatusKey = "FeedbackError";
        }
        finally
        {
            BtnRefreshStatus.IsEnabled = true;
        }
    }

    private async void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        string subject = TxtSubject.Text?.Trim() ?? "";
        string message = TxtMessage.Text?.Trim() ?? "";
        string contact = TxtContact.Text?.Trim() ?? "";
        string type = (CmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Feedback";

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(message))
        {
            _lastStatusKey = "FeedbackEmpty";
            TxtStatus.Foreground = Brushes.OrangeRed;
            TxtStatus.Text = loc.Get("FeedbackEmpty");
            return;
        }

        BtnSend.IsEnabled = false;
        _lastStatusKey = "FeedbackSending";
        TxtStatus.Foreground = Brushes.LightSkyBlue;
        TxtStatus.Text = loc.Get("FeedbackSending");

        try
        {
            // Create local history record first
            var record = FeedbackHistoryManager.AddRecord(type, subject, message, contact, "v1.1.0");

            string user = Environment.UserName;
            string device = Environment.MachineName;
            string deviceId = AppSettings.GetOrCreateDeviceId();
            string contactEmail = string.IsNullOrWhiteSpace(contact) ? "Not provided (Anonymous)" : contact;
            string systemOs = $"{Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})";
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

            var payload = new System.Collections.Generic.Dictionary<string, string>
            {
                { "Report ID", record.Id },
                { "User", user },
                { "Device", device },
                { "Device ID", deviceId },
                { "Report Type", type },
                { "Contact Email", contactEmail },
                { "Subject", subject },
                { "Description", message },
                { "App Version", "v1.1.0" },
                { "OS Version", systemOs },
                { ".NET Runtime", Environment.Version.ToString() },
                { "Timestamp", timestamp },
                { "email", string.IsNullOrWhiteSpace(contact) ? "noreply@livetranslator.app" : contact },
                { "_subject", $"[Live Translator] {type} from {user} ({record.Id}): {subject}" },
                { "_template", "table" },
                { "_captcha", "false" }
            };

            if (!string.IsNullOrWhiteSpace(contact))
            {
                payload["_replyto"] = contact;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, FormSubmitEndpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Origin", "https://livetranslator.app");
            request.Headers.Add("Referer", "https://livetranslator.app");

            var response = await HttpClient.SendAsync(request);
            string responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode || responseBody.Contains("Activation", StringComparison.OrdinalIgnoreCase))
            {
                _lastStatusKey = "FeedbackSuccess";
                TxtStatus.Foreground = Brushes.LightGreen;
                TxtStatus.Text = loc.Get("FeedbackSuccess");
                TxtSubject.Text = "";
                TxtMessage.Text = "";
                BtnSend.IsEnabled = true;
                UpdateTabCounts();
            }
            else
            {
                _lastStatusKey = "FeedbackError";
                TxtStatus.Foreground = Brushes.OrangeRed;
                TxtStatus.Text = loc.Get("FeedbackError");
                BtnSend.IsEnabled = true;
            }
        }
        catch
        {
            _lastStatusKey = "FeedbackError";
            TxtStatus.Foreground = Brushes.OrangeRed;
            TxtStatus.Text = loc.Get("FeedbackError");
            BtnSend.IsEnabled = true;
        }
    }

    private void BtnMailto_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        string user = Environment.UserName;
        string device = Environment.MachineName;
        string deviceId = AppSettings.GetOrCreateDeviceId();
        string type = (CmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Feedback";
        string subject = TxtSubject.Text?.Trim() ?? "";
        string message = TxtMessage.Text?.Trim() ?? "";
        string contact = TxtContact.Text?.Trim() ?? "";

        var record = FeedbackHistoryManager.AddRecord(type, subject, message, contact, "v1.1.0");
        UpdateTabCounts();

        string mailtoSubject = Uri.EscapeDataString($"[Live Translator] {type} from {user} ({record.Id}): {subject}");
        string mailtoBody = Uri.EscapeDataString(
            $"{message}\n\n" +
            $"========================================\n" +
            $"User & System Diagnostics (Auto-generated)\n" +
            $"Report ID: {record.Id}\n" +
            $"User: {user}\n" +
            $"Device: {device}\n" +
            $"Device ID: {deviceId}\n" +
            $"Contact Email: {(string.IsNullOrWhiteSpace(contact) ? "Not provided" : contact)}\n" +
            $"App Version: v1.1.0\n" +
            $"OS: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})\n" +
            $".NET: {Environment.Version}\n" +
            $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
            $"========================================"
        );
        string mailtoUrl = $"mailto:{TargetEmail}?subject={mailtoSubject}&body={mailtoBody}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = mailtoUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(loc.Get("FeedbackMailtoError", ex.Message), "Email", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
