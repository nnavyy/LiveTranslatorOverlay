using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LiveTranslatorOverlay.Core;

namespace LiveTranslatorMac;

public sealed partial class TranslatorWindow
{
    private readonly ComboBox _audioSource = new() { ItemsSource = new[] { "System audio", "Microphone" }, SelectedIndex = 0 };
    private readonly ComboBox _speechModel = new() { ItemsSource = new[] { "Balanced", "Larger model" }, SelectedIndex = 0 };
    private readonly Button _audioToggle = new() { Content = "Start audio" };
    private CancellationTokenSource? _audioRequest;

    private Control CreateAudioControls()
    {
        _audioToggle.Click += async (_, _) =>
        {
            if (_audioRequest != null) { _audioRequest.Cancel(); return; }
            await TranslateAudioAsync();
        };
        Avalonia.Automation.AutomationProperties.SetName(_audioSource, "Audio source");
        Avalonia.Automation.AutomationProperties.SetName(_speechModel, "Local speech model");
        return new StackPanel
        {
            Spacing = 8, Children =
            {
                new TextBlock { Text = "Audio translation", FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = "macOS asks for microphone or system audio permission when you start. Audio stays on this Mac. The selected translation service receives text.",
                    TextWrapping = TextWrapping.Wrap, Classes = { "secondary" }
                },
                new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { _audioSource, _speechModel, _audioToggle } }
            }
        };
    }

    private async Task TranslateAudioAsync()
    {
        if (_request != null || !OperatingSystem.IsMacOS()) return;
        var provider = GetTranslationProvider();
        if (provider == null) return;
        string sourceTag = ((ComboBoxItem)_sourceLang.SelectedItem!).Tag!.ToString()!;
        string targetTag = ((ComboBoxItem)_targetLang.SelectedItem!).Tag!.ToString()!;
        var microphone = _audioSource.SelectedIndex == 1;
        using var request = new CancellationTokenSource();
        _audioRequest = request;
        _audioToggle.Content = "Stop audio";
        _sourceLang.IsEnabled = _targetLang.IsEnabled = _engine.IsEnabled = _key.IsEnabled =
            _translate.IsEnabled = _audioSource.IsEnabled = _speechModel.IsEnabled = false;
        var captions = new TextBlock { Text = "Waiting for speech…", TextWrapping = TextWrapping.Wrap, FontSize = 20, Foreground = Brushes.White };
        var frame = new Border { Background = new SolidColorBrush(Color.FromRgb(26, 27, 31)), Padding = new Thickness(18), Child = captions };
        var overlay = new Window
        {
            Title = "Live captions", Width = 560, SizeToContent = SizeToContent.Height, MaxHeight = 300,
            CanResize = false, Topmost = true, ShowActivated = false, ShowInTaskbar = false,
            WindowDecorations = WindowDecorations.None, Content = frame
        };
        frame.PointerPressed += (_, e) => { if (e.GetCurrentPoint(frame).Properties.IsLeftButtonPressed) overlay.BeginMoveDrag(e); };
        SetStatus("Preparing the local speech model…");
        try
        {
            await using var speech = new LocalSpeechService(small: _speechModel.SelectedIndex == 1);
            await speech.InitializeAsync(sourceTag, request.Token, message => Dispatcher.UIThread.Post(() =>
            {
                if (!_closed && _audioRequest == request && !request.IsCancellationRequested) SetStatus(message);
            }));
            await using var capture = new MacAudioCapture();
            SetStatus("Starting audio capture…");
            await capture.StartAsync(microphone, request.Token);
            request.Token.ThrowIfCancellationRequested();
            overlay.Show(this);
            SetStatus("Audio translation is running. Drag the captions to reposition them.");
            using var processing = CancellationTokenSource.CreateLinkedTokenSource(request.Token);
            var processingToken = processing.Token;
            // Stop ongoing recognition or network work as soon as native capture stops.
            async Task WatchCaptureAsync()
            {
                try { await capture.Stopped.WaitAsync(request.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                processing.Cancel();
            }
            var watchCapture = WatchCaptureAsync();
            try
            {
                await foreach (var samples in capture.Chunks.ReadAllAsync(processingToken))
                {
                    var original = await Task.Run(() => speech.TranscribeAsync(samples, processingToken), processingToken);
                    if (string.IsNullOrWhiteSpace(original)) continue;
                    int generation = _historyGeneration;
                    var translated = await provider.TranslateAsync(original, targetTag, sourceTag)
                        .WaitAsync(TimeSpan.FromSeconds(40), processingToken);
                    if (_closed || processingToken.IsCancellationRequested || string.IsNullOrWhiteSpace(translated)) continue;
                    captions.Text = original + "\n" + translated;
                    if (generation == _historyGeneration) AddToHistory(original, translated);
                }
            }
            catch (OperationCanceledException) when (processingToken.IsCancellationRequested) { }
            finally { request.Cancel(); await watchCapture; }
            if (!_closed) SetStatus(capture.Error ?? "Audio translation stopped.");
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { if (!_closed) SetStatus("Audio translation stopped."); }
        catch (Exception ex) { if (!_closed) SetStatus($"Could not translate audio: {ex.Message}"); }
        finally
        {
            if (overlay.IsVisible) overlay.Close();
            _audioRequest = null;
            _audioToggle.Content = "Start audio";
            _sourceLang.IsEnabled = _targetLang.IsEnabled = _engine.IsEnabled = _key.IsEnabled =
                _translate.IsEnabled = _audioSource.IsEnabled = _speechModel.IsEnabled = true;
        }
    }
}
