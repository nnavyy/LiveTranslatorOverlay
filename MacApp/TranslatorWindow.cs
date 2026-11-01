using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Localization;
using LiveTranslatorOverlay.Core.Translation;
using LiveTranslatorOverlay.Core.Update;

namespace LiveTranslatorMac;

public sealed class TranslatorWindow : Window
{
    private readonly TextBox _source = new() { AcceptsReturn = true, Height = 110, MaxLength = 4000, PlaceholderText = "Enter text to translate", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _result = new() { IsReadOnly = true, AcceptsReturn = true, MinHeight = 90, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _historyText = new() { IsReadOnly = true, AcceptsReturn = true, Height = 150, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBox _key = new() { PasswordChar = '●', PlaceholderText = "Groq API key — kept only for this session" };
    private readonly ComboBox _sourceLang = new() { MinWidth = 220 };
    private readonly ComboBox _targetLang = new() { MinWidth = 220 };
    private readonly ComboBox _engine = new() { ItemsSource = new[] { "Google Translate (no key)", "Groq AI (API key required)" }, SelectedIndex = 0, MinWidth = 280 };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _translate = new() { Content = "Translate" };
    private readonly TranslationHistory _history = new();
    private readonly GoogleFreeTranslateProvider _google = new();
    private GroqTranslateProvider? _groq;
    private string _sessionKey = "";
    private CancellationTokenSource? _request;
    private int _historyGeneration;
    private bool _closed;

    public TranslatorWindow()
    {
        Title = $"Live Translator — Mac preview {AppVersion.DisplayString}";
        Width = 850; Height = 800; MinWidth = 600; MinHeight = 520;
        _sourceLang.Items.Add(new ComboBoxItem { Content = "Auto detect", Tag = "auto" });
        foreach (var language in LanguageCatalog.Names.OrderBy(x => x.Value))
        {
            _sourceLang.Items.Add(new ComboBoxItem { Content = language.Value, Tag = language.Key });
            _targetLang.Items.Add(new ComboBoxItem { Content = language.Value, Tag = language.Key });
        }
        _sourceLang.SelectedIndex = 0;
        _targetLang.SelectedItem = _targetLang.Items.Cast<ComboBoxItem>().First(x => (string?)x.Tag == "en");
        _key.IsVisible = false;
        _engine.SelectionChanged += (_, _) => _key.IsVisible = _engine.SelectedIndex == 1;
        _translate.Click += async (_, _) => await TranslateAsync();
        var clear = new Button { Content = "Clear history" };
        clear.Click += (_, _) => { _historyGeneration++; _history.Clear(); _historyText.Text = ""; };
        var save = new Button { Content = "Save history…" };
        save.Click += async (_, _) => await SaveHistoryAsync();
        var report = new Button { Content = "Report an issue…" };
        report.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/nnavyy/LiveTranslatorOverlay/issues/new") { UseShellExecute = true }); }
            catch (Exception ex) { _status.Text = $"Could not open the issue form: {ex.Message}"; }
        };
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "Live Translator", FontSize = 28, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        content.Children.Add(new TextBlock { Text = "Mac preview: text translation and session history. Audio, screen OCR, and capture privacy are not available in this preview.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _sourceLang, _targetLang } });
        content.Children.Add(_engine); content.Children.Add(_key);
        content.Children.Add(new TextBlock { Text = "Text goes to cloud translation services. Google/MyMemory may be used as fallbacks. Service limits apply.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        content.Children.Add(_source); content.Children.Add(_translate); content.Children.Add(_result); content.Children.Add(_status);
        content.Children.Add(new TextBlock { Text = "Translation history", FontSize = 18 });
        content.Children.Add(_historyText);
        content.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { clear, save, report } });
        Content = new ScrollViewer { Content = content };
        Closing += (_, _) => { _closed = true; _request?.Cancel(); };
    }

    private async Task TranslateAsync()
    {
        string source = _source.Text?.Trim() ?? "";
        if (source.Length == 0) { _status.Text = "Enter text first."; return; }
        bool useGroq = _engine.SelectedIndex == 1;
        if (useGroq && string.IsNullOrWhiteSpace(_key.Text)) { _status.Text = "Enter a Groq API key first."; return; }
        if (useGroq)
        {
            string key = _key.Text!.Trim();
            _groq ??= new GroqTranslateProvider(key);
            if (key != _sessionKey) { _groq.UpdateApiKey(key); _sessionKey = key; }
        }
        ITranslationProvider provider = useGroq ? _groq! : _google;
        string sourceTag = ((ComboBoxItem)_sourceLang.SelectedItem!).Tag!.ToString()!;
        string targetTag = ((ComboBoxItem)_targetLang.SelectedItem!).Tag!.ToString()!;
        int generation = _historyGeneration;
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        _request = request;
        _translate.IsEnabled = false; _status.Text = "Translating…";
        _sourceLang.IsEnabled = _targetLang.IsEnabled = _engine.IsEnabled = _key.IsEnabled = false;
        try
        {
            string translated = await provider.TranslateAsync(source, targetTag, sourceTag).WaitAsync(request.Token);
            if (_closed) return;
            _result.Text = translated;
            if (generation == _historyGeneration) { _history.Add(source, translated); _historyText.Text = _history.ToString(); }
            _status.Text = "Translation complete. Unchanged text can also indicate that a service was unavailable; check important results.";
        }
        catch (OperationCanceledException) { if (!_closed) _status.Text = "Translation timed out. Try again."; }
        catch (Exception ex) { if (!_closed) _status.Text = $"Translation failed: {ex.Message}"; }
        finally
        {
            _request = null;
            if (!_closed) _translate.IsEnabled = _sourceLang.IsEnabled = _targetLang.IsEnabled = _engine.IsEnabled = _key.IsEnabled = true;
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
            _status.Text = "History saved.";
        }
        catch (Exception ex) { _status.Text = $"Could not save history: {ex.Message}"; }
    }
}
