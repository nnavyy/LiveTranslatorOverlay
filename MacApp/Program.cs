using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Update;

namespace LiveTranslatorMac;

internal static class Program
{
    internal static bool UiSmokeTest { get; private set; }
    internal static bool WindowOpened { get; set; }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--smoke-test"))
        {
            var history = new TranslationHistory();
            history.Add("hello", "hola");
            if (!history.ToString().Contains("hola")) return 1;
            history.Clear();
            if (history.ToString() != "") return 1;
            if (LiveTranslatorOverlay.Core.Localization.LanguageCatalog.Names.Count != 34) return 1;
            var locale = LiveTranslatorOverlay.Core.Localization.LocalizationManager.Instance;
            locale.SetLanguage("es");
            if (locale.Get("BtnSettings") != "Ajustes") return 1;
            locale.SetLanguage("en");
            Console.WriteLine($"Mac preview smoke check passed: {AppVersion.DisplayString}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            return 0;
        }
        UiSmokeTest = args.Contains("--ui-smoke-test");
        AppBuilder.Configure<MacApplication>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
        if (UiSmokeTest) Console.WriteLine($"Mac UI smoke check: opened={WindowOpened}");
        return UiSmokeTest && !WindowOpened ? 1 : 0;
    }
}

public sealed class MacApplication : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new TranslatorWindow();
            desktop.MainWindow.Opened += (_, _) =>
            {
                Program.WindowOpened = true;
                if (Program.UiSmokeTest)
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => desktop.MainWindow.Close(), TimeSpan.FromSeconds(2));
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
