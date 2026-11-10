using System.IO;
using System.Windows;
using System.Windows.Controls;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Update;

namespace LiveTranslatorOverlay;

public partial class App
{
    private async void PrepareLocalSpeechForInstaller()
    {
        int exitCode = 0;
        try
        {
            if (!await SpeechModelDownload.HasDefaultModelsAsync(CancellationToken.None))
                ShowSpeechDownload();
        }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError(error.ToString());
            exitCode = 1;
        }
        finally
        {
            // The installer already introduced this version. Keep What's New available in settings.
            try { ReleaseNotes.MarkSeen(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Shutdown(exitCode);
        }
    }

    private static void ShowSpeechDownload()
    {
        var status = new TextBlock
        {
            Text = "Preparing local speech…",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 12)
        };
        var retry = new Button { Content = "Retry", Padding = new Thickness(12, 6, 12, 6), Visibility = Visibility.Collapsed };
        var skip = new Button { Content = "Cancel download", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(retry);
        buttons.Children.Add(skip);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "The installer is downloading the missing files for local speech. The default speech model and detector total about 149 MB. You can finish setup and download them later if needed.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(status);
        panel.Children.Add(buttons);

        var window = new Window
        {
            Title = "Live Translator Overlay Setup",
            Content = panel,
            Width = 490,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };

        CancellationTokenSource? pending = null;
        window.Closing += (_, e) =>
        {
            if (pending == null) return;
            pending.Cancel();
            e.Cancel = true;
        };
        skip.Click += (_, _) => window.Close();
        retry.Click += async (_, _) => await DownloadAsync();
        window.ContentRendered += async (_, _) => await DownloadAsync();
        window.ShowDialog();

        async Task DownloadAsync()
        {
            if (pending != null) return;
            using var cancellation = new CancellationTokenSource();
            pending = cancellation;
            retry.Visibility = Visibility.Collapsed;
            skip.Content = "Cancel download";

            bool finish = false;
            void ReportProgress(string message) => status.Text = message.Replace("Stop cancels.", "").Trim();
            try
            {
                await SpeechModelDownload.EnsureAsync(small: false, ReportProgress, cancellation.Token);
                await SpeechModelDownload.EnsureVadAsync(ReportProgress, cancellation.Token);
                finish = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { finish = true; }
            catch (Exception error)
            {
                status.Text = "The speech files could not be prepared. Retry or continue without downloading. " + error.Message;
                retry.Visibility = Visibility.Visible;
                skip.Content = "Continue without downloading";
            }
            finally { pending = null; }

            if (finish) window.Close();
        }
    }
}
