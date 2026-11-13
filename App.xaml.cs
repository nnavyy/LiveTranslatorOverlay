using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace LiveTranslatorOverlay;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    internal static bool IsDeveloperMode { get; } = Environment.GetCommandLineArgs().Contains("--developer", StringComparer.Ordinal);
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
        var styles = TermsStyles;
        var dialog = new Window
        {
            Owner = owner,
            Title = "Live Translator Overlay — Terms and privacy",
            Width = 740, Height = 620, MinWidth = 400, MinHeight = 365,
            Background = TermsBrush(0x1C, 0x1C, 0x22), Foreground = TermsBrush(0xF2, 0xF2, 0xF7),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"),
            WindowStartupLocation = review ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        var layout = new DockPanel { Margin = new Thickness(24, 20, 24, 18) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = "Terms and privacy", FontSize = 27, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        header.Children.Add(new TextBlock
        {
            Text = $"Live Translator Overlay  ·  Revision {Core.TermsNotice.Revision}",
            Margin = new Thickness(0, 5, 0, 0), FontSize = 13, TextWrapping = TextWrapping.Wrap,
            Foreground = TermsBrush(0xA1, 0xA1, 0xAA)
        });
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0)
        };
        var accept = new Button { Content = "Accept terms", Style = (Style)styles["TermsPrimaryButton"], Margin = new Thickness(8, 0, 0, 0) };
        var decline = new Button { Content = review ? "Close" : "Exit", IsCancel = true, Style = (Style)styles["TermsButton"] };
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
        actions.Children.Add(decline);
        if (!review) actions.Children.Add(accept);
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);
        layout.Children.Add(CreateTermsView());
        dialog.Content = layout;
        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// The one place that displays the bundled Terms of Service and Privacy Policy on Windows.
    /// A tab strip switches between two read-only, selectable, wrapping documents rendered from
    /// <see cref="Core.TermsNotice.Blocks"/>, so Markdown markup is never shown.
    /// </summary>
    public static FrameworkElement CreateTermsView(int selectedTab = 0)
    {
        var styles = TermsStyles;
        var root = new Grid();
        root.Resources.MergedDictionaries.Add(styles);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var body = new Border
        {
            Background = TermsBrush(0x22, 0x22, 0x2A), BorderBrush = TermsBrush(0x33, 0x33, 0x3C),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(4)
        };
        Grid.SetRow(body, 1);
        var pages = new Grid();
        body.Child = pages;
        var documents = new (string Label, string Markdown)[]
        {
            ("Terms of Service", Core.TermsNotice.LicenseText),
            ("Privacy Policy", Core.TermsNotice.PrivacyText)
        };
        for (int i = 0; i < documents.Length; i++)
        {
            var reader = CreateTermsReader(documents[i].Markdown, documents[i].Label);
            reader.Visibility = i == selectedTab ? Visibility.Visible : Visibility.Collapsed;
            pages.Children.Add(reader);
            var tab = new RadioButton
            {
                Content = documents[i].Label, GroupName = "TermsTabs" + root.GetHashCode(), IsChecked = i == selectedTab,
                Style = (Style)styles["TermsTab"]
            };
            tab.Checked += (_, _) => { foreach (UIElement page in pages.Children) page.Visibility = page == reader ? Visibility.Visible : Visibility.Collapsed; };
            System.Windows.Automation.AutomationProperties.SetName(tab, documents[i].Label);
            strip.Children.Add(tab);
        }
        root.Children.Add(strip);
        root.Children.Add(body);
        return root;
    }

    public static RichTextBox CreateTermsReader(string markdown, string automationName)
    {
        var text = TermsBrush(0xD8, 0xD8, 0xDF);
        var strong = TermsBrush(0xFF, 0xFF, 0xFF);
        var document = new FlowDocument
        {
            PagePadding = new Thickness(14, 10, 14, 16), FontSize = 14, Foreground = text,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"),
            TextAlignment = TextAlignment.Left
        };
        List? bullets = null;
        foreach (var block in Core.TermsNotice.Blocks(markdown))
        {
            if (block.Kind != Core.TermsBlockKind.Bullet) bullets = null;
            switch (block.Kind)
            {
                case Core.TermsBlockKind.Title:
                    document.Blocks.Add(new Paragraph(new Run(block.Text))
                    {
                        FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = strong,
                        Margin = new Thickness(0, 0, 0, 8), KeepWithNext = true
                    });
                    break;
                case Core.TermsBlockKind.Heading:
                    document.Blocks.Add(new Paragraph(new Run(block.Text))
                    {
                        FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = strong,
                        Margin = new Thickness(0, 18, 0, 6), KeepWithNext = true
                    });
                    break;
                case Core.TermsBlockKind.Bullet:
                    bullets ??= AddBulletList(document);
                    bullets.ListItems.Add(new ListItem(new Paragraph(new Run(block.Text)) { LineHeight = 22, Margin = new Thickness(0, 0, 0, 4) }));
                    break;
                default:
                    document.Blocks.Add(new Paragraph(new Run(block.Text)) { LineHeight = 22, Margin = new Thickness(0, 0, 0, 10) });
                    break;
            }
        }
        var reader = new RichTextBox(document)
        {
            IsReadOnly = true, IsReadOnlyCaretVisible = true, IsDocumentEnabled = false,
            Background = System.Windows.Media.Brushes.Transparent, Foreground = text,
            BorderThickness = new Thickness(0), Padding = new Thickness(0),
            SelectionBrush = TermsBrush(0x0A, 0x84, 0xFF), SelectionOpacity = 0.55,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        System.Windows.Automation.AutomationProperties.SetName(reader, automationName);
        return reader;
    }

    private static List AddBulletList(FlowDocument document)
    {
        var list = new List
        {
            MarkerStyle = TextMarkerStyle.Disc, MarkerOffset = 8, Padding = new Thickness(26, 0, 0, 0),
            Margin = new Thickness(0, 0, 0, 8)
        };
        document.Blocks.Add(list);
        return list;
    }

    private static System.Windows.Media.SolidColorBrush TermsBrush(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static ResourceDictionary? _termsStyles;
    private static ResourceDictionary TermsStyles => _termsStyles ??= (ResourceDictionary)System.Windows.Markup.XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style x:Key="TermsButton" TargetType="Button">
            <Setter Property="Background" Value="#2C2C31"/>
            <Setter Property="Foreground" Value="#F2F2F7"/>
            <Setter Property="BorderBrush" Value="#33FFFFFF"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="18,8"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Button">
                  <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                          BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="8">
                    <Grid>
                      <Border x:Name="Shade" CornerRadius="7" Background="White" Opacity="0"/>
                      <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                    </Grid>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Shade" Property="Opacity" Value="0.10"/></Trigger>
                    <Trigger Property="IsPressed" Value="True"><Setter TargetName="Shade" Property="Opacity" Value="0.04"/></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Bd" Property="BorderBrush" Value="#FF8FC4FF"/></Trigger>
                    <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="TermsPrimaryButton" TargetType="Button" BasedOn="{StaticResource TermsButton}">
            <Setter Property="Background" Value="#0A84FF"/>
            <Setter Property="Foreground" Value="White"/>
            <Setter Property="BorderBrush" Value="#0A84FF"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
          </Style>
          <Style x:Key="TermsTab" TargetType="RadioButton">
            <Setter Property="Foreground" Value="#A1A1AA"/>
            <Setter Property="FontSize" Value="14"/>
            <Setter Property="Padding" Value="4,6"/>
            <Setter Property="Margin" Value="0,0,18,0"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                  <Border x:Name="Bd" Background="Transparent" BorderBrush="Transparent" BorderThickness="0,0,0,2" Padding="{TemplateBinding Padding}">
                    <ContentPresenter HorizontalAlignment="Left" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter Property="Foreground" Value="#F2F2F7"/></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Bd" Property="Background" Value="#18FFFFFF"/></Trigger>
                    <Trigger Property="IsChecked" Value="True">
                      <Setter Property="Foreground" Value="White"/>
                      <Setter TargetName="Bd" Property="BorderBrush" Value="#0A84FF"/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style x:Key="TermsScrollThumb" TargetType="Thumb">
            <Setter Property="OverridesDefaultStyle" Value="True"/>
            <Setter Property="IsTabStop" Value="False"/>
            <Setter Property="Template">
              <Setter.Value>
                <ControlTemplate TargetType="Thumb">
                  <Border x:Name="ThumbBorder" Background="#45FFFFFF" CornerRadius="3" Margin="2,0"/>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ThumbBorder" Property="Background" Value="#75FFFFFF"/></Trigger>
                    <Trigger Property="IsDragging" Value="True"><Setter TargetName="ThumbBorder" Property="Background" Value="#99FFFFFF"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </Setter.Value>
            </Setter>
          </Style>
          <Style TargetType="ScrollBar">
            <Setter Property="Background" Value="Transparent"/>
            <Style.Triggers>
              <Trigger Property="Orientation" Value="Vertical">
                <Setter Property="Width" Value="9"/>
                <Setter Property="MinWidth" Value="9"/>
                <Setter Property="Margin" Value="2,6,4,6"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="ScrollBar">
                      <Grid Background="Transparent">
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                          <Track.Thumb><Thumb Style="{StaticResource TermsScrollThumb}"/></Track.Thumb>
                        </Track>
                      </Grid>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Trigger>
            </Style.Triggers>
          </Style>
        </ResourceDictionary>
        """);

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
