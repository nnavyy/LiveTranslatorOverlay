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
            if (OperatingSystem.IsMacOS() && !MacAudioCapture.CanLoadNativeBridge())
            {
                Console.Error.WriteLine("The bundled Mac audio bridge could not be loaded.");
                return 1;
            }
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
            else desktop.MainWindow = CreateTermsWindow(() =>
            {
                desktop.MainWindow = new TranslatorWindow();
                desktop.MainWindow.Show();
            });
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal static Window CreateTermsWindow(Action? onAccepted = null)
    {
        var window = new Window
        {
            Title = "Live Translator — Terms and privacy",
            Width = 740, Height = 620, MinWidth = 400, MinHeight = 365,
            WindowStartupLocation = onAccepted == null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        var accept = new Button { Content = "Accept terms" };
        accept.Classes.Add("primary");
        var decline = new Button { Content = onAccepted == null ? "Close" : "Exit" };
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        actions.Children.Add(decline);
        if (onAccepted != null) actions.Children.Add(accept);
        var header = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 14), Spacing = 5,
            Children =
            {
                new TextBlock { Text = "Terms and privacy", FontSize = 27, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = $"Live Translator Overlay  ·  Revision {TermsNotice.Revision}", FontSize = 13 }
            }
        };
        DockPanel.SetDock(header, Dock.Top);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { error, actions } };
        DockPanel.SetDock(footer, Dock.Bottom);
        window.Content = new DockPanel
        {
            Margin = new Thickness(24, 20, 24, 18),
            Children = { header, footer, new TabControl
            {
                ItemsSource = new[] { TermsTab("Terms of Service", TermsNotice.LicenseText), TermsTab("Privacy notice", TermsNotice.PrivacyText) }
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
            onAccepted?.Invoke();
            window.Close();
        };
        return window;
    }

    private static TabItem TermsTab(string label, string markdown)
    {
        var document = new StackPanel { Margin = new Thickness(14, 14, 14, 18), Spacing = 12 };
        foreach (var section in TermsNotice.Sections(markdown))
        {
            if (section.Heading.Length > 0)
                document.Children.Add(new TextBlock
                {
                    Text = section.Heading, FontSize = 17, FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
            if (section.Body.Length > 0)
                document.Children.Add(new SelectableTextBlock
                {
                    Text = section.Body, FontSize = 14, LineHeight = 21, TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
        }
        return new TabItem { Header = label, Content = new ScrollViewer { Content = document } };
    }
}
