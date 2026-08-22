using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LiveTranslatorOverlay.Core;
using LiveTranslatorOverlay.Core.Translation;

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
    private bool _isAudioMode = false;
    private CancellationTokenSource? _audioCts;

    // Extras
    private HistoryWindow? _historyWindow;

    // UI Animations & Timers
    private DispatcherTimer _subtitleHideTimer;
    private Storyboard _fadeInStoryboard;
    private Storyboard _fadeOutStoryboard;
    private bool _isCaptionVisible = false;

    public MainWindow()
    {
        InitializeComponent();
        _ocrService = new ScreenOcrService();
        
        // Load API key dari appsettings.json (tidak di-commit ke GitHub)
        string savedApiKey = LoadApiKeyFromSettings();
        TxtApiKey.Text = savedApiKey;
        
        // Initialize with Groq as default (matches SelectedIndex=1 in XAML)
        _translationProvider = new GroqTranslateProvider(TxtApiKey.Text);
        
        this.SourceInitialized += MainWindow_SourceInitialized;

        this.Loaded += async (s, e) =>
        {
            Canvas.SetLeft(CaptionBar, (this.ActualWidth - 400) / 2);
            Canvas.SetTop(CaptionBar, this.ActualHeight - 140); // Lifted slightly for better UX
            
            // Auto-populate model list from Groq
            await RefreshGroqModelsAsync();

            _fadeInStoryboard = (Storyboard)this.Resources["FadeInCaption"];
            _fadeOutStoryboard = (Storyboard)this.Resources["FadeOutCaption"];
        };
        
        // Timer for auto-hiding subtitle
        _subtitleHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _subtitleHideTimer.Tick += (s, e) => 
        {
            _subtitleHideTimer.Stop();
            if (_isCaptionVisible && _isAudioMode)
            {
                _fadeOutStoryboard.Begin(this);
                _isCaptionVisible = false;
            }
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
    
    /// <summary>Load API key dari appsettings.json. File ini di-gitignore dan tidak ke-commit.</summary>
    private string LoadApiKeyFromSettings()
    {
        try
        {
            // Cari file di folder yang sama dengan .exe (bin/Debug/...)
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string settingsPath = System.IO.Path.Combine(exeDir, "appsettings.json");

            // Fallback: cari di root project saat develop
            if (!File.Exists(settingsPath))
            {
                string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..", "..", ".."));
                settingsPath = System.IO.Path.Combine(projectRoot, "appsettings.json");
            }

            if (!File.Exists(settingsPath)) return string.Empty;

            string json = File.ReadAllText(settingsPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("GroqApiKey", out var keyEl))
            {
                string? key = keyEl.GetString();
                if (!string.IsNullOrWhiteSpace(key) && key != "PASTE_API_KEY_GROQ_KAMU_DI_SINI")
                    return key;
            }
        }
        catch { /* Abaikan error baca file, biarkan user isi manual */ }
        return string.Empty;
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
            if (!(_translationProvider is GoogleFreeTranslateProvider))
                _translationProvider = new GoogleFreeTranslateProvider();
        }
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

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        CaptureExclusion.ExcludeFromCapture(this);
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
            TranslatedTextBlock.Text = "[Area dipilih. Klik 'Mulai' untuk OCR]";
            BtnStartLive.IsEnabled = true;
        }
    }

    private async void BtnStartLive_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOcrRegion.Width == 0 || _selectedOcrRegion.Height == 0)
        {
            MessageBox.Show("Silakan klik 'Pilih Area OCR' dan gambar area terlebih dahulu.");
            return;
        }

        if (!_isLiveMode)
        {
            _isLiveMode = true;
            SelectionRect.Stroke = new SolidColorBrush(Colors.Green);
            SelectionRect.Fill = null;
            BtnStartLive.Content = "⏸ Berhenti OCR";
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
            TranslatedTextBlock.Text = "[OCR Berhenti]";
            BtnStartLive.Content = "▶ Mulai Live OCR";
            BtnStartLive.ClearValue(Button.BackgroundProperty);
        }
    }

    private async Task RunLiveOcrLoopAsync(CancellationToken token)
    {
        TranslatedTextBlock.Text = "Memulai OCR...";

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
                        TranslatedTextBlock.Text = "Menerjemahkan...";

                        string translated = await _translationProvider.TranslateAsync(cleanText, targetLang, sourceLang);

                        // Sanity check: if result is empty or same as source, show warning
                        if (string.IsNullOrWhiteSpace(translated) || translated.Trim() == text.Trim())
                            translated = $"[Gagal terjemahkan] {text}";

                        if (!token.IsCancellationRequested)
                        {
                            TranslatedTextBlock.Text = translated;
                            if (_historyWindow != null && _historyWindow.IsVisible)
                            {
                                _historyWindow.AppendText(text, translated);
                            }
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
                        TranslatedTextBlock.Text = "[Tidak ada teks terdeteksi]";
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

    // ===================== AUDIO STT =====================

    private async void BtnStartAudio_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAudioMode)
        {
            _isAudioMode = true;
            BtnStartAudio.Content = "⏹ Berhenti Audio";
            BtnStartAudio.ClearValue(Button.BackgroundProperty); // Remove old style override
            BtnStartAudio.Style = (Style)FindResource("ActionDangerButton");
            
            CaptionBar.Visibility = Visibility.Visible;
            CaptionTranslated.Text = "Memuat Whisper AI model...";
            ShowSubtitleWithAnimation();

            // Initialize Whisper (lazy load)
            if (_whisperSttService == null)
            {
                _whisperSttService = new WhisperSttService(TxtApiKey.Text);
                bool ok = await _whisperSttService.InitializeAsync(msg =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtAudioStatus.Text = msg;
                        CaptionTranslated.Text = msg;
                    });
                });

                if (!ok)
                {
                    _isAudioMode = false;
                    BtnStartAudio.Content = "🔊 Mulai Audio Translate";
                    BtnStartAudio.Style = (Style)FindResource("ActionPrimaryButton");
                    CaptionTranslated.Text = "Gagal memuat model Whisper.";
                    return;
                }
            }

            // Start audio capture
            _audioCaptureService = new AudioCaptureService();
            _audioCts = new CancellationTokenSource();

            _audioCaptureService.AudioChunkReady += async (s, chunk) =>
            {
                if (_audioCts?.IsCancellationRequested == true) return;

                try
                {
                    string targetLang = "";
                    string sourceLang = "";
                    Dispatcher.Invoke(() =>
                    {
                        targetLang = GetTargetLanguage();
                        sourceLang = GetSourceLanguage();
                    });

                    await _whisperSttService.ChangeLanguageAsync(sourceLang);

                    // Jalankan STT dulu, lalu translate — keduanya async tapi sequential dalam satu chunk
                    // (paralel antar chunk sudah dihandle oleh queue di AudioCaptureService)
                    string transcribed = await _whisperSttService!.ProcessAudioChunkAsync(chunk);
                    if (string.IsNullOrWhiteSpace(transcribed)) return;

                    // Jalankan translate paralel — tampilkan teks asli dulu sambil nunggu terjemahan
                    Dispatcher.Invoke(() =>
                    {
                        if (!_isAudioMode) return;
                        CaptionOriginal.Text = transcribed;
                        CaptionTranslated.Text = "Menerjemahkan...";
                        ShowSubtitleWithAnimation();
                    });

                    string translated = await _translationProvider.TranslateAsync(transcribed, targetLang, sourceLang);

                    Dispatcher.Invoke(() =>
                    {
                        if (!_isAudioMode) return;
                        CaptionTranslated.Text = translated;
                        ShowSubtitleWithAnimation();

                        if (_historyWindow != null && _historyWindow.IsVisible)
                            _historyWindow.AppendText(transcribed, translated);
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => CaptionTranslated.Text = $"[Error: {ex.Message}]");
                }
            };

            _audioCaptureService.Start();
            TxtAudioStatus.Text = "🔴 Merekam audio sistem...";
            CaptionTranslated.Text = "[Menunggu audio...]";
        }
        else
        {
            _isAudioMode = false;
            _audioCts?.Cancel();
            _audioCaptureService?.Stop();
            _audioCaptureService?.Dispose();
            _audioCaptureService = null;

            _subtitleHideTimer.Stop();

            BtnStartAudio.Content = "🔊 Mulai Audio Translate";
            BtnStartAudio.Style = (Style)FindResource("ActionPrimaryButton");
            CaptionBar.Visibility = Visibility.Collapsed;
            CaptionBar.Opacity = 0;
            _isCaptionVisible = false;
            TxtAudioStatus.Text = "Model dimuat (siap)";
        }
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
        if (ControlPanel.Visibility == Visibility.Visible)
        {
            ControlPanel.Visibility = Visibility.Collapsed;
            BtnTogglePanel.Content = "⚙️ Show Settings";
        }
        else
        {
            ControlPanel.Visibility = Visibility.Visible;
            BtnTogglePanel.Content = "⚙️ Hide Settings";
        }
    }

    private void BtnOpenHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_historyWindow == null)
        {
            _historyWindow = new HistoryWindow();
        }
        _historyWindow.Show();
        _historyWindow.Activate();
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