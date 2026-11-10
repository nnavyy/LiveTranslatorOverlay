using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Localization;
using LiveTranslatorOverlay.Core.Translation;

namespace LiveTranslatorMac;

public sealed partial class TranslatorWindow : Window
{
    private readonly TextBox _source = new() { AcceptsReturn = true, MaxLength = 4000, PlaceholderText = "Type or paste text…", TextWrapping = TextWrapping.Wrap, Classes = { "editor" } };
    private readonly TextBox _result = new() { IsReadOnly = true, AcceptsReturn = true, PlaceholderText = "Your translation appears here", TextWrapping = TextWrapping.Wrap, Classes = { "editor" } };
    private readonly TextBox _historyText = new() { IsReadOnly = true, AcceptsReturn = true, Height = 180, PlaceholderText = "Translations from this session appear here.", TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _key = new() { PasswordChar = '●', PlaceholderText = "Groq API key — kept only for this session" };
    private readonly ComboBox _sourceLang = new();
    private readonly ComboBox _targetLang = new();
    private readonly ComboBox _engine = new() { ItemsSource = new[] { "Google Translate", "Groq AI" }, SelectedIndex = 0 };
    private readonly TextBlock _status = new() { IsVisible = false, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } };
    private readonly TextBlock _providerNote = new() { Text = "Google Translate · Text is sent to cloud services.", TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } };
    private readonly Button _translate = new() { Content = "Translate", Classes = { "primary" } };
    private readonly Button _copy = new() { Content = "Copy translation", IsEnabled = false };
    private readonly Button _clear = new() { Content = "Clear", IsEnabled = false };
    private readonly Button _save = new() { Content = "Export…", IsEnabled = false };
    private readonly Expander _settings = new() { Header = "Translation settings" };
    private readonly TranslationHistory _history = new();
    private readonly GoogleFreeTranslateProvider _google = new();
    private GroqTranslateProvider? _groq;
    private string _sessionKey = "";
    private CancellationTokenSource? _request;
    private int _historyGeneration;
    private bool _closed;

    public TranslatorWindow()
    {
        Title = "Live Translator";
        Width = 920; Height = 720; MinWidth = 640; MinHeight = 560;
        _sourceLang.Items.Add(new ComboBoxItem { Content = "Auto detect", Tag = "auto" });
        foreach (var language in LanguageCatalog.Names.OrderBy(x => x.Value))
        {
            _sourceLang.Items.Add(new ComboBoxItem { Content = language.Value, Tag = language.Key });
            _targetLang.Items.Add(new ComboBoxItem { Content = language.Value, Tag = language.Key });
        }
        _sourceLang.SelectedIndex = 0;
        _targetLang.SelectedItem = _targetLang.Items.Cast<ComboBoxItem>().First(x => (string?)x.Tag == "en");
        _key.IsVisible = false;
        _engine.SelectionChanged += (_, _) =>
        {
            _key.IsVisible = _engine.SelectedIndex == 1;
            _providerNote.Text = $"{(_engine.SelectedIndex == 1 ? "Groq AI" : "Google Translate")} · Text is sent to cloud services.";
        };
        _translate.Click += async (_, _) => await TranslateAsync();
        _clear.Click += (_, _) =>
        {
            _historyGeneration++; _history.Clear(); _historyText.Text = "";
            _clear.IsEnabled = _save.IsEnabled = false;
            SetStatus("Session history cleared.");
        };
        _save.Click += async (_, _) => await SaveHistoryAsync();
        _copy.Click += async (_, _) =>
        {
            if (string.IsNullOrEmpty(_result.Text)) return;
            try
            {
                if (Clipboard is not { } clipboard) { SetStatus("Clipboard is unavailable."); return; }
                await clipboard.SetTextAsync(_result.Text);
                SetStatus("Translation copied.");
            }
            catch (Exception ex) { SetStatus($"Could not copy translation: {ex.Message}"); }
        };
        var report = new Button { Content = "Report an issue…" };
        report.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/nnavyy/LiveTranslatorOverlay/issues/new") { UseShellExecute = true }); }
            catch (Exception ex) { SetStatus($"Could not open the issue form: {ex.Message}"); }
        };

        AutomationProperties.SetName(_sourceLang, "Source language");
        AutomationProperties.SetName(_targetLang, "Target language");
        AutomationProperties.SetName(_source, "Text to translate");
        AutomationProperties.SetName(_result, "Translation");
        AutomationProperties.SetName(_engine, "Translation service");
        AutomationProperties.SetName(_key, "Groq API key");
        AutomationProperties.SetName(_historyText, "Session history");
        AutomationProperties.SetName(_clear, "Clear session history");
        AutomationProperties.SetName(_save, "Export session history");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        ToolTip.SetTip(_translate, OperatingSystem.IsMacOS() ? "Translate (⌘ Return)" : "Translate (Ctrl+Enter)");

        var content = new StackPanel { Margin = new Thickness(28), Spacing = 16, MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.Children.Add(new TextBlock { Text = "Translate", FontSize = 24, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4) });

        var editors = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1,*") };
        var sourcePane = CreateEditorPane("From", _sourceLang, _source);
        var targetPane = CreateEditorPane("To", _targetLang, _result);
        var divider = new Border { Classes = { "divider" } };
        Grid.SetColumn(divider, 1); Grid.SetColumn(targetPane, 2);
        editors.Children.Add(sourcePane); editors.Children.Add(divider); editors.Children.Add(targetPane);
        content.Children.Add(new Border { Classes = { "workspace" }, Child = editors });

        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 };
        _providerNote.VerticalAlignment = VerticalAlignment.Center;
        _providerNote.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(_copy, 1); Grid.SetColumn(_translate, 2);
        actions.Children.Add(_providerNote); actions.Children.Add(_copy); actions.Children.Add(_translate);
        content.Children.Add(actions);
        content.Children.Add(CreateAudioControls());
        content.Children.Add(_status);

        var settings = new StackPanel { Spacing = 12, Margin = new Thickness(16, 8, 16, 16), Children =
        {
            new TextBlock { Text = "Translation service", Classes = { "secondary" } }, _engine, _key,
            new TextBlock { Text = "Google Translate needs no key. Groq needs your API key, kept only for this session. Google or MyMemory may be used as fallbacks. Service limits apply.", TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } }
        } };
        _settings.Content = settings;
        content.Children.Add(_settings);
        content.Children.Add(new Expander { Header = "Session history", Content = new StackPanel
        {
            Spacing = 12, Margin = new Thickness(16, 8, 16, 16), Children =
            {
                _historyText,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { _clear, _save } }
            }
        } });
        var about = new Button { Content = "About & feedback", HorizontalAlignment = HorizontalAlignment.Left };
        var terms = new Button { Content = "Terms and privacy" };
        terms.Click += async (_, _) => await MacApplication.CreateTermsWindow().ShowDialog(this);
        about.Flyout = new Flyout { Content = new StackPanel
        {
            Spacing = 12, MaxWidth = 340, Children =
            {
                new TextBlock { Text = "Live Translator", FontSize = 18, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = $"Live Translator {Program.DisplayVersion} for Mac. Translate text, system audio, or microphone input. Session history stays in memory unless you export it.", TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } },
                report, terms
            }
        } };
        content.Children.Add(about);
        Content = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        KeyDown += async (_, e) =>
        {
            var shortcut = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            if (e.Key == Key.Enter && e.KeyModifiers == shortcut && _translate.IsEnabled)
            {
                e.Handled = true;
                await TranslateAsync();
            }
        };
        Closing += (_, _) => { _closed = true; _request?.Cancel(); _audioRequest?.Cancel(); _usageClock.Stop(); };
    }

    private static Grid CreateEditorPane(string label, ComboBox language, TextBox editor)
    {
        var pane = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 12, Margin = new Thickness(20), MinWidth = 0 };
        pane.Children.Add(new TextBlock { Text = label, Classes = { "secondary" } });
        Grid.SetRow(language, 1); Grid.SetRow(editor, 2);
        editor.Height = 220;
        pane.Children.Add(language); pane.Children.Add(editor);
        return pane;
    }

    private void SetStatus(string text)
    {
        _status.Text = text;
        _status.IsVisible = text.Length > 0;
    }

    private ITranslationProvider? GetTranslationProvider()
    {
        bool useGroq = _engine.SelectedIndex == 1;
        if (useGroq && string.IsNullOrWhiteSpace(_key.Text))
        {
            _settings.IsExpanded = true;
            SetStatus("Add your Groq API key in Translation settings."); _key.Focus();
            return null;
        }
        if (useGroq)
        {
            string key = _key.Text!.Trim();
            _groq ??= new GroqTranslateProvider(key);
            if (key != _sessionKey) { _groq.UpdateApiKey(key); _sessionKey = key; }
        }
        return useGroq ? _groq : _google;
    }

    private void AddToHistory(string source, string translated)
    {
        _history.Add(source, translated);
        _historyText.Text = _history.ToString();
        _clear.IsEnabled = _save.IsEnabled = !string.IsNullOrEmpty(_historyText.Text);
    }

    private async Task TranslateAsync()
    {
        string source = _source.Text?.Trim() ?? "";
        if (source.Length == 0) { SetStatus("Enter text to translate."); _source.Focus(); return; }
        var provider = GetTranslationProvider();
        if (provider == null) return;
        string sourceTag = ((ComboBoxItem)_sourceLang.SelectedItem!).Tag!.ToString()!;
        string targetTag = ((ComboBoxItem)_targetLang.SelectedItem!).Tag!.ToString()!;
        int generation = _historyGeneration;
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        _request = request;
        _translate.IsEnabled = false; _translate.Content = "Translating…"; SetStatus("");
        _sourceLang.IsEnabled = _targetLang.IsEnabled = _engine.IsEnabled = _key.IsEnabled = _source.IsEnabled = false;
        try
        {
            string translated = await provider.TranslateAsync(source, targetTag, sourceTag).WaitAsync(request.Token);
            if (_closed) return;
            _result.Text = translated;
            _copy.IsEnabled = !string.IsNullOrEmpty(translated);
            if (generation == _historyGeneration) AddToHistory(source, translated);
            SetStatus(string.IsNullOrWhiteSpace(translated) || source == translated.Trim()
                ? "The service returned no new translation. Check the language selection or try again."
                : "Translation complete. Check important results.");
        }
        catch (OperationCanceledException) { if (!_closed) SetStatus("Translation timed out. Try again."); }
        catch (Exception ex) { if (!_closed) SetStatus($"Translation failed: {ex.Message}"); }
        finally
        {
            _request = null;
            if (!_closed)
            {
                _translate.Content = "Translate";
                _translate.IsEnabled = _sourceLang.IsEnabled = _targetLang.IsEnabled = _engine.IsEnabled = _key.IsEnabled = _source.IsEnabled = true;
            }
        }
    }

    private async Task SaveHistoryAsync()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save translation history", SuggestedFileName = "translation-history.txt" });
            if (file == null) return;
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(_history.ToString());
            SetStatus("History exported.");
        }
        catch (Exception ex) { SetStatus($"Could not export history: {ex.Message}"); }
    }
}
