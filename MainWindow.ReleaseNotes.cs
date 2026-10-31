using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Localization;
using LiveTranslatorOverlay.Core.Update;

namespace LiveTranslatorOverlay;

public partial class MainWindow
{
    private Window? _releaseNotesWindow;

    private void ShowReleaseNotesIfNew()
    {
        if (ReleaseNotes.HasUnseenRelease()) ShowReleaseNotes();
    }

    private void BtnReleaseNotes_Click(object sender, RoutedEventArgs e) => ShowReleaseNotes();

    private void ShowReleaseNotes()
    {
        if (_releaseNotesWindow != null) { _releaseNotesWindow.Activate(); return; }
        var loc = LocalizationManager.Instance;
        var text = new TextBox
        {
            Text = ReleaseNotes.Text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 34)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(22),
            FontSize = 15
        };
        var window = new Window
        {
            Title = $"{loc.Get("WhatsNew")} — {AppVersion.DisplayString}",
            Owner = this,
            Width = 620, Height = 510, MinWidth = 360, MinHeight = 280,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = text
        };
        _releaseNotesWindow = window;
        window.SourceInitialized += (_, _) => CaptureExclusion.SetExclusion(window, CaptureExclusion.IsExclusionEnabled);
        window.Closed += (_, _) =>
        {
            _releaseNotesWindow = null;
            try { ReleaseNotes.MarkSeen(); }
            catch (IOException ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
            catch (UnauthorizedAccessException ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
        };
        window.Show();
    }
}
