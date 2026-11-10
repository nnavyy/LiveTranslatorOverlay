using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace LiveTranslatorOverlay;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _mutex;
    private const string AppMutexName = @"Local\LiveTranslatorOverlay_SingleInstance_Mutex_v1";

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args is ["--record-setup-terms", var revision])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            if (revision != Core.TermsNotice.Revision) { Shutdown(1); return; }
            try
            {
                Core.TermsNotice.Accept(Core.AppSettings.DataDirectory);
                Shutdown();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Shutdown(1); }
            return;
        }

        if (e.Args is ["--prepare-local-speech"])
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            PrepareLocalSpeechForInstaller();
            return;
        }

        bool isNewInstance;
        _mutex = new Mutex(true, AppMutexName, out isNewInstance);

        if (!isNewInstance)
        {
            // Another instance of Live Translator is already running!
            // Bring the existing window to the foreground and exit immediately.
            try
            {
                var current = Process.GetCurrentProcess();
                foreach (var process in Process.GetProcessesByName(current.ProcessName))
                {
                    if (process.Id != current.Id && process.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(process.MainWindowHandle, SW_RESTORE);
                        SetForegroundWindow(process.MainWindowHandle);
                        break;
                    }
                }
            }
            catch { }

            // Terminate duplicate instance immediately
            Environment.Exit(0);
            return;
        }

        if (!Core.TermsNotice.HasAccepted(Core.AppSettings.DataDirectory))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!ShowTerms())
            {
                Shutdown();
                return;
            }
            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }

        Core.Japanese.JapaneseScriptConverter.NativePhoneticReader = Core.Japanese.WindowsPhoneticReader.Read;
        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    internal static bool ShowTerms(Window? owner = null)
    {
        bool review = owner != null;
        var dialog = new Window
        {
            Owner = owner,
            Title = "Live Translator Overlay — Terms and privacy",
            Width = 740, Height = 590, MinWidth = 380, MinHeight = 300,
            WindowStartupLocation = review ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        var layout = new System.Windows.Controls.DockPanel { Margin = new Thickness(18) };
        var actions = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0)
        };
        var accept = new System.Windows.Controls.Button { Content = "Accept terms", Padding = new Thickness(14, 6, 14, 6) };
        var decline = new System.Windows.Controls.Button { Content = review ? "Close" : "Exit", IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Padding = accept.Padding };
        accept.Click += (_, _) =>
        {
            try
            {
                Core.TermsNotice.Accept(Core.AppSettings.DataDirectory);
                dialog.DialogResult = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(dialog, "Live Translator could not save your acceptance. Check that your app data folder is writable.", dialog.Title);
            }
        };
        if (!review) actions.Children.Add(accept);
        actions.Children.Add(decline);
        System.Windows.Controls.DockPanel.SetDock(actions, System.Windows.Controls.Dock.Bottom);
        layout.Children.Add(actions);
        layout.Children.Add(new System.Windows.Controls.TextBox
        {
            Text = Core.TermsNotice.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
        });
        dialog.Content = layout;
        return dialog.ShowDialog() == true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
            catch { }
            _mutex = null;
        }
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        string msg = e.Exception.ToString();
        try { File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] DISPATCHER:\n{msg}\n\n"); } catch { }
        MessageBox.Show("Error: " + e.Exception.Message + "\n\n(Detail disimpan di crash_log.txt)");
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        string msg = e.ExceptionObject?.ToString() ?? "Unknown fatal error";
        try { File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] FATAL:\n{msg}\n\n"); } catch { }
        MessageBox.Show("Fatal: " + msg[..Math.Min(msg.Length, 300)]);
    }
}
