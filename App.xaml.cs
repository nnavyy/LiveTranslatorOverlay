using System.Windows;
using System.IO;
using System;

namespace LiveTranslatorOverlay;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
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
        string msg = e.ExceptionObject.ToString();
        try { File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] FATAL:\n{msg}\n\n"); } catch { }
        MessageBox.Show("Fatal: " + msg[..Math.Min(msg.Length, 300)]);
    }
}
