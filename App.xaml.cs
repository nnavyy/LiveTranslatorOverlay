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

        base.OnStartup(e);
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
