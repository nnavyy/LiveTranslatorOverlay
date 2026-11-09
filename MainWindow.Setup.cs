using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Update;

namespace LiveTranslatorOverlay;

public partial class MainWindow
{
    private void ShowFirstRunOrReleaseNotes()
    {
        string receipt = Path.Combine(AppSettings.DataDirectory, "welcome-v1.txt");
        if (File.Exists(receipt))
        {
            ShowReleaseNotesIfNew();
            return;
        }

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Set up local speech", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "Download the balanced speech model (about 149 MB including the speech detector). Speech recognition runs on this computer without an account, API key or GPU.\n\nGoogle Translate is the default for text and sends recognized text online. Choosing cloud speech sends audio to that provider. Captions and translations can be wrong or include words nobody said.\n\nRead the privacy policy, terms and app license before continuing.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 12)
        });
        var privacy = SetupButton("Privacy, terms and license");
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var later = SetupButton("Continue without downloading");
        var download = SetupButton("Download and continue");
        buttons.Children.Add(later);
        buttons.Children.Add(download);
        panel.Children.Add(privacy);
        panel.Children.Add(status);
        panel.Children.Add(buttons);
        var window = CreateInfoWindow("Welcome to Live Translator Overlay", new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Height = 435;
        privacy.Click += (_, _) => ShowPrivacy(window);

        CancellationTokenSource? pending = null;
        window.Closing += (_, e) =>
        {
            if (pending == null) return;
            pending.Cancel();
            e.Cancel = true;
        };
        void Continue()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                File.WriteAllText(receipt, AppVersion.DisplayString);
                ReleaseNotes.MarkSeen();
                window.Close();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                status.Text = "Setup preferences could not be saved. " + error.Message;
            }
        }

        later.Click += (_, _) =>
        {
            if (pending != null) pending.Cancel();
            else Continue();
        };
        download.Click += async (_, _) =>
        {
            using var cancellation = new CancellationTokenSource();
            pending = cancellation;
            download.IsEnabled = false;
            later.Content = "Cancel download";
            bool complete = false;
            void ReportProgress(string message) => status.Text = message.Replace("Stop cancels.", "").Trim();
            try
            {
                await SpeechModelDownload.EnsureAsync(small: false, ReportProgress, cancellation.Token);
                await SpeechModelDownload.EnsureVadAsync(ReportProgress, cancellation.Token);
                complete = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { status.Text = "Download canceled. You can retry or continue."; }
            catch (Exception error) { status.Text = "Speech setup could not finish. " + error.Message; }
            finally
            {
                pending = null;
                download.IsEnabled = true;
                later.Content = "Continue without downloading";
            }
            if (complete) Continue();
        };
        window.ShowDialog();
    }

    private Button SetupButton(string label) => new()
    {
        Content = label,
        Style = (Style)FindResource("ModernButton"),
        Padding = new Thickness(12, 7, 12, 7),
        Margin = new Thickness(0, 4, 8, 4),
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private Window CreateInfoWindow(string title, object content, Window? owner = null)
    {
        var window = new Window
        {
            Title = title,
            Owner = owner ?? this,
            Width = 605,
            Height = 510,
            MinWidth = 370,
            MinHeight = 315,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 34)),
            Foreground = Brushes.White,
            Content = content,
            ShowInTaskbar = false
        };
        window.SourceInitialized += (_, _) => CaptureExclusion.ExcludeFromCapture(window);
        return window;
    }

    private void BtnPrivacy_Click(object sender, RoutedEventArgs e) => ShowPrivacy(this);

    private void ShowPrivacy(Window owner)
    {
        try
        {
            string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
            var text = new TextBox
            {
                Text = Read("PRIVACY.md") + "\n\n" + Read("APP_LICENSE.md"),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 34)),
                Foreground = Brushes.White,
                Padding = new Thickness(20)
            };
            CreateInfoWindow("Privacy, terms and license", text, owner).ShowDialog();
        }
        catch (IOException) { MessageBox.Show(owner, "The bundled privacy and license files could not be opened."); }
        catch (UnauthorizedAccessException) { MessageBox.Show(owner, "Windows denied access to the bundled privacy and license files."); }
    }

    private void BtnDeveloper_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "Developer tools use your existing GitHub and Gmail sign-in. Feedback from other users is available in the receiving mailbox when delivery succeeds.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        string archive = Path.Combine(AppContext.BaseDirectory, "versions");
        void AddLink(string label, string target)
        {
            var button = SetupButton(label);
            button.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception error) { MessageBox.Show(error.Message); }
            };
            panel.Children.Add(button);
        }
        if (Directory.Exists(archive)) AddLink("Local Windows version archive", archive);
        AddLink("Public releases", "https://github.com/nnavyy/LiveTranslatorOverlay/releases");
        AddLink("Private build releases", "https://github.com/nnavyy/LiveTranslatorOverlay-private/releases");
        AddLink("Received reports and feedback in Gmail", "https://mail.google.com/mail/?authuser=nandazhafran%40gmail.com#search/in%3Aanywhere%20%22Live%20Translator%22");
        AddLink("Report status file", "https://github.com/nnavyy/LiveTranslatorOverlay/blob/main/reports_status.json");
        var window = CreateInfoWindow("Developer tools", panel);
        window.Height = 395;
        window.ShowDialog();
    }
}
