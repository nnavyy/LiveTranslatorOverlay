using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Localization;

namespace LiveTranslatorOverlay;

public partial class FeedbackWindow : Window
{
    private const string TargetEmail = "nandazhafran@gmail.com";
    private const string FormSubmitEndpoint = "https://formsubmit.co/ajax/nandazhafran@gmail.com";
    private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public FeedbackWindow()
    {
        InitializeComponent();
        ApplyLocalization();
        LocalizationManager.Instance.LanguageChanged += (s, e) => Dispatcher.Invoke(ApplyLocalization);
        if (CaptureExclusion.IsExclusionEnabled)
        {
            CaptureExclusion.SetExclusion(this, true);
        }
    }

    public void ApplyLocalization()
    {
        var loc = LocalizationManager.Instance;
        this.Title = loc.Get("FeedbackTitle");
        if (TxtTitle != null) TxtTitle.Text = loc.Get("FeedbackTitle");
        if (TxtSubtitle != null) TxtSubtitle.Text = loc.Get("FeedbackSubtitle");
        if (LblType != null) LblType.Text = loc.Get("FeedbackType");
        if (ItemTypeBug != null) ItemTypeBug.Content = loc.Get("FeedbackTypeBug");
        if (ItemTypeFeature != null) ItemTypeFeature.Content = loc.Get("FeedbackTypeFeature");
        if (ItemTypeGeneral != null) ItemTypeGeneral.Content = loc.Get("FeedbackTypeGeneral");
        if (LblContact != null) LblContact.Text = loc.Get("FeedbackContact");
        if (LblSubject != null) LblSubject.Text = loc.Get("FeedbackSubject");
        if (LblMessage != null) LblMessage.Text = loc.Get("FeedbackMessage");
        if (BtnSend != null) BtnSend.Content = loc.Get("FeedbackSend");
        if (BtnMailto != null) BtnMailto.Content = loc.Get("FeedbackMailto");
        if (BtnClose != null) BtnClose.Content = loc.Get("BtnCloseApp");
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
            TxtStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            TxtStatus.Text = loc.Get("FeedbackEmpty");
            return;
        }

        BtnSend.IsEnabled = false;
        TxtStatus.Foreground = System.Windows.Media.Brushes.LightSkyBlue;
        TxtStatus.Text = loc.Get("FeedbackSending");

        try
        {
            string user = Environment.UserName;
            string device = Environment.MachineName;
            string deviceId = AppSettings.GetOrCreateDeviceId();
            string contactEmail = string.IsNullOrWhiteSpace(contact) ? "Not provided (Anonymous)" : contact;
            string systemOs = $"{Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})";
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

            var payload = new System.Collections.Generic.Dictionary<string, string>
            {
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
                { "_subject", $"[Live Translator] {type} from {user} ({deviceId}): {subject}" },
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
                TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                TxtStatus.Text = loc.Get("FeedbackSuccess");
                await Task.Delay(2000);
                this.Close();
            }
            else
            {
                TxtStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                TxtStatus.Text = loc.Get("FeedbackError");
                BtnSend.IsEnabled = true;
            }
        }
        catch
        {
            TxtStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
            TxtStatus.Text = loc.Get("FeedbackError");
            BtnSend.IsEnabled = true;
        }
    }

    private void BtnMailto_Click(object sender, RoutedEventArgs e)
    {
        string user = Environment.UserName;
        string device = Environment.MachineName;
        string deviceId = AppSettings.GetOrCreateDeviceId();
        string type = (CmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Feedback";
        string subject = TxtSubject.Text?.Trim() ?? "";
        string message = TxtMessage.Text?.Trim() ?? "";
        string contact = TxtContact.Text?.Trim() ?? "";

        string mailtoSubject = Uri.EscapeDataString($"[Live Translator] {type} from {user} ({deviceId}): {subject}");
        string mailtoBody = Uri.EscapeDataString(
            $"{message}\n\n" +
            $"========================================\n" +
            $"User & System Diagnostics (Auto-generated)\n" +
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
            MessageBox.Show($"Failed to open email client: {ex.Message}", "Email", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
