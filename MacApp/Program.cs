using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using LiveTranslatorOverlay.Core;

namespace LiveTranslatorMac;

internal static class Program
{
    internal static string DisplayVersion => $"v{typeof(Program).Assembly.GetName().Version!.ToString(3)}";
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
            if (LiveTranslatorOverlay.Core.Localization.LanguageCatalog.Names.Count < 34 ||
                LiveTranslatorOverlay.Core.Localization.AdditionalLanguages.Names.Keys.Any(code =>
                    !LiveTranslatorOverlay.Core.Localization.LanguageCatalog.Names.ContainsKey(code))) return 1;
            var locale = LiveTranslatorOverlay.Core.Localization.LocalizationManager.Instance;
            locale.SetLanguage("es");
            if (locale.Get("BtnSettings") != "Ajustes") return 1;
            locale.SetLanguage("en");
            Console.WriteLine($"Mac preview smoke check passed: {DisplayVersion}; {System.Runtime.InteropServices.RuntimeInformation.OSDescription}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
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
        Styles.Add(new StyleInclude(new Uri("avares://LiveTranslatorMac/")) { Source = new Uri("avares://LiveTranslatorMac/Styles.axaml") });
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Default;
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Program.UiSmokeTest || TermsNotice.HasAccepted(AppSettings.DataDirectory))
            {
                var window = new TranslatorWindow();
                desktop.MainWindow = window;
                if (Program.UiSmokeTest)
                    window.Opened += (_, _) =>
                    {
                        Program.WindowOpened = true;
                        Avalonia.Threading.DispatcherTimer.RunOnce(window.Close, TimeSpan.FromSeconds(2));
                    };
            }
            else desktop.MainWindow = CreateTermsWindow(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static Window CreateTermsWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = new Window
        {
            Title = "Live Translator — Terms and privacy",
            Width = 740, Height = 590, MinWidth = 380, MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var accept = new Button { Content = "Accept terms" };
        var decline = new Button { Content = "Exit" };
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { accept, decline }
        };
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { error, actions } };
        DockPanel.SetDock(footer, Dock.Bottom);
        window.Content = new DockPanel
        {
            Margin = new Thickness(18),
            Children = { footer, new TextBox
            {
                Text = TermsNotice.Text, IsReadOnly = true, AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            } }
        };
        decline.Click += (_, _) => window.Close();
        accept.Click += (_, _) =>
        {
            try { TermsNotice.Accept(AppSettings.DataDirectory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error.Text = "Could not save acceptance. Check that your app data folder is writable.";
                return;
            }
            desktop.MainWindow = new TranslatorWindow();
            desktop.MainWindow.Show();
            window.Close();
        };
        return window;
    }
}
