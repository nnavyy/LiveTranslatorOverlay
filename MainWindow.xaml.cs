using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Translation;
using LiveTranslatorOverlay.Core.Localization;
using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace LiveTranslatorOverlay;

public partial class MainWindow : Window
{
    private Point _startPoint;
    private bool _isDrawing;
    private Rect _selectedOcrRegion;
    private bool _isLiveMode = false;
    private CancellationTokenSource? _liveOcrCts;
    private ScreenOcrService _ocrService;
    private ITranslationProvider _translationProvider;
    private string _lastOcrText = string.Empty;

    // Audio STT
    private AudioCaptureService? _audioCaptureService;
    private WhisperSttService? _whisperSttService;
    private DiarizedSttService? _diarizedSttService;
    private bool _isAudioMode = false;
    private CancellationTokenSource? _audioCts;
    // Semaphore ensures only one translation runs at a time (prevents queued-up delays)
    private readonly SemaphoreSlim _translateSemaphore = new SemaphoreSlim(1, 1);

    // Speaker diarization colors (Deepgram mode)
    private static readonly string[] SpeakerColors = { "#00FF88", "#88BBFF", "#FFDD44", "#FF88CC", "#BB88FF" };

    // Extras
    private HistoryWindow? _historyWindow;
    private readonly System.Text.StringBuilder _historyBuffer = new();

    public void ClearHistoryBuffer()
    {
        lock (_historyBuffer)
        {
            _historyBuffer.Clear();
        }
    }

    private void AppendToHistory(string sourceText, string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText)) return;
        string entry = $"[{DateTime.Now:HH:mm:ss}]\r\nOriginal: {sourceText}\r\nTranslated: {translatedText}\r\n----------------------------------------\r\n\r\n";
        lock (_historyBuffer)
        {
            _historyBuffer.Append(entry);
        }
        Dispatcher.Invoke(() =>
        {
            if (_historyWindow != null && _historyWindow.IsVisible)
            {
                _historyWindow.AppendText(sourceText, translatedText);
            }
        });
    }

    // UI Animations & Timers
    private DispatcherTimer _subtitleHideTimer;
    private Storyboard _fadeInStoryboard;
    private Storyboard _fadeOutStoryboard;
    private bool _isCaptionVisible = false;

    private string _savedAudioDeviceName = "";

    public MainWindow()
    {
        InitializeComponent();
        _ocrService = new ScreenOcrService();
        
        var loadedSettings = LiveTranslatorOverlay.Core.AppSettings.Load();
        TxtApiKey.Text = loadedSettings.GroqApiKey;
        TxtDeepgramKey.Text = loadedSettings.DeepgramApiKey;
        if (loadedSettings.SourceLangIndex >= 0 && loadedSettings.SourceLangIndex < CmbSourceLang.Items.Count) CmbSourceLang.SelectedIndex = loadedSettings.SourceLangIndex;
        if (loadedSettings.TargetLangIndex >= 0 && loadedSettings.TargetLangIndex < CmbTargetLang.Items.Count) CmbTargetLang.SelectedIndex = loadedSettings.TargetLangIndex;
        if (loadedSettings.EngineIndex >= 0 && loadedSettings.EngineIndex < CmbEngine.Items.Count) CmbEngine.SelectedIndex = loadedSettings.EngineIndex;
        if (loadedSettings.SttModeIndex >= 0 && loadedSettings.SttModeIndex < CmbSttMode.Items.Count) CmbSttMode.SelectedIndex = loadedSettings.SttModeIndex;
        if (loadedSettings.FontSizeIndex >= 0 && loadedSettings.FontSizeIndex < CmbFontSize.Items.Count) CmbFontSize.SelectedIndex = loadedSettings.FontSizeIndex;
        _savedAudioDeviceName = loadedSettings.AudioDeviceName;

        // UI Language setup (English default, can follow system default)
        if (loadedSettings.UiLanguageIndex >= 0 && loadedSettings.UiLanguageIndex < CmbUiLanguage.Items.Count)
        {
            CmbUiLanguage.SelectedIndex = loadedSettings.UiLanguageIndex;
        }
        else
        {
            CmbUiLanguage.SelectedIndex = 0; // Default: English
        }
        if (ChkHideAllWindows != null)
        {
            ChkHideAllWindows.IsChecked = loadedSettings.HideFromCapture;
        }

        LocalizationManager.Instance.SetLanguage(loadedSettings.UiLanguage);
        LocalizationManager.Instance.LanguageChanged += (s, e) => Dispatcher.Invoke(ApplyLocalization);
        ApplyLocalization();
        
        // Initialize with Google Free as default (matches SelectedIndex=0 in XAML)
        if (loadedSettings.EngineIndex == 1 && !string.IsNullOrWhiteSpace(TxtApiKey.Text))
            _translationProvider = new GroqTranslateProvider(TxtApiKey.Text);
        else
            _translationProvider = new GoogleFreeTranslateProvider();

        QuotaTracker.Instance.QuotaChanged += (s, info) =>
        {
            Dispatcher.Invoke(() => UpdateQuotaUi(info));
        };

        CaptureExclusion.Initialize();
        CaptureExclusion.ScreenshotOccurred += () => Dispatcher.Invoke(() => this.Opacity = 0);
        CaptureExclusion.ScreenshotFinished += () => Dispatcher.Invoke(() => this.Opacity = 1);

        this.Closing += MainWindow_Closing;

        this.Loaded += async (s, e) =>
        {
            var loadedSettings = LiveTranslatorOverlay.Core.AppSettings.Load();
            CaptureExclusion.ApplyToAllWindows(loadedSettings.HideFromCapture);

            if (loadedSettings.EngineIndex == 1)
                QuotaTracker.Instance.NotifyCurrentGroq();
            else
                QuotaTracker.Instance.SetUnlimited("Google Translate");

            if (loadedSettings.CaptionLeft != -1 && loadedSettings.CaptionTop != -1)
            {
                double maxAllowedLeft = this.ActualWidth - 400 - 20;
                double safeLeft = Math.Max(20, Math.Min(loadedSettings.CaptionLeft, maxAllowedLeft));
                double safeTop = Math.Max(20, Math.Min(loadedSettings.CaptionTop, this.ActualHeight - 60)); // Approximate height
                Canvas.SetLeft(CaptionBar, safeLeft);
                Canvas.SetTop(CaptionBar, safeTop);
                CaptionBar.MaxWidth = Math.Max(400, Math.Min(900, this.ActualWidth - safeLeft - 20));
            }
            else
            {
                double defaultLeft = (this.ActualWidth - 400) / 2;
                Canvas.SetLeft(CaptionBar, defaultLeft);
                Canvas.SetTop(CaptionBar, this.ActualHeight - 140); // Lifted slightly for better UX
                CaptionBar.MaxWidth = Math.Max(400, Math.Min(900, this.ActualWidth - defaultLeft - 20));
            }
            
            // Populate processes synchronously first so UI doesn't reset if user interacts early
            RefreshApplications();
            
            if (CmbAudioDevice.Items.Count > 0)
            {
                bool found = false;
                for (int i = 0; i < CmbAudioDevice.Items.Count; i++)
                {
                    var item = CmbAudioDevice.Items[i] as System.Windows.Controls.ComboBoxItem;
                    if (item?.Content?.ToString() == _savedAudioDeviceName)
                    {
                        CmbAudioDevice.SelectedIndex = i;
                        found = true;
                        break;
                    }
                }
                if (!found) CmbAudioDevice.SelectedIndex = 0;
            }
            
            // Auto-populate model list from Groq
            await RefreshGroqModelsAsync();

            _fadeInStoryboard = (Storyboard)this.Resources["FadeInCaption"];
            _fadeOutStoryboard = (Storyboard)this.Resources["FadeOutCaption"];
        };
        
        // Timer for auto-hiding subtitle (Disabled per user request for STT)
        _subtitleHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _subtitleHideTimer.Tick += (s, e) => 
        {
            _subtitleHideTimer.Stop();
            // Disabled auto fade-out for Audio mode
            // if (_isCaptionVisible && _isAudioMode)
            // {
            //     _fadeOutStoryboard.Begin(this);
            //     _isCaptionVisible = false;
            // }
        };
        
        // Pastikan container tidak meluber ke bawah layar
        TranslatedTextContainer.SizeChanged += (s, e) =>
        {
            if (TranslatedTextContainer.Visibility == Visibility.Visible && _selectedOcrRegion.Height > 0)
            {
                double currentTop = Canvas.GetTop(TranslatedTextContainer);
                double bottomEdge = currentTop + TranslatedTextContainer.ActualHeight;
                
                // Jika melewati batas bawah layar (sisakan margin 30px)
                if (bottomEdge > this.ActualHeight - 30)
                {
                    // Pindah ke atas kotak seleksi
                    double newTop = Math.Max(10, _selectedOcrRegion.Top - TranslatedTextContainer.ActualHeight - 5);
                    Canvas.SetTop(TranslatedTextContainer, newTop);
                }
            }
        };
        
        TxtApiKey.TextChanged += (s, e) => UpdateEngine();
    }
    
        private bool _isDraggingCaption = false;
    private Point _captionDragStartPoint;

    private void CaptionBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingCaption = true;
        _captionDragStartPoint = e.GetPosition(CaptionBar);
        CaptionBar.CaptureMouse();
        e.Handled = true;
    }

    private void CaptionBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingCaption)
        {
            var pos = e.GetPosition(OverlayCanvas);
            double newLeft = pos.X - _captionDragStartPoint.X;
            double newTop = pos.Y - _captionDragStartPoint.Y;
            
            // The box must always have at least 400px of space to grow on the right
            double maxAllowedLeft = this.ActualWidth - 400 - 20; 
            
            newLeft = Math.Max(20, Math.Min(newLeft, maxAllowedLeft));
            newTop = Math.Max(20, Math.Min(newTop, this.ActualHeight - CaptionBar.ActualHeight - 20));

            Canvas.SetLeft(CaptionBar, newLeft);
            Canvas.SetTop(CaptionBar, newTop);
            
            // Dynamically constrain the MaxWidth so it wraps instead of going off-screen
            CaptionBar.MaxWidth = Math.Max(400, Math.Min(900, this.ActualWidth - newLeft - 20));
            
            e.Handled = true;
        }
    }

    private void CaptionBar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingCaption)
        {
            _isDraggingCaption = false;
            CaptionBar.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void CmbFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CaptionTranslated != null && CmbFontSize.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            if (int.TryParse(item.Tag?.ToString(), out int size))
            {
                CaptionTranslated.FontSize = size;
                CaptionOriginal.FontSize = Math.Max(12, size - 6);
            }
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CaptureExclusion.Shutdown();

        var settings = new LiveTranslatorOverlay.Core.AppSettings
        {
            GroqApiKey = TxtApiKey.Text,
            DeepgramApiKey = TxtDeepgramKey.Text,
            SourceLangIndex = CmbSourceLang.SelectedIndex,
            TargetLangIndex = CmbTargetLang.SelectedIndex,
            EngineIndex = CmbEngine.SelectedIndex,
            SttModeIndex = CmbSttMode.SelectedIndex,
            FontSizeIndex = CmbFontSize.SelectedIndex,
            UiLanguage = LocalizationManager.Instance.ConfiguredLanguage,
            UiLanguageIndex = CmbUiLanguage.SelectedIndex,
            AudioDeviceName = (CmbAudioDevice.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "",
            CaptionLeft = Canvas.GetLeft(CaptionBar),
            CaptionTop = Canvas.GetTop(CaptionBar)
        };
        settings.Save();
    }

    private async Task RefreshGroqModelsAsync()
    {
        if (_translationProvider is not GroqTranslateProvider groqProvider) return;
        
        var models = await groqProvider.GetAvailableModelsAsync();
        if (models.Count == 0) return;
        
        CmbGroqModel.Items.Clear();
        CmbGroqModel.Items.Add(new ComboBoxItem { Content = "(Otomatis)", Tag = "" });
        
        foreach (var m in models)
        {
            CmbGroqModel.Items.Add(new ComboBoxItem { Content = m, Tag = m });
        }
        
        CmbGroqModel.SelectedIndex = 0;
    }
    
    private async void BtnRefreshModels_Click(object sender, RoutedEventArgs e)
    {
        UpdateEngine();
        await RefreshGroqModelsAsync();
    }
    
    private void CmbEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateEngine();
    }
    
    private void UpdateEngine()
    {
        if (CmbEngine == null || TxtApiKey == null) return;
        
        string selectedModel = "";
        if (CmbGroqModel?.SelectedItem is ComboBoxItem modelItem)
            selectedModel = modelItem.Tag?.ToString() ?? "";
        
        if (CmbEngine.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "groq")
        {
            QuotaTracker.Instance.NotifyCurrentGroq();
            if (_translationProvider is GroqTranslateProvider groq)
            {
                groq.UpdateApiKey(TxtApiKey.Text);
                if (!string.IsNullOrEmpty(selectedModel))
                    groq.SetModel(selectedModel);
            }
            else
            {
                var newGroq = new GroqTranslateProvider(TxtApiKey.Text);
                if (!string.IsNullOrEmpty(selectedModel))
                    newGroq.SetModel(selectedModel);
                _translationProvider = newGroq;
            }
        }
        else
        {
            QuotaTracker.Instance.SetUnlimited("Google Translate");
            if (!(_translationProvider is GoogleFreeTranslateProvider))
                _translationProvider = new GoogleFreeTranslateProvider();
        }
    }

    private void UpdateQuotaUi(QuotaInfo info)
    {
        try
        {
            TxtQuotaBadge.Text = info.BadgeText;
            BadgeQuotaStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(info.BadgeBgColor));
            TxtQuotaBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(info.BadgeFgColor));

            PbQuotaRequests.Value = info.ProgressValue;
            PbQuotaRequests.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(info.BadgeFgColor));

            TxtQuotaInfo.Text = info.DisplayMain;
            TxtQuotaInfo.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(info.BadgeFgColor));
        }
        catch { }
    }

    private string GetTargetLanguage()
    {
        if (CmbTargetLang.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? "id";
        return "id";
    }

    private string GetSourceLanguage()
    {
        if (CmbSourceLang.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? "auto";
        return "auto";
    }

    // --- DPI helpers ---
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    private double GetDpiScale()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var monitor = MonitorFromWindow(hwnd, 2);
            GetDpiForMonitor(monitor, 0, out uint dpiX, out _);
            return dpiX / 96.0;
        }
        catch
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        }
    }

    // ===================== SCREEN OCR =====================

    private bool _isDrawingMode = false;

    private void BtnDrawArea_Click(object sender, RoutedEventArgs e)
    {
        if (_isLiveMode) return;
        
        _isDrawingMode = true;
        this.Background = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0));
        OverlayCanvas.Background = Brushes.Transparent;
        
        SelectionRect.Visibility = Visibility.Collapsed;
        TranslatedTextContainer.Visibility = Visibility.Collapsed;
        BtnStartLive.IsEnabled = false;
    }

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawingMode || _isLiveMode) return;

        if (e.OriginalSource is DependencyObject source && IsDescendantOf(ControlPanel, source))
            return;

        _isDrawing = true;
        _startPoint = e.GetPosition(OverlayCanvas);

        Canvas.SetLeft(SelectionRect, _startPoint.X);
        Canvas.SetTop(SelectionRect, _startPoint.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing) return;

        var pos = e.GetPosition(OverlayCanvas);
        var x = Math.Min(pos.X, _startPoint.X);
        var y = Math.Min(pos.Y, _startPoint.Y);
        var w = Math.Max(pos.X, _startPoint.X) - x;
        var h = Math.Max(pos.Y, _startPoint.Y) - y;

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing) return;
        _isDrawing = false;
        _isDrawingMode = false;
        
        // Kembalikan layar agar bisa diklik tembus
        this.Background = null;
        OverlayCanvas.Background = null;

        _selectedOcrRegion = new Rect(
            Canvas.GetLeft(SelectionRect),
            Canvas.GetTop(SelectionRect),
            SelectionRect.Width,
            SelectionRect.Height);

        if (_selectedOcrRegion.Width > 0 && _selectedOcrRegion.Height > 0)
        {
            Canvas.SetLeft(TranslatedTextContainer, _selectedOcrRegion.Left);
            
            // Taruh di bawah by default
            Canvas.SetTop(TranslatedTextContainer, _selectedOcrRegion.Bottom + 5);
            TranslatedTextContainer.MaxHeight = 250; // Batasi tinggi agar tidak menuhi layar
            
            TranslatedTextContainer.Visibility = Visibility.Visible;
            TranslatedTextBlock.Text = "[Area selected. Click 'Start' for OCR]";
            BtnStartLive.IsEnabled = true;
        }
    }

    private async void BtnStartLive_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        if (_selectedOcrRegion.Width == 0 || _selectedOcrRegion.Height == 0)
        {
            MessageBox.Show(loc.Get("OcrSelectPrompt"));
            return;
        }

        if (!_isLiveMode)
        {
            _isLiveMode = true;
            SelectionRect.Stroke = new SolidColorBrush(Colors.Green);
            SelectionRect.Fill = null;
            BtnStartLive.Content = loc.Get("BtnStopOcr");
            BtnStartLive.Background = new SolidColorBrush(Color.FromRgb(255, 165, 0));

            _liveOcrCts = new CancellationTokenSource();
            _ = RunLiveOcrLoopAsync(_liveOcrCts.Token);
        }
        else
        {
            _isLiveMode = false;
            _liveOcrCts?.Cancel();

            SelectionRect.Stroke = new SolidColorBrush(Colors.Red);
            SelectionRect.Fill = new SolidColorBrush(Color.FromArgb(0x20, 255, 0, 0));
            TranslatedTextBlock.Text = loc.Get("OcrStopped");
            BtnStartLive.Content = loc.Get("BtnStartOcr");
            BtnStartLive.ClearValue(Button.BackgroundProperty);
        }
    }

    private async Task RunLiveOcrLoopAsync(CancellationToken token)
    {
        TranslatedTextBlock.Text = LocalizationManager.Instance.Get("OcrStarting");

        double dpiScale = GetDpiScale();
        int screenX = (int)(_selectedOcrRegion.X * dpiScale);
        int screenY = (int)(_selectedOcrRegion.Y * dpiScale);
        int screenW = (int)(_selectedOcrRegion.Width * dpiScale);
        int screenH = (int)(_selectedOcrRegion.Height * dpiScale);

        int emptyCount = 0; // Track consecutive empty scans

        while (!token.IsCancellationRequested)
        {
            try
            {
                string targetLang = GetTargetLanguage();
                string sourceLang = GetSourceLanguage();
                
                _ocrService.ChangeLanguage(sourceLang);
                
                string text = await Task.Run(() => _ocrService.RecognizeRegionAsync(screenX, screenY, screenW, screenH));

                if (!token.IsCancellationRequested && !string.IsNullOrWhiteSpace(text))
                {
                    emptyCount = 0;
                    string cleanText = text.Trim();
                    if (cleanText != _lastOcrText)
                    {
                        _lastOcrText = cleanText;
                        TranslatedTextBlock.Text = "Translating...";

                        Dispatcher.Invoke(() => TranslatedTextBlock.Text = "");
                        
                        string finalTranslation = "";
                        await _translationProvider.TranslateStreamAsync(cleanText, targetLang, sourceLang, chunk => 
                        {
                            finalTranslation = chunk;
                            Dispatcher.Invoke(() =>
                            {
                                if (!token.IsCancellationRequested)
                                    TranslatedTextBlock.Text = chunk;
                            });
                        });

                        if (!token.IsCancellationRequested)
                        {
                            // Sanity check: if result is empty or same as source, show warning
                            if (string.IsNullOrWhiteSpace(finalTranslation) || finalTranslation.Trim() == text.Trim())
                            {
                                finalTranslation = $"[Failed to translate] {text}";
                                Dispatcher.Invoke(() => TranslatedTextBlock.Text = finalTranslation);
                            }

                            Dispatcher.Invoke(() =>
                            {
                                AppendToHistory(text, finalTranslation);
                            });
                        }
                    }
                }
                else if (!token.IsCancellationRequested && string.IsNullOrWhiteSpace(text))
                {
                    emptyCount++;
                    // Only show "tidak ada teks" after 5 consecutive empty scans (~1.5 detik)
                    // This prevents the translated text from disappearing too quickly
                    if (emptyCount >= 5)
                    {
                        TranslatedTextBlock.Text = "[No text detected]";
                        _lastOcrText = string.Empty;
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    TranslatedTextBlock.Text = $"[Error: {ex.Message}]";
            }

            try { await Task.Delay(300, token); } catch (OperationCanceledException) { break; }
        }
    }

    private string GetSttMode()
    {
        if (CmbSttMode?.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? "deepgram";
        return "deepgram";
    }

    // ===================== AUDIO STT =====================

    private async void BtnStartAudio_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        if (!_isAudioMode)
        {
            _isAudioMode = true;
            BtnStartAudio.Content = loc.Get("BtnStopAudio");
            BtnStartAudio.ClearValue(Button.BackgroundProperty);
            BtnStartAudio.Style = (Style)FindResource("ActionDangerButton");
            CaptionBar.Visibility = Visibility.Visible;
            ShowSubtitleWithAnimation();

            string sttMode = GetSttMode();

            if (sttMode == "deepgram")
            {
                await StartDeepgramModeAsync();
            }
            else
            {
                await StartGroqWhisperModeAsync();
            }
        }
        else
        {
            await StopAudioModeAsync();
        }
    }

    private async Task StartDeepgramModeAsync()
    {
        var loc = LocalizationManager.Instance;
        QuotaTracker.Instance.SetDeepgram();
        CaptionTranslated.Text = loc.Get("ConnectingDeepgram");
        CaptionSpeaker.Visibility = Visibility.Visible;
        CaptionSpeaker.Text = loc.Get("SpeakerDetectionOn");

        _diarizedSttService = new DiarizedSttService(TxtDeepgramKey.Text);
        string sourceLang = GetSourceLanguage();
        _diarizedSttService.SetLanguage(sourceLang == "auto" ? "multi" : sourceLang);
        
        bool ok = await _diarizedSttService.ConnectAsync(msg =>
            Dispatcher.Invoke(() => { TxtAudioStatus.Text = msg; CaptionTranslated.Text = msg; }));

        if (!ok)
        {
            _isAudioMode = false;
            BtnStartAudio.Content = loc.Get("BtnStartAudio");
            BtnStartAudio.Style = (Style)FindResource("ActionPrimaryButton");
            CaptionSpeaker.Visibility = Visibility.Collapsed;
            TxtAudioStatus.Text = loc.Get("FailedDeepgram");
            if (_diarizedSttService != null)
            {
                await _diarizedSttService.DisposeAsync();
                _diarizedSttService = null;
            }
            return;
        }

        _diarizedSttService.TranscriptReady += async (s, args) =>
        {
            if (!_isAudioMode) return;

            string color = SpeakerColors[args.SpeakerIndex % SpeakerColors.Length];

            if (args.IsInterim)
            {
                // ── INTERIM: display raw transcript immediately, no translation ──
                // This makes words appear in real-time as the person is speaking
                Dispatcher.Invoke(() =>
                {
                    if (!_isAudioMode) return;
                    CaptionSpeaker.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                    CaptionSpeaker.Text = LocalizationManager.Instance.Get("SpeakerLabel", args.SpeakerIndex + 1);
                    CaptionSpeaker.Visibility = Visibility.Visible;
                    CaptionOriginal.Text = args.Text + "..."; // interim indicator
                    ShowSubtitleWithAnimation();
                });
                return; // Don't translate interim — wait for final
            }

            // ── FINAL: sentence is complete, translate it ──
            string targetLang = "", sourceLang = "";
            Dispatcher.Invoke(() => { targetLang = GetTargetLanguage(); sourceLang = GetSourceLanguage(); });

            // Show the finalized original text immediately (remove the blinking cursor)
            Dispatcher.Invoke(() =>
            {
                if (!_isAudioMode) return;
                CaptionSpeaker.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                CaptionSpeaker.Text = LocalizationManager.Instance.Get("SpeakerLabel", args.SpeakerIndex + 1);
                CaptionSpeaker.Visibility = Visibility.Visible;
                CaptionOriginal.Text = args.Text;
                CaptionTranslated.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                ShowSubtitleWithAnimation();
            });

            // Wait up to 5s for translation slot (queue instead of drop)
            if (!await _translateSemaphore.WaitAsync(5000)) return;
            try
            {
                Dispatcher.Invoke(() => CaptionTranslated.Text = "");
                
                string finalTranslation = "";
                await _translationProvider.TranslateStreamAsync(args.Text, targetLang, sourceLang, chunk => 
                {
                    finalTranslation = chunk;
                    Dispatcher.Invoke(() =>
                    {
                        if (!_isAudioMode) return;
                        if (IsWhisperHallucination(chunk))
                        {
                            CaptionTranslated.Text = "";
                            return;
                        }
                        CaptionTranslated.Text = chunk;
                        ShowSubtitleWithAnimation();
                    });
                });

                Dispatcher.Invoke(() =>
                {
                    if (!_isAudioMode) return;
                    AppendToHistory($"[Speaker {args.SpeakerIndex + 1}] {args.Text}", finalTranslation);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => CaptionTranslated.Text = $"[Error: {ex.Message}]");
            }
            finally
            {
                _translateSemaphore.Release();
            }
        };

        _audioCaptureService = new AudioCaptureService();
        _audioCaptureService.RawPcmBytesAvailable += async (s, pcmBytes) =>
        {
            if (_diarizedSttService != null && _isAudioMode)
                await _diarizedSttService.SendAudioAsync(pcmBytes);
        };
        await _audioCaptureService.StartAsync(GetSelectedDeviceId(), GetSelectedProcessId());
        TxtAudioStatus.Text = " Deepgram Streaming + Speaker Detection active";
        CaptionTranslated.Text = "[Waiting for audio...]";
    }

    private void RefreshApplications()
    {
        CmbAudioDevice.Items.Clear();
        var defaultItem = new ComboBoxItem { Content = "System Audio (Default)", Tag = "default", IsSelected = true };
        CmbAudioDevice.Items.Add(defaultItem);

        try
        {
            var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            var loc = LocalizationManager.Instance;
            // Loopback (Speakers)
            foreach (var device in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active))
            {
                CmbAudioDevice.Items.Add(new ComboBoxItem
                {
                    Content = loc.Get("DeviceOutput", device.FriendlyName),
                    Tag = device.ID
                });
            }
            // Microphones
            foreach (var device in enumerator.EnumerateAudioEndPoints(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
            {
                CmbAudioDevice.Items.Add(new ComboBoxItem
                {
                    Content = loc.Get("DeviceMic", device.FriendlyName),
                    Tag = device.ID
                });
            }
        }
        catch { }

        try
        {
            var loc = LocalizationManager.Instance;
            foreach (var p in Process.GetProcesses())
            {
                if (!string.IsNullOrEmpty(p.MainWindowTitle))
                {
                    var item = new ComboBoxItem
                    {
                        Content = loc.Get("DeviceApp", p.ProcessName, p.MainWindowTitle),
                        Tag = p.Id
                    };
                    CmbAudioDevice.Items.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Failed to enumerate processes: " + ex.Message);
        }
    }

    private void BtnRefreshAudioDevices_Click(object sender, RoutedEventArgs e)
    {
        RefreshApplications();
    }

    private string? GetSelectedDeviceId()
    {
        string? deviceId = null;
        Dispatcher.Invoke(() => {
            if (CmbAudioDevice.SelectedItem is ComboBoxItem item && item.Tag is string id && id != "default")
            {
                deviceId = id;
            }
        });
        return deviceId;
    }

    private int? GetSelectedProcessId()
    {
        int? pid = null;
        Dispatcher.Invoke(() => {
            if (CmbAudioDevice.SelectedItem is ComboBoxItem item && item.Tag is int id && id > 0)
            {
                pid = id;
            }
        });
        return pid;
    }

    private bool IsWhisperHallucination(string text)
    {
        return HallucinationDetector.IsHallucination(text);
    }

    private async Task StartGroqWhisperModeAsync()
    {
        QuotaTracker.Instance.NotifyCurrentGroqWhisper();
        CaptionTranslated.Text = "Loading Groq Whisper...";
        CaptionSpeaker.Visibility = Visibility.Collapsed;

        if (_whisperSttService == null)
        {
            _whisperSttService = new WhisperSttService(TxtApiKey.Text);
            _whisperSttService.RateLimitCooldownStarted += (s, secs) =>
            {
                Dispatcher.Invoke(() =>
                {
                    TxtAudioStatus.Text = LocalizationManager.Instance.Get("RateLimitCooldown", secs);
                });
            };

            bool ok = await _whisperSttService.InitializeAsync(msg =>
                Dispatcher.Invoke(() => { TxtAudioStatus.Text = msg; CaptionTranslated.Text = msg; }));

            if (!ok)
            {
                _isAudioMode = false;
                BtnStartAudio.Content = LocalizationManager.Instance.Get("BtnStartAudio");
                BtnStartAudio.Style = (Style)FindResource("ActionPrimaryButton");
                CaptionTranslated.Text = LocalizationManager.Instance.Get("FailedWhisper");
                return;
            }
        }

        AcousticSpeakerDetector.Reset();
        _audioCaptureService = new AudioCaptureService();
        _audioCts = new CancellationTokenSource();

        _audioCaptureService.AudioChunkReady += async (s, chunk) =>
        {
            if (_audioCts?.IsCancellationRequested == true) return;
            try
            {
                string targetLang = "", sourceLang = "";
                Dispatcher.Invoke(() => { targetLang = GetTargetLanguage(); sourceLang = GetSourceLanguage(); });

                // Acoustic pitch detection: Pisahkan Man 1, Man 2, Woman 1, Woman 2 berdasarkan nada suara (F0)
                var speaker = AcousticSpeakerDetector.DetectSpeaker(chunk);

                await _whisperSttService!.ChangeLanguageAsync(sourceLang);
                string transcribed = await _whisperSttService!.ProcessAudioChunkAsync(chunk);
                if (string.IsNullOrWhiteSpace(transcribed)) return;

                // ── HALLUCINATION FILTER (same as Deepgram path) ──
                // Groq Whisper also hallucinates "subtitles by...", "спасибо", etc. from noise
                if (IsWhisperHallucination(transcribed)) return;

                Dispatcher.Invoke(() =>
                {
                    if (!_isAudioMode) return;
                    CaptionSpeaker.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(speaker.ColorHex));
                    CaptionSpeaker.Text = speaker.Label;
                    CaptionSpeaker.Visibility = Visibility.Visible;
                    CaptionOriginal.Text = transcribed;
                    CaptionTranslated.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(speaker.ColorHex));
                    ShowSubtitleWithAnimation();
                });

                Dispatcher.Invoke(() => CaptionTranslated.Text = "");
                
                string finalTranslation = "";
                await _translationProvider.TranslateStreamAsync(transcribed, targetLang, sourceLang, chunk => 
                {
                    finalTranslation = chunk;
                    Dispatcher.Invoke(() =>
                    {
                        if (!_isAudioMode) return;
                        if (IsWhisperHallucination(chunk))
                        {
                            CaptionTranslated.Text = "";
                            return;
                        }
                        CaptionTranslated.Text = chunk;
                        ShowSubtitleWithAnimation();
                    });
                });

                Dispatcher.Invoke(() =>
                {
                    if (!_isAudioMode) return;
                    AppendToHistory($"[{speaker.Label}] {transcribed}", finalTranslation);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => CaptionTranslated.Text = $"[Error: {ex.Message}]");
            }
        };

        await _audioCaptureService.StartAsync(GetSelectedDeviceId(), GetSelectedProcessId());
        TxtAudioStatus.Text = "Groq Whisper active (Speaker Detection ON)";
        CaptionTranslated.Text = "[Waiting for audio...]";
    }

    private async Task StopAudioModeAsync()
    {
        _isAudioMode = false;
        _audioCts?.Cancel();
        _audioCaptureService?.Stop();
        _audioCaptureService?.Dispose();
        _audioCaptureService = null;

        AcousticSpeakerDetector.Reset();

        if (_diarizedSttService != null)
        {
            await _diarizedSttService.DisposeAsync();
            _diarizedSttService = null;
        }

        _subtitleHideTimer.Stop();
        BtnStartAudio.Content = LocalizationManager.Instance.Get("BtnStartAudio");
        BtnStartAudio.Style = (Style)FindResource("ActionPrimaryButton");
        CaptionBar.Visibility = Visibility.Collapsed;
        CaptionBar.Opacity = 0;
        CaptionSpeaker.Visibility = Visibility.Collapsed;
        // Reset translated color to default green
        CaptionTranslated.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00FF88"));
        _isCaptionVisible = false;
        TxtAudioStatus.Text = "";
    }

    private void ShowSubtitleWithAnimation()
    {
        _subtitleHideTimer.Stop();
        _subtitleHideTimer.Start();
        
        if (!_isCaptionVisible)
        {
            _fadeInStoryboard.Begin(this);
            _isCaptionVisible = true;
        }
    }

    // ===================== EXTRAS & CLOSE =====================

    private void BtnTogglePanel_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        if (ControlPanel.Visibility == Visibility.Visible)
        {
            ControlPanel.Visibility = Visibility.Collapsed;
            BtnTogglePanel.Content = loc.Get("BtnShowSettings");
        }
        else
        {
            ControlPanel.Visibility = Visibility.Visible;
            BtnTogglePanel.Content = loc.Get("BtnHideSettings");
        }
    }

    private void ChkHideAllWindows_Changed(object sender, RoutedEventArgs e)
    {
        bool hideFromCapture = ChkHideAllWindows?.IsChecked == true;
        CaptureExclusion.ApplyToAllWindows(hideFromCapture);

        var settings = LiveTranslatorOverlay.Core.AppSettings.Load();
        settings.HideFromCapture = hideFromCapture;
        settings.Save();
    }

    private void CmbUiLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbUiLanguage?.SelectedItem is ComboBoxItem item)
        {
            string tag = item.Tag?.ToString() ?? "en";
            LocalizationManager.Instance.SetLanguage(tag);
            var settings = LiveTranslatorOverlay.Core.AppSettings.Load();
            settings.UiLanguage = tag;
            settings.UiLanguageIndex = CmbUiLanguage.SelectedIndex;
            settings.Save();
        }
    }

    private void ApplyLocalization()
    {
        var loc = LocalizationManager.Instance;

        // Top Header
        if (BtnTogglePanel != null)
        {
            BtnTogglePanel.Content = ControlPanel.Visibility == Visibility.Visible ? loc.Get("BtnHideSettings") : loc.Get("BtnShowSettings");
        }
        if (TxtAppTitle != null) TxtAppTitle.Text = loc.Get("AppTitle");

        // Window Visibility Toggle
        if (TxtToggleHideDesc != null) TxtToggleHideDesc.Text = loc.Get("TxtToggleHideDesc");

        // App Preferences
        if (TxtTitlePreferences != null) TxtTitlePreferences.Text = loc.Get("TxtTitlePreferences");
        if (LblUiLanguage != null) LblUiLanguage.Text = loc.Get("LblUiLanguage");
        if (ItemUiLangEn != null) ItemUiLangEn.Content = loc.Get("UiLangEn");
        if (ItemUiLangId != null) ItemUiLangId.Content = loc.Get("UiLangId");
        if (ItemUiLangAuto != null) ItemUiLangAuto.Content = loc.Get("UiLangAuto");

        // Labels
        if (LblSourceLang != null) LblSourceLang.Text = loc.Get("LblSourceLanguage");
        if (LblTargetLang != null) LblTargetLang.Text = loc.Get("LblTargetLanguage");
        if (LblEngine != null) LblEngine.Text = loc.Get("LblEngine");
        if (LblApiKey != null) LblApiKey.Text = loc.Get("LblApiKey");
        if (LblDeepgramKey != null) LblDeepgramKey.Text = loc.Get("LblDeepgramKey");
        if (LblGroqModel != null) LblGroqModel.Text = loc.Get("LblGroqModel");
        if (BtnRefreshModels != null) BtnRefreshModels.Content = loc.Get("BtnRefresh");

        if (TxtTitleOcr != null) TxtTitleOcr.Text = loc.Get("TitleOcr");
        if (BtnDrawArea != null) BtnDrawArea.Content = loc.Get("BtnSelectArea");
        if (BtnStartLive != null) BtnStartLive.Content = _isLiveMode ? loc.Get("BtnStopOcr") : loc.Get("BtnStartOcr");

        if (TxtTitleAudio != null) TxtTitleAudio.Text = loc.Get("TitleAudio");
        if (LblSttMode != null) LblSttMode.Text = loc.Get("LblSttMode");
        if (LblFontSize != null) LblFontSize.Text = loc.Get("LblFontSize");
        if (LblAudioDevice != null) LblAudioDevice.Text = loc.Get("LblAudioDevice");
        if (BtnRefreshAudio != null) BtnRefreshAudio.Content = loc.Get("BtnRefresh");
        if (BtnStartAudio != null) BtnStartAudio.Content = _isAudioMode ? loc.Get("BtnStopAudio") : loc.Get("BtnStartAudio");

        if (TxtTitleQuota != null) TxtTitleQuota.Text = loc.Get("TitleQuota");
        if (BtnOpenHistory != null) BtnOpenHistory.Content = loc.Get("BtnOpenHistory");
        if (BtnFeedback != null) BtnFeedback.Content = loc.Get("BtnFeedback");
        if (BtnCloseApp != null) BtnCloseApp.Content = loc.Get("BtnCloseApp");

        // Combobox items text
        if (ItemSrcAuto != null) ItemSrcAuto.Content = loc.Get("AutoDetect");
        if (ItemSrcEn != null) ItemSrcEn.Content = loc.Get("LangEnglish");
        if (ItemSrcJa != null) ItemSrcJa.Content = loc.Get("LangJapanese");
        if (ItemSrcKo != null) ItemSrcKo.Content = loc.Get("LangKorean");
        if (ItemSrcZh != null) ItemSrcZh.Content = loc.Get("LangChinese");
        if (ItemSrcId != null) ItemSrcId.Content = loc.Get("LangIndonesian");
        if (ItemSrcEs != null) ItemSrcEs.Content = loc.Get("LangSpanish");
        if (ItemSrcFr != null) ItemSrcFr.Content = loc.Get("LangFrench");
        if (ItemSrcDe != null) ItemSrcDe.Content = loc.Get("LangGerman");
        if (ItemSrcRu != null) ItemSrcRu.Content = loc.Get("LangRussian");
        if (ItemSrcAr != null) ItemSrcAr.Content = loc.Get("LangArabic");

        if (ItemTgtId != null) ItemTgtId.Content = loc.Get("LangIndonesian");
        if (ItemTgtEn != null) ItemTgtEn.Content = loc.Get("LangEnglish");
        if (ItemTgtJa != null) ItemTgtJa.Content = loc.Get("LangJapanese");
        if (ItemTgtKo != null) ItemTgtKo.Content = loc.Get("LangKorean");
        if (ItemTgtZh != null) ItemTgtZh.Content = loc.Get("LangChineseSimp");
        if (ItemTgtEs != null) ItemTgtEs.Content = loc.Get("LangSpanish");
        if (ItemTgtFr != null) ItemTgtFr.Content = loc.Get("LangFrench");
        if (ItemTgtDe != null) ItemTgtDe.Content = loc.Get("LangGerman");
        if (ItemTgtRu != null) ItemTgtRu.Content = loc.Get("LangRussian");
        if (ItemTgtAr != null) ItemTgtAr.Content = loc.Get("LangArabic");

        if (ItemEngineGoogle != null) ItemEngineGoogle.Content = loc.Get("EngineGoogle");
        if (ItemEngineGroq != null) ItemEngineGroq.Content = loc.Get("EngineGroq");

        if (ItemSttDeepgram != null) ItemSttDeepgram.Content = loc.Get("SttDeepgram");
        if (ItemSttGroq != null) ItemSttGroq.Content = loc.Get("SttGroq");

        if (ItemFontSmall != null) ItemFontSmall.Content = loc.Get("FontSmall");
        if (ItemFontMed != null) ItemFontMed.Content = loc.Get("FontMedium");
        if (ItemFontLg != null) ItemFontLg.Content = loc.Get("FontLarge");
        if (ItemFontXl != null) ItemFontXl.Content = loc.Get("FontExtraLarge");

        if (ItemAudioDefault != null) ItemAudioDefault.Content = loc.Get("DefaultAudioDevice");
        if (ItemGroqAuto != null) ItemGroqAuto.Content = loc.Get("ModelAuto");

        // Active caption/status messages
        if (CaptionTranslated != null && (CaptionTranslated.Text == "[Waiting for audio...]" || CaptionTranslated.Text == "[Menunggu audio...]"))
        {
            CaptionTranslated.Text = loc.Get("WaitingForAudio");
        }
        if (TranslatedTextBlock != null && (TranslatedTextBlock.Text == "[Translation will appear here]" || TranslatedTextBlock.Text == "[Terjemahan akan muncul di sini]"))
        {
            TranslatedTextBlock.Text = loc.Get("OcrPlaceholder");
        }
    }

    private void ControlPanel_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double currentLeft = Canvas.GetLeft(ControlPanelContainer);
        double currentTop = Canvas.GetTop(ControlPanelContainer);
        
        // Ensure values aren't NaN
        if (double.IsNaN(currentLeft)) currentLeft = 20;
        if (double.IsNaN(currentTop)) currentTop = 20;

        Canvas.SetLeft(ControlPanelContainer, currentLeft + e.HorizontalChange);
        Canvas.SetTop(ControlPanelContainer, currentTop + e.VerticalChange);
    }

    private void BtnOpenHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_historyWindow == null)
        {
            string initial;
            lock (_historyBuffer)
            {
                initial = _historyBuffer.ToString();
            }
            _historyWindow = new HistoryWindow(initial);
            _historyWindow.Closed += (s, args) => _historyWindow = null;
        }
        _historyWindow.Show();
        _historyWindow.Activate();
        if (CaptureExclusion.IsExclusionEnabled)
        {
            CaptureExclusion.SetExclusion(_historyWindow, true);
        }
    }

    private FeedbackWindow? _feedbackWindow;

    private void BtnFeedback_Click(object sender, RoutedEventArgs e)
    {
        if (_feedbackWindow == null || !_feedbackWindow.IsLoaded)
        {
            _feedbackWindow = new FeedbackWindow();
            _feedbackWindow.Closed += (s, args) => _feedbackWindow = null;
        }
        _feedbackWindow.Show();
        _feedbackWindow.Activate();
        if (CaptureExclusion.IsExclusionEnabled)
        {
            CaptureExclusion.SetExclusion(_feedbackWindow, true);
        }
    }

    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _liveOcrCts?.Cancel();
        _audioCts?.Cancel();
        _audioCaptureService?.Stop();
        _audioCaptureService?.Dispose();
        _audioCaptureService = null;

        if (_whisperSttService != null)
        {
            await _whisperSttService.DisposeAsync();
            _whisperSttService = null;
        }

        if (_historyWindow != null)
        {
            _historyWindow.Close();
        }

        if (_feedbackWindow != null)
        {
            _feedbackWindow.Close();
        }

        this.Close();
    }

    private bool IsDescendantOf(DependencyObject parent, DependencyObject child)
    {
        DependencyObject? current = child;
        while (current != null)
        {
            if (current == parent) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }
}



