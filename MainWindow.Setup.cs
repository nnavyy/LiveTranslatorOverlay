using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Update;

namespace LiveTranslatorOverlay;

public partial class MainWindow
{
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
        var window = CreateInfoWindow("Privacy, terms and license", App.CreateTermsView(selectedTab: 1), owner);
        if (window.Content is FrameworkElement view) view.Margin = new Thickness(20, 16, 20, 16);
        window.Width = 700;
        window.Height = 600;
        window.ShowDialog();
    }

    private void BtnDeveloper_Click(object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Running {AppVersion.DisplayString}\n{Environment.ProcessPath}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
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
        var window = CreateInfoWindow("Developer tools", new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        window.Height = 450;
        window.ShowDialog();
    }
}
