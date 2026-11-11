using System;
using System.IO;
using System.Globalization;
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
using LiveTranslatorOverlay.Core.Update;
using LiveTranslatorOverlay.Core.Japanese;
using NAudio.CoreAudioApi;
using System.Diagnostics;

namespace LiveTranslatorOverlay;

public partial class MainWindow : Window
{
    private DispatcherTimer? _apiKeyDebounceTimer;
    private Point _startPoint;
    private bool _isDrawing;
    private Rect _selectedOcrRegion;
    private bool _isLiveMode = false;
    private CancellationTokenSource? _liveOcrCts;
    private ITranslationProvider _translationProvider;
    private string _lastOcrText = string.Empty;
    private DateTime _lastUserActivity = DateTime.UtcNow;

    // Audio STT
    private AudioCaptureService? _audioCaptureService;
    private WhisperSttService? _whisperSttService;
    private LocalSpeechService? _localSpeechService;
    private DiarizedSttService? _diarizedSttService;
    private bool _isAudioMode = false;
    private CancellationTokenSource? _audioCts;
    // Semaphore ensures only one translation runs at a time (prevents queued-up delays)
    private readonly SemaphoreSlim _translateSemaphore = new SemaphoreSlim(1, 1);

    // Speaker diarization colors (Deepgram mode)
    private static readonly string[] SpeakerColors = { "#00FF88", "#88BBFF", "#FFDD44", "#FF88CC", "#BB88FF" };

    // Extras
    private HistoryWindow? _historyWindow;
    private readonly TranslationHistory _historyBuffer = new();

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
        lock (_historyBuffer)
        {
            _historyBuffer.Add(sourceText, translatedText);
        }
        Dispatcher.Invoke(() =>
        {
            if (_historyWindow != null && _historyWindow.IsVisible)
            {
                lock (_historyBuffer) _historyWindow.SetHistory(_historyBuffer.ToString());
            }
        });
    }

    // UI Animations & Timers
    private DispatcherTimer _subtitleHideTimer;
    private Storyboard? _fadeInStoryboard;
    private Storyboard? _fadeOutStoryboard;
    private bool _isCaptionVisible = false;
    private string _activeScriptCategory = "none";

    // Apple-style fluid scroll physics state (interruptible, velocity-continuous, slowed-down step)
    private readonly System.Collections.Generic.Dictionary<ScrollViewer, double> _scrollTargets = new();
    private bool _isScrollRenderingHooked = false;
    private DateTime _lastScrollFrameTime = DateTime.UtcNow;

    private string _savedAudioDeviceName = "";

    public MainWindow()
    {
        InitializeComponent();
        TxtInstalledVersion.Text = $"Installed {AppVersion.DisplayString}";
        if (App.IsDeveloperMode)
        {
            BtnDeveloper.Visibility = Visibility.Visible;
            DeveloperBadge.Visibility = Visibility.Visible;
            Title = $"Live Translator Overlay {AppVersion.DisplayString} — Developer mode";
        }
        foreach (var language in AdditionalLanguages.Names)
        {
            CmbSourceLang.Items.Add(new ComboBoxItem { Content = $"{language.Value} ({language.Key})", Tag = language.Key });
            CmbTargetLang.Items.Add(new ComboBoxItem { Content = $"{language.Value} ({language.Key})", Tag = language.Key });
        }
        
        var loadedSettings = LiveTranslatorOverlay.Core.AppSettings.Load();
        loadedSettings.UseAvailableEngines(System.IO.File.Exists(System.IO.Path.Combine(AppSettings.DataDirectory, "local-speech.json")));
        ItemLocalTranslation.IsEnabled = System.IO.File.Exists(System.IO.Path.Combine(AppSettings.DataDirectory, "local-speech.json"));
        TxtApiKey.Password = loadedSettings.GroqApiKey;
        TxtDeepgramKey.Password = loadedSettings.DeepgramApiKey;
        if (!string.IsNullOrEmpty(loadedSettings.SourceLangTag))
            SelectLanguageByTag(CmbSourceLang, loadedSettings.SourceLangTag, loadedSettings.SourceLangIndex);
        else if (loadedSettings.SourceLangIndex >= 0 && loadedSettings.SourceLangIndex < CmbSourceLang.Items.Count)
            CmbSourceLang.SelectedIndex = loadedSettings.SourceLangIndex;

        if (!string.IsNullOrEmpty(loadedSettings.TargetLangTag))
            SelectLanguageByTag(CmbTargetLang, loadedSettings.TargetLangTag, loadedSettings.TargetLangIndex);
        else if (loadedSettings.TargetLangIndex >= 0 && loadedSettings.TargetLangIndex < CmbTargetLang.Items.Count)
            CmbTargetLang.SelectedIndex = loadedSettings.TargetLangIndex;

        SetupSearchableLanguageComboBox(CmbSourceLang);
        SetupSearchableLanguageComboBox(CmbTargetLang);

        if (loadedSettings.JapaneseStyleIndex >= 0 && loadedSettings.JapaneseStyleIndex < CmbJapaneseStyle.Items.Count) CmbJapaneseStyle.SelectedIndex = loadedSettings.JapaneseStyleIndex;
        if (loadedSettings.EngineIndex >= 0 && loadedSettings.EngineIndex < CmbEngine.Items.Count) CmbEngine.SelectedIndex = loadedSettings.EngineIndex;
        if (loadedSettings.SttModeIndex >= 0 && loadedSettings.SttModeIndex < CmbSttMode.Items.Count) CmbSttMode.SelectedIndex = loadedSettings.SttModeIndex;
        if (loadedSettings.FontSizeIndex >= 0 && loadedSettings.FontSizeIndex < CmbFontSize.Items.Count) CmbFontSize.SelectedIndex = loadedSettings.FontSizeIndex;
        _savedAudioDeviceName = loadedSettings.AudioDeviceName;

        // UI Language setup (English default, can follow system default)
        SelectLanguageByTag(CmbUiLanguage, loadedSettings.UiLanguage, 0);
        if (ChkHideAllWindows != null)
        {
            ChkHideAllWindows.IsChecked = loadedSettings.HideFromCapture;
        }

        LocalizationManager.Instance.SetLanguage(loadedSettings.UiLanguage);
        LocalizationManager.Instance.LanguageChanged += (s, e) => Dispatcher.Invoke(ApplyLocalization);
        ApplyLocalization();
        
        // Preserve online choices as optional providers.
        if (loadedSettings.EngineIndex == 1 && !string.IsNullOrWhiteSpace(TxtApiKey.Password))
        {
            var groq = new GroqTranslateProvider(TxtApiKey.Password);
            groq.JapaneseStyle = GetSelectedJapaneseScriptStyle();
            _translationProvider = groq;
        }
        else if (CmbEngine.SelectedIndex == 2)
        {
            _translationProvider = new LocalTranslateProvider();
        }
        else
        {
            var google = new GoogleFreeTranslateProvider();
            google.ScriptStyle = GetSelectedJapaneseScriptStyle();
            _translationProvider = google;
        }
        UpdateProviderUi();
        SortLanguageComboBoxAscending(CmbSourceLang);
        SortLanguageComboBoxAscending(CmbTargetLang);
        UpdateScriptStyleVisibilityAndOptions(animate: false);

        QuotaTracker.Instance.QuotaChanged += (s, info) =>
        {
            Dispatcher.BeginInvoke(UpdateProviderUi);
        };

        CaptureExclusion.Initialize();
        CaptureExclusion.ScreenshotOccurred += () => Dispatcher.Invoke(() => this.Opacity = 0);
        CaptureExclusion.ScreenshotFinished += () => Dispatcher.Invoke(() => this.Opacity = 1);

        this.Closing += MainWindow_Closing;

        this.Loaded += (s, e) =>
        {
            var loadedSettings = LiveTranslatorOverlay.Core.AppSettings.Load();
            CaptureExclusion.ApplyToAllWindows(loadedSettings.HideFromCapture);

            QuotaTracker.Instance.Initialize(loadedSettings.GroqApiKey, loadedSettings.EngineIndex, loadedSettings.SttModeIndex);
            UpdateProviderUi();

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
            
            _fadeInStoryboard = (Storyboard)this.Resources["FadeInCaption"];
            _fadeOutStoryboard = (Storyboard)this.Resources["FadeOutCaption"];
            UpdateApiKeyStatusVisual();
        };
        
        // Timer for auto-hiding subtitle (Disabled per user request for STT)
        _subtitleHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _subtitleHideTimer.Tick += (s, e) => 
        {
            _subtitleHideTimer.Stop();
        };
        
        TranslatedTextContainer.SizeChanged += (_, _) => PositionOcrResult();
        OverlayCanvas.SizeChanged += (_, _) => PositionOcrResult();
        
        SetupApiKeyVerification();
        SetupAutoUpdater();
        Loaded += (_, _) => ShowReleaseNotesIfNew();
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

    private void SaveCurrentSettings()
    {
        try
        {
            var settings = new LiveTranslatorOverlay.Core.AppSettings
            {
                HideFromCapture = ChkHideAllWindows.IsChecked == true,
                GroqApiKey = TxtApiKey.Password,
                DeepgramApiKey = TxtDeepgramKey.Password,
                SourceLangIndex = CmbSourceLang.SelectedIndex,
                TargetLangIndex = CmbTargetLang.SelectedIndex,
                SourceLangTag = (CmbSourceLang.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto",
                TargetLangTag = (CmbTargetLang.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "auto",
                JapaneseStyleIndex = CmbJapaneseStyle.SelectedIndex,
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
        catch { }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopUsageClock();
        _liveOcrCts?.Cancel();
        _audioCts?.Cancel();
        _audioCaptureService?.Stop();
        CaptureExclusion.Shutdown();
        SaveCurrentSettings();
        QuotaTracker.Instance.Flush();

        // Opsi A: Seamlessly apply update when closing window

    }

    private async Task RefreshGroqModelsAsync()
    {
        if (_translationProvider is not GroqTranslateProvider groqProvider) return;
        
        var models = await groqProvider.GetAvailableModelsAsync();
        if (models.Count == 0) return;
        
        string prevSelected = (CmbGroqModel.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
        CmbGroqModel.Items.Clear();
        CmbGroqModel.Items.Add(new ComboBoxItem { Content = LocalizationManager.Instance.Get("ModelAuto"), Tag = "" });
        
        int selectedIdx = 0;
        for (int i = 0; i < models.Count; i++)
        {
            var item = new ComboBoxItem { Content = models[i], Tag = models[i] };
            CmbGroqModel.Items.Add(item);
            if (models[i] == prevSelected) selectedIdx = i + 1;
        }
        
        CmbGroqModel.SelectedIndex = selectedIdx;
    }
    
    private async void BtnRefreshModels_Click(object sender, RoutedEventArgs e)
    {
        UpdateEngine();
        await RefreshGroqModelsAsync();
    }

    private bool _isUpdatingLanguageText = false;

    private static void SelectLanguageByTag(ComboBox cmb, string tag, int fallbackIndex = 0)
    {
        if (cmb == null || cmb.Items.Count == 0) return;
        foreach (var obj in cmb.Items)
        {
            if (obj is ComboBoxItem item && string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                cmb.SelectedItem = item;
                cmb.Text = item.Content?.ToString() ?? "";
                return;
            }
        }
        if (fallbackIndex >= 0 && fallbackIndex < cmb.Items.Count)
        {
            cmb.SelectedIndex = fallbackIndex;
            if (cmb.SelectedItem is ComboBoxItem item)
                cmb.Text = item.Content?.ToString() ?? "";
        }
        else
        {
            cmb.SelectedIndex = 0;
        }
    }

    private void SortLanguageComboBoxAscending(ComboBox cmb)
    {
        if (cmb == null || cmb.Items.Count <= 1) return;

        var selectedItem = cmb.SelectedItem as ComboBoxItem;
        string? selectedTag = selectedItem?.Tag?.ToString();

        ComboBoxItem? autoItem = null;
        var otherItems = new System.Collections.Generic.List<ComboBoxItem>();

        foreach (var item in cmb.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag?.ToString() == "auto")
                autoItem = item;
            else
                otherItems.Add(item);
        }

        otherItems.Sort((a, b) => string.Compare(a.Content?.ToString(), b.Content?.ToString(), StringComparison.CurrentCultureIgnoreCase));

        _isUpdatingLanguageText = true;
        cmb.Items.Clear();
        if (autoItem != null) cmb.Items.Add(autoItem);
        foreach (var item in otherItems) cmb.Items.Add(item);

        if (!string.IsNullOrEmpty(selectedTag))
        {
            SelectLanguageByTag(cmb, selectedTag, 0);
        }
        else if (cmb.SelectedIndex < 0)
        {
            cmb.SelectedIndex = 0;
        }

        if (cmb.SelectedItem is ComboBoxItem cur)
        {
            cmb.Text = cur.Content?.ToString() ?? "";
        }
        _isUpdatingLanguageText = false;
    }

    private void SetupSearchableLanguageComboBox(ComboBox cmb)
    {
        if (cmb == null) return;
        cmb.IsEditable = true;
        cmb.StaysOpenOnEdit = true;
        cmb.IsTextSearchEnabled = false;

        if (cmb.SelectedItem is ComboBoxItem initialItem)
        {
            _isUpdatingLanguageText = true;
            cmb.Text = initialItem.Content?.ToString() ?? "";
            _isUpdatingLanguageText = false;
        }

        // Live search filter on typing
        cmb.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, ev) =>
        {
            if (_isUpdatingLanguageText || cmb.SelectedItem == null) return;

            string query = cmb.Text.Trim();
            string selectedContent = (cmb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

            // If the text equals the currently selected item, don't filter
            if (string.Equals(query, selectedContent, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var item in cmb.Items.OfType<ComboBoxItem>())
                    item.Visibility = Visibility.Visible;
                return;
            }

            // User is actively typing to search -> ensure dropdown is open
            if (!cmb.IsDropDownOpen)
            {
                cmb.IsDropDownOpen = true;
            }

            foreach (var item in cmb.Items.OfType<ComboBoxItem>())
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    item.Visibility = Visibility.Visible;
                    continue;
                }

                string content = item.Content?.ToString() ?? "";
                string tag = item.Tag?.ToString() ?? "";
                bool match = content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             tag.Contains(query, StringComparison.OrdinalIgnoreCase);

                item.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            }
        }));

        // When dropdown closes or focus lost: reset filter and restore full name
        cmb.DropDownClosed += (s, ev) =>
        {
            foreach (var item in cmb.Items.OfType<ComboBoxItem>())
                item.Visibility = Visibility.Visible;

            if (cmb.SelectedItem is ComboBoxItem selItem)
            {
                _isUpdatingLanguageText = true;
                cmb.Text = selItem.Content?.ToString() ?? "";
                _isUpdatingLanguageText = false;
            }
        };

        cmb.LostFocus += (s, ev) =>
        {
            foreach (var item in cmb.Items.OfType<ComboBoxItem>())
                item.Visibility = Visibility.Visible;

            if (cmb.SelectedItem is ComboBoxItem selItem)
            {
                _isUpdatingLanguageText = true;
                cmb.Text = selItem.Content?.ToString() ?? "";
                _isUpdatingLanguageText = false;
            }
        };

        // When clicking or focusing: select all text so typing immediately replaces it with search
        cmb.PreviewMouseDown += (s, ev) =>
        {
            if (!cmb.IsDropDownOpen)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (cmb.Template.FindName("PART_EditableTextBox", cmb) is TextBox tb)
                    {
                        tb.SelectAll();
                    }
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        };

        // Keyboard navigation: Enter commits first visible match if needed
        cmb.PreviewKeyDown += (s, ev) =>
        {
            if (ev.Key == Key.Enter)
            {
                if (cmb.IsDropDownOpen)
                {
                    var firstMatch = cmb.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Visibility == Visibility.Visible);
                    if (firstMatch != null && cmb.SelectedItem != firstMatch && !string.IsNullOrWhiteSpace(cmb.Text))
                    {
                        cmb.SelectedItem = firstMatch;
                    }
                    cmb.IsDropDownOpen = false;
                    ev.Handled = true;
                }
            }
            else if (ev.Key == Key.Escape)
            {
                cmb.IsDropDownOpen = false;
                if (cmb.SelectedItem is ComboBoxItem item)
                {
                    _isUpdatingLanguageText = true;
                    cmb.Text = item.Content?.ToString() ?? "";
                    _isUpdatingLanguageText = false;
                }
                ev.Handled = true;
            }
            else if (ev.Key == Key.Down && !cmb.IsDropDownOpen)
            {
                cmb.IsDropDownOpen = true;
                ev.Handled = true;
            }
        };
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cmb && cmb.SelectedItem is ComboBoxItem item)
        {
            _isUpdatingLanguageText = true;
            cmb.Text = item.Content?.ToString() ?? "";
            _isUpdatingLanguageText = false;
        }

        UpdateScriptStyleVisibilityAndOptions(animate: true);

    }

    /// <summary>
    /// Shows the Script Style selector ONLY when the selected target or source language has 2+ writing systems
    /// (Japanese, Korean, Chinese, Serbian, Bosnian, Uzbek, Azerbaijani, Mongolian), and hides it for single-script languages.
    /// </summary>
    private void UpdateScriptStyleVisibilityAndOptions(bool animate = false)
    {
        if (ScriptStylePanel == null || CmbJapaneseStyle == null || LblJapaneseStyle == null) return;
        if (ItemJpStandard == null || ItemJpHiragana == null || ItemJpFurigana == null || ItemJpRomaji == null || ItemJpKatakana == null) return;

        string targetLang = GetTargetLanguage();
        string sourceLang = GetSourceLanguage();

        string activeMultiLang = "";
        if (JapaneseScriptConverter.IsMultiScriptLanguage(targetLang))
            activeMultiLang = targetLang;
        else if (JapaneseScriptConverter.IsMultiScriptLanguage(sourceLang))
            activeMultiLang = sourceLang;

        if (string.IsNullOrEmpty(activeMultiLang))
        {
            ScriptStylePanel.Visibility = Visibility.Collapsed;
            _activeScriptCategory = "none";
            return;
        }

        string category = JapaneseScriptConverter.GetScriptCategory(activeMultiLang);
        bool categoryChanged = category != _activeScriptCategory;
        _activeScriptCategory = category;

        var loc = LocalizationManager.Instance;

        if (category == "ja")
        {
            LblJapaneseStyle.Text = loc.Get("LblJapaneseStyle");
            ItemJpStandard.Content = loc.Get("ItemJpStandard");
            ItemJpHiragana.Content = loc.Get("ItemJpHiragana");
            ItemJpFurigana.Content = loc.Get("ItemJpFurigana");
            ItemJpRomaji.Content = loc.Get("ItemJpRomaji");
            ItemJpKatakana.Content = loc.Get("ItemJpKatakana");

            ItemJpStandard.Visibility = Visibility.Visible;
            ItemJpHiragana.Visibility = Visibility.Visible;
            ItemJpFurigana.Visibility = Visibility.Visible;
            ItemJpRomaji.Visibility = Visibility.Visible;
            ItemJpKatakana.Visibility = Visibility.Visible;
        }
        else if (category == "ko")
        {
            LblJapaneseStyle.Text = loc.Get("LblKoreanStyle");
            ItemJpStandard.Content = loc.Get("ItemKoStandard");
            ItemJpFurigana.Content = loc.Get("ItemKoFurigana");
            ItemJpRomaji.Content = loc.Get("ItemKoRomaji");
            ItemJpKatakana.Content = loc.Get("ItemKoHanja");

            ItemJpStandard.Visibility = Visibility.Visible;
            ItemJpHiragana.Visibility = Visibility.Collapsed;
            ItemJpFurigana.Visibility = Visibility.Visible;
            ItemJpRomaji.Visibility = Visibility.Visible;
            ItemJpKatakana.Visibility = Visibility.Visible;

            if (CmbJapaneseStyle.SelectedItem == ItemJpHiragana)
                CmbJapaneseStyle.SelectedIndex = 0;
        }
        else if (category == "zh")
        {
            LblJapaneseStyle.Text = loc.Get("LblChineseStyle");
            ItemJpStandard.Content = loc.Get("ItemZhStandard");
            ItemJpHiragana.Content = loc.Get("ItemZhTrad");
            ItemJpFurigana.Content = loc.Get("ItemZhFurigana");
            ItemJpRomaji.Content = loc.Get("ItemZhRomaji");

            ItemJpStandard.Visibility = Visibility.Visible;
            ItemJpHiragana.Visibility = Visibility.Visible;
            ItemJpFurigana.Visibility = Visibility.Visible;
            ItemJpRomaji.Visibility = Visibility.Visible;
            ItemJpKatakana.Visibility = Visibility.Collapsed;

            if (CmbJapaneseStyle.SelectedItem == ItemJpKatakana)
                CmbJapaneseStyle.SelectedIndex = 0;
        }
        else if (category == "dual")
        {
            LblJapaneseStyle.Text = loc.Get("LblDualScriptStyle");
            ItemJpStandard.Content = loc.Get("ItemDualStandard");
            ItemJpRomaji.Content = loc.Get("ItemDualLatin");
            ItemJpHiragana.Content = loc.Get("ItemDualCyrillic");

            ItemJpStandard.Visibility = Visibility.Visible;
            ItemJpRomaji.Visibility = Visibility.Visible;
            ItemJpHiragana.Visibility = Visibility.Visible;
            ItemJpFurigana.Visibility = Visibility.Collapsed;
            ItemJpKatakana.Visibility = Visibility.Collapsed;

            if (CmbJapaneseStyle.SelectedItem == ItemJpFurigana || CmbJapaneseStyle.SelectedItem == ItemJpKatakana)
                CmbJapaneseStyle.SelectedIndex = 0;
        }

        if (ScriptStylePanel.Visibility != Visibility.Visible)
        {
            ScriptStylePanel.Visibility = Visibility.Visible;
            if (animate)
            {
                var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ScriptStylePanel.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }
        else if (categoryChanged && animate)
        {
            var pulse = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ScriptStylePanel.BeginAnimation(UIElement.OpacityProperty, pulse);
        }
    }

    private void SmoothScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        // If user is scrolling inside an open ComboBox popup and this event hit the parent SettingsScrollViewer, let the child handle it
        if (sv == SettingsScrollViewer && e.OriginalSource is DependencyObject src && src != sv)
        {
            DependencyObject? curr = src;
            while (curr != null && curr != sv)
            {
                if (curr is ScrollViewer innerSv && innerSv != sv)
                    return;
                curr = VisualTreeHelper.GetParent(curr);
            }
        }

        e.Handled = true;

        if (!_scrollTargets.TryGetValue(sv, out double currentTarget))
        {
            currentTarget = sv.VerticalOffset;
        }

        // Gentle, controlled scroll step (~24px per wheel notch instead of WPF's abrupt 48px jump)
        double step = e.Delta * 0.20;
        double nextTarget = Math.Clamp(currentTarget - step, 0.0, sv.ScrollableHeight);
        _scrollTargets[sv] = nextTarget;

        if (!_isScrollRenderingHooked)
        {
            _lastScrollFrameTime = DateTime.UtcNow;
            CompositionTarget.Rendering += OnSmoothScrollRendering;
            _isScrollRenderingHooked = true;
        }
    }

    private void OnSmoothScrollRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _lastScrollFrameTime).TotalSeconds, 0.001, 0.05);
        _lastScrollFrameTime = now;

        // Critically-damped exponential spring factor normalized for any monitor refresh rate (60Hz / 120Hz / 144Hz)
        double blend = 1.0 - Math.Pow(1.0 - 0.16, dt * 60.0);

        var finished = new System.Collections.Generic.List<ScrollViewer>();
        foreach (var kv in _scrollTargets)
        {
            var sv = kv.Key;
            double target = Math.Clamp(kv.Value, 0.0, sv.ScrollableHeight);
            double current = sv.VerticalOffset;
            double diff = target - current;

            if (Math.Abs(diff) < 0.35)
            {
                sv.ScrollToVerticalOffset(target);
                finished.Add(sv);
            }
            else
            {
                sv.ScrollToVerticalOffset(current + diff * blend);
            }
        }

        foreach (var sv in finished)
        {
            _scrollTargets.Remove(sv);
        }

        if (_scrollTargets.Count == 0 && _isScrollRenderingHooked)
        {
            CompositionTarget.Rendering -= OnSmoothScrollRendering;
            _isScrollRenderingHooked = false;
        }
    }

    private async void CmbJapaneseStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedStyle = GetSelectedJapaneseScriptStyle();
        if (_translationProvider is GroqTranslateProvider groq)
        {
            groq.JapaneseStyle = selectedStyle;
        }
        else if (_translationProvider is GoogleFreeTranslateProvider google)
        {
            google.ScriptStyle = selectedStyle;
        }

        string sourceLang = GetSourceLanguage();
        string targetLang = GetTargetLanguage();

        if (CaptionOriginal != null && !string.IsNullOrWhiteSpace(CaptionOriginal.Text))
        {
            CaptionOriginal.Text = await FormatJapaneseTextIfNeededAsync(CaptionOriginal.Text, sourceLang);
        }

        if (CaptionTranslated != null && !string.IsNullOrWhiteSpace(CaptionTranslated.Text) && (JapaneseScriptConverter.IsMultiScriptLanguage(targetLang) || ContainsJapaneseKana(CaptionTranslated.Text) || JapaneseScriptConverter.HasKanji(CaptionTranslated.Text) || JapaneseScriptConverter.HasHangul(CaptionTranslated.Text)))
        {
            CaptionTranslated.Text = await FormatJapaneseTextIfNeededAsync(CaptionTranslated.Text, targetLang);
        }

        if (TranslatedTextBlock != null && !string.IsNullOrWhiteSpace(TranslatedTextBlock.Text) && (JapaneseScriptConverter.IsMultiScriptLanguage(targetLang) || ContainsJapaneseKana(TranslatedTextBlock.Text) || JapaneseScriptConverter.HasKanji(TranslatedTextBlock.Text) || JapaneseScriptConverter.HasHangul(TranslatedTextBlock.Text)))
        {
            TranslatedTextBlock.Text = await FormatJapaneseTextIfNeededAsync(TranslatedTextBlock.Text, targetLang);
        }
    }

    private JapaneseScriptStyle GetSelectedJapaneseScriptStyle()
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(GetSelectedJapaneseScriptStyle);

        if (CmbJapaneseStyle?.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() switch
            {
                "hiragana" => JapaneseScriptStyle.Hiragana,
                "furigana" => JapaneseScriptStyle.Furigana,
                "romaji" => JapaneseScriptStyle.Romaji,
                "katakana" => JapaneseScriptStyle.Katakana,
                _ => JapaneseScriptStyle.Standard
            };
        }
        return JapaneseScriptStyle.Standard;
    }

    private async Task<string> FormatJapaneseTextIfNeededAsync(string text, string langCode)
    {
        var style = GetSelectedJapaneseScriptStyle();
        if (style == JapaneseScriptStyle.Standard || string.IsNullOrWhiteSpace(text))
            return text;

        if (JapaneseScriptConverter.IsMultiScriptLanguage(langCode) || JapaneseScriptConverter.HasKanji(text) || ContainsJapaneseKana(text) || JapaneseScriptConverter.HasHangul(text))
        {
            string apiKey = "";
            if (Dispatcher.CheckAccess())
                apiKey = GetTranslationMode() == "groq" ? TxtApiKey.Password : "";
            else
                apiKey = Dispatcher.Invoke(() => GetTranslationMode() == "groq" ? TxtApiKey.Password : "");

            return await JapaneseScriptConverter.ConvertForLanguageAsync(text, langCode, style, apiKey);
        }

        return text;
    }

    private static bool ContainsJapaneseKana(string text)
    {
        foreach (char c in text)
        {
            if ((c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF))
                return true;
        }
        return false;
    }
    
    private void CmbEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateEngine();
        UpdateApiKeyStatusVisual();
        UpdateProviderUi();
    }

    private void CmbGroqModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_translationProvider is GroqTranslateProvider groq)
            groq.SetModel((CmbGroqModel.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "");
        UpdateProviderUi();
    }
    
    private void UpdateEngine()
    {
        if (CmbEngine == null || TxtApiKey == null) return;
        
        if ((CmbEngine.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "local")
        {
            _translationProvider = new LocalTranslateProvider();
            return;
        }
        string selectedModel = "";
        if (CmbGroqModel?.SelectedItem is ComboBoxItem modelItem)
            selectedModel = modelItem.Tag?.ToString() ?? "";
        
        if (CmbEngine.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "groq")
        {
            if (_translationProvider is GroqTranslateProvider groq)
            {
                groq.UpdateApiKey(TxtApiKey.Password);
                groq.JapaneseStyle = GetSelectedJapaneseScriptStyle();
                if (!string.IsNullOrEmpty(selectedModel))
                    groq.SetModel(selectedModel);
            }
            else
            {
                var newGroq = new GroqTranslateProvider(TxtApiKey.Password);
                newGroq.JapaneseStyle = GetSelectedJapaneseScriptStyle();
                if (!string.IsNullOrEmpty(selectedModel))
                    newGroq.SetModel(selectedModel);
                _translationProvider = newGroq;
            }
        }
        else
        {
            if (_translationProvider is GoogleFreeTranslateProvider existingGoogle)
            {
                existingGoogle.ScriptStyle = GetSelectedJapaneseScriptStyle();
            }
            else
            {
                var newGoogle = new GoogleFreeTranslateProvider
                {
                    ScriptStyle = GetSelectedJapaneseScriptStyle()
                };
                _translationProvider = newGoogle;
            }
        }
    }

    // ===================== GROQ API KEY VALIDATION & FEEDBACK =====================

    private void SetupApiKeyVerification()
    {
        _apiKeyDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _apiKeyDebounceTimer.Tick += async (s, e) =>
        {
            _apiKeyDebounceTimer.Stop();
            await VerifyGroqApiKeyAsync(showNotification: false);
        };

        TxtApiKey.PasswordChanged += (s, e) =>
        {
            QuotaTracker.Instance.SetApiKey(TxtApiKey.Password.Trim());
            UpdateEngine();
            UpdateProviderUi();
            _apiKeyDebounceTimer.Stop();
            _apiKeyDebounceTimer.Start();
        };
    }

    private void UpdateApiKeyStatusVisual()
    {
        if (TxtApiKeyStatus == null || DotApiKeyStatus == null) return;
        var loc = LocalizationManager.Instance;
        string key = TxtApiKey?.Password?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(key))
        {
            DotApiKeyStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#71717A"));
            TxtApiKeyStatus.Text = "Enter a Groq API key";
            TxtApiKeyStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#71717A"));
            return;
        }

        DotApiKeyStatus.Fill = Brushes.Gray;
        TxtApiKeyStatus.Text = "Groq key entered";
        TxtApiKeyStatus.Foreground = Brushes.LightGray;
    }

    private async Task VerifyGroqApiKeyAsync(bool showNotification = false)
    {
        if (CmbEngine == null || TxtApiKey == null || TxtApiKeyStatus == null || DotApiKeyStatus == null) return;
        if (!UsesGroq()) return;
        string key = TxtApiKey.Password.Trim();
        var loc = LocalizationManager.Instance;

        if (string.IsNullOrWhiteSpace(key))
        {
            UpdateApiKeyStatusVisual();
            return;
        }

        // Show verifying pulse
        DotApiKeyStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24")); // Amber
        TxtApiKeyStatus.Text = loc.Get("ApiKeyChecking");
        TxtApiKeyStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));

        var (isValid, message, models) = await GroqTranslateProvider.ValidateKeyAsync(key);

        if (!UsesGroq() || key != TxtApiKey.Password.Trim()) return;

        if (isValid)
        {
            DotApiKeyStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")); // Emerald
            TxtApiKeyStatus.Text = loc.Get("ApiKeyValid");
            TxtApiKeyStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));

            // Populate Groq Models
            if (GetTranslationMode() == "groq" && models.Count > 0)
            {
                string prevSelected = (CmbGroqModel.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
                CmbGroqModel.Items.Clear();
                CmbGroqModel.Items.Add(new ComboBoxItem { Content = loc.Get("ModelAuto"), Tag = "" });
                int selectedIdx = 0;
                for (int i = 0; i < models.Count; i++)
                {
                    var item = new ComboBoxItem { Content = models[i], Tag = models[i] };
                    CmbGroqModel.Items.Add(item);
                    if (models[i] == prevSelected) selectedIdx = i + 1;
                }
                CmbGroqModel.SelectedIndex = selectedIdx;
            }

            var settings = AppSettings.Load();
            settings.GroqApiKey = key;
            settings.Save();
            QuotaTracker.Instance.SetApiKey(key);
            UpdateProviderUi();
        }
        else
        {
            DotApiKeyStatus.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Red
            TxtApiKeyStatus.Text = $"{loc.Get("ApiKeyInvalid")}: {message}";
            TxtApiKeyStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
        }
    }

    private async void BtnVerifyApiKey_Click(object sender, RoutedEventArgs e)
    {
        BtnVerifyApiKey.IsEnabled = false;
        try
        {
            await VerifyGroqApiKeyAsync(showNotification: true);
        }
        finally
        {
            BtnVerifyApiKey.IsEnabled = true;
        }
    }

    // ===================== SILENT AUTO UPDATER =====================

    private void SetupAutoUpdater()
    {
        TxtUpdateStatus.Text = "Waiting for update check…";

        AutoUpdaterService.Instance.StatusChanged += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                TxtUpdateStatus.Text = e.StatusMessage;
                if (e.IsReadyToInstall)
                {
                    BannerUpdateReady.Visibility = Visibility.Visible;
                    TxtBannerUpdateTitle.Text = LocalizationManager.Instance.Get("UpdateAvailable", e.NewVersion);
                    TxtBannerUpdateDesc.Text = LocalizationManager.Instance.Get("BtnRestartUpdate");
                    BtnApplyUpdateNow.Visibility = Visibility.Visible;
                    BtnApplyUpdateNow.Content = $"{LocalizationManager.Instance.Get("BtnRestartUpdate")} ({e.NewVersion})";


                }
            });
        };

        AutoUpdaterService.Instance.UpdateReadyToInstall += (s, version) =>
        {
            Dispatcher.Invoke(() =>
            {
                BannerUpdateReady.Visibility = Visibility.Visible;
                TxtBannerUpdateTitle.Text = LocalizationManager.Instance.Get("UpdateAvailable", version);
                TxtBannerUpdateDesc.Text = LocalizationManager.Instance.Get("BtnRestartUpdate");
                BtnBannerRestart.Content = LocalizationManager.Instance.Get("BtnRestartUpdate");

                BtnApplyUpdateNow.Visibility = Visibility.Visible;
                BtnApplyUpdateNow.Content = $"{LocalizationManager.Instance.Get("BtnRestartUpdate")} ({version})";


            });
        };

        // Start background silent check
        _ = AutoUpdaterService.Instance.StartBackgroundCheckAsync(silent: true);
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        try
        {
            await AutoUpdaterService.Instance.CheckAndDownloadAsync(silent: false);
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private void BtnApplyUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_isAudioMode || _isLiveMode)
        {
            MessageBox.Show(LocalizationManager.Instance.Get("StopBeforeUpdate"));
            return;
        }
        SaveCurrentSettings();
        AutoUpdaterService.Instance.ApplyUpdateAndRestart();
    }

    private string GetTranslationMode() => (CmbEngine?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "google";

    internal string GetGroqTranslationKey() => GetTranslationMode() == "groq" ? TxtApiKey.Password.Trim() : "";

    private bool UsesGroq() => GetTranslationMode() == "groq" || GetSttMode() == "groq";

    private void CmbSttMode_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateProviderUi();

    private void UpdateProviderUi()
    {
        if (QuotaPanel == null) return; // Selection events can fire during XAML initialization.
        string speech = GetSttMode();
        bool groqTranslation = GetTranslationMode() == "groq";
        GroqKeySettings.Visibility = UsesGroq() ? Visibility.Visible : Visibility.Collapsed;
        DeepgramKeySettings.Visibility = speech == "deepgram" ? Visibility.Visible : Visibility.Collapsed;
        GroqModelSettings.Visibility = groqTranslation ? Visibility.Visible : Visibility.Collapsed;

        string model = speech switch
        {
            "deepgram" => $"Deepgram · {DiarizedSttService.ModelName}",
            "groq" => $"Groq · {WhisperSttService.ModelName}",
            "local-small" => "Local Whisper small · CPU",
            _ => "Local Whisper base · CPU"
        };
        TxtSpeechSelection.Text = _isAudioMode
            ? $"{model} · Stop audio to change speech settings."
            : $"{model} · {(speech is "local" or "local-small" ? "No key needed. Downloads the model on first use." : "Sends audio to the selected provider.")}";

        var usage = new List<string>();
        if (speech == "deepgram") usage.Add($"{model} (speech)\nCheck usage in your Deepgram dashboard.");
        if (speech == "groq") usage.Add($"{model} (speech)\n{QuotaTracker.Instance.GetGroqUsage(speech: true)}");
        if (groqTranslation)
        {
            string? translationModel = (_translationProvider as GroqTranslateProvider)?.CurrentModel;
            usage.Add($"Groq · {(string.IsNullOrEmpty(translationModel) ? "Auto model" : translationModel)} (translation)\n{QuotaTracker.Instance.GetGroqUsage(speech: false)}");
        }
        QuotaPanel.Visibility = usage.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        TxtQuotaInfo.Text = string.Join("\n\n", usage);
    }

    private string GetTargetLanguage()
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(GetTargetLanguage);

        if (CmbTargetLang?.SelectedItem is ComboBoxItem item)
        {
            string tag = item.Tag?.ToString() ?? "id";
            if (tag == "auto")
            {
                string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                string installedSys = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                if (AdditionalLanguages.Names.ContainsKey(sys)) return sys;
                return sys switch
                {
                    "ru" or "id" or "ja" or "ko" or "zh" or "uk" or "az" or "uz" or "mn" or "ka"
                        or "sr" or "hr" or "bs" or "bg" or "ro" or "el" or "sq" or "mk" or "sl"
                        or "es" or "fr" or "de" or "ar" => sys,
                    _ => installedSys switch
                    {
                        "ru" or "id" or "ja" or "uk" => installedSys,
                        _ => "en"
                    }
                };
            }
            return tag;
        }
        return "id";
    }

    private string GetSourceLanguage()
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(GetSourceLanguage);

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
        _selectedOcrRegion = default;
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

        if (!OverlayCanvas.CaptureMouse()) return;
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
        OverlayCanvas.ReleaseMouseCapture();
        
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
            TranslatedTextContainer.Visibility = Visibility.Visible;
            TranslatedTextBlock.Text = "[Area selected. Click 'Start' for OCR]";
            BtnStartLive.IsEnabled = true;
            PositionOcrResult();
        }
        else SelectionRect.Visibility = Visibility.Collapsed;
    }

    private void PositionOcrResult()
    {
        if (TranslatedTextContainer.Visibility != Visibility.Visible || _selectedOcrRegion.IsEmpty) return;
        const double margin = 8;
        double width = OverlayCanvas.ActualWidth;
        double height = OverlayCanvas.ActualHeight;
        if (width <= margin * 2 || height <= margin * 2) return;
        TranslatedTextContainer.MaxWidth = Math.Min(800, width - margin * 2);
        double right = Math.Max(margin, width - TranslatedTextContainer.ActualWidth - margin);
        double bottom = Math.Max(margin, height - TranslatedTextContainer.ActualHeight - margin);
        double below = _selectedOcrRegion.Bottom + margin;
        double top = below <= bottom ? below : _selectedOcrRegion.Top - TranslatedTextContainer.ActualHeight - margin;
        Canvas.SetLeft(TranslatedTextContainer, Math.Clamp(_selectedOcrRegion.Left, margin, right));
        Canvas.SetTop(TranslatedTextContainer, Math.Clamp(top, margin, bottom));
    }

    private void BtnCloseOcr_Click(object sender, RoutedEventArgs e) => DismissOcr();

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || (!_isDrawingMode && TranslatedTextContainer.Visibility != Visibility.Visible)) return;
        if (e.OriginalSource is ComboBox { IsDropDownOpen: true }) return;
        DismissOcr();
        e.Handled = true;
    }

    private void DismissOcr()
    {
        _liveOcrCts?.Cancel();
        _liveOcrCts = null;
        _isLiveMode = false;
        _isDrawing = false;
        _isDrawingMode = false;
        if (OverlayCanvas.IsMouseCaptured) OverlayCanvas.ReleaseMouseCapture();
        Background = null;
        OverlayCanvas.Background = null;
        SelectionRect.Visibility = Visibility.Collapsed;
        TranslatedTextContainer.Visibility = Visibility.Collapsed;
        BtnStartLive.Content = LocalizationManager.Instance.Get("BtnStartOcr");
        BtnStartLive.ClearValue(Button.BackgroundProperty);
    }

    private async void BtnStartLive_Click(object sender, RoutedEventArgs e)
    {
        if (_isLiveMode)
        {
            DismissOcr();
            return;
        }
        var loc = LocalizationManager.Instance;
        if (_selectedOcrRegion.Width == 0 || _selectedOcrRegion.Height == 0)
        {
            MessageBox.Show(loc.Get("OcrSelectPrompt"));
            return;
        }

        using var session = new CancellationTokenSource();
        _liveOcrCts = session;
        _lastOcrText = string.Empty;
        _isLiveMode = true;
        SelectionRect.Stroke = Brushes.LimeGreen;
        SelectionRect.Fill = null;
        SelectionRect.Visibility = Visibility.Visible;
        TranslatedTextContainer.Visibility = Visibility.Visible;
        PositionOcrResult();
        BtnStartLive.Content = loc.Get("BtnStopOcr");
        BtnStartLive.Background = Brushes.DarkOrange;
        try { await RunLiveOcrLoopAsync(session.Token); }
        finally { if (_liveOcrCts == session) DismissOcr(); }
    }

    private void TxtOcrPackHint_MouseDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Buka Windows Settings -> Time & Language -> Language untuk memasang paket bahasa OCR.\n\nDetail: {ex.Message}", "OCR Language Settings", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async Task RunLiveOcrLoopAsync(CancellationToken token)
    {
        ScreenOcrService? ocr = null;
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
                
                ocr ??= new ScreenOcrService();
                ocr.ChangeLanguage(sourceLang);
                
                string text = await Task.Run(() => ocr.RecognizeRegionAsync(screenX, screenY, screenW, screenH), token);

                if (!token.IsCancellationRequested && !string.IsNullOrWhiteSpace(text))
                {
                    emptyCount = 0;
                    string cleanText = text.Trim();

                    // If text is an OCR system warning/error (e.g. language pack missing), display it directly
                    if (cleanText.StartsWith("[OCR") || cleanText.StartsWith("[Error:"))
                    {
                        TranslatedTextBlock.Text = cleanText;
                        await Task.Delay(300, token);
                        continue;
                    }

                    _lastUserActivity = DateTime.UtcNow;

                    string requestIdentity = $"{sourceLang}|{targetLang}|{CmbEngine.SelectedIndex}|{GetSelectedJapaneseScriptStyle()}|{cleanText}";
                    if (requestIdentity != _lastOcrText)
                    {
                        _lastOcrText = requestIdentity;
                        TranslatedTextBlock.Text = "Translating...";

                        Dispatcher.Invoke(() => TranslatedTextBlock.Text = "");
                        
                        string finalTranslation = "";
                        await _translationProvider.TranslateStreamAsync(cleanText, targetLang, sourceLang, chunk => 
                        {
                            if (token.IsCancellationRequested) return;
                            finalTranslation = chunk;
                            Dispatcher.Invoke(() =>
                            {
                                if (!token.IsCancellationRequested)
                                    TranslatedTextBlock.Text = chunk;
                            });
                        });

                        if (!token.IsCancellationRequested)
                        {
                            if (JapaneseScriptConverter.IsMultiScriptLanguage(targetLang) && GetSelectedJapaneseScriptStyle() != JapaneseScriptStyle.Standard)
                            {
                                string formatted = await FormatJapaneseTextIfNeededAsync(finalTranslation, targetLang);
                                if (token.IsCancellationRequested) break;
                                if (!string.IsNullOrWhiteSpace(formatted) && formatted != finalTranslation)
                                {
                                    finalTranslation = formatted;
                                    Dispatcher.Invoke(() =>
                                    {
                                        if (!token.IsCancellationRequested)
                                            TranslatedTextBlock.Text = finalTranslation;
                                    });
                                }
                            }

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

    private void BtnLicense_Click(object sender, RoutedEventArgs e) => new CommunityWindow(this, _usageClock.Checkpoint(), showSupport: true).ShowDialog();

    private void BtnCommunity_Click(object sender, RoutedEventArgs e) => new CommunityWindow(this, _usageClock.Checkpoint(), showSupport: false).ShowDialog();

    private string GetSttMode()
    {
        if (CmbSttMode?.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? "local";
        return "local";
    }

    // ===================== AUDIO STT =====================

    private async void BtnStartAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_isAudioMode)
        {
            await StopAudioModeAsync();
            return;
        }

        string sttMode = GetSttMode();
        if ((sttMode == "groq" && string.IsNullOrWhiteSpace(TxtApiKey.Password)) ||
            (sttMode == "deepgram" && string.IsNullOrWhiteSpace(TxtDeepgramKey.Password)))
        {
            TxtAudioStatus.Text = sttMode == "groq" ? "Enter a Groq API key to start audio." : "Enter a Deepgram API key to start audio.";
            return;
        }

        _audioCts = new CancellationTokenSource();
        var token = _audioCts.Token;
        _isAudioMode = true;
        SetSpeechControlsLocked(true);
        BtnStartAudio.Content = LocalizationManager.Instance.Get("BtnStopAudio");
        BtnStartAudio.ClearValue(Button.BackgroundProperty);
        BtnStartAudio.Style = (Style)FindResource("ActionDangerButton");
        CaptionOriginal.Text = "";
        CaptionBar.Visibility = Visibility.Visible;
        ShowSubtitleWithAnimation();
        UpdateProviderUi();

        try
        {
            if (sttMode == "deepgram")
                await StartDeepgramModeAsync();
            else
                await StartWhisperModeAsync(sttMode, token);
        }
        catch (Exception error)
        {
            if (token.IsCancellationRequested) return;
            await StopAudioModeAsync();
            TxtAudioStatus.Text = error.Message;
        }
    }

    private void SetSpeechControlsLocked(bool locked)
    {
        CmbSttMode.IsEnabled = !locked;
        CmbSourceLang.IsEnabled = !locked;
        CmbAudioDevice.IsEnabled = !locked;
        BtnRefreshAudio.IsEnabled = !locked;
        TxtDeepgramKey.IsEnabled = !locked;
        TxtApiKey.IsEnabled = !locked || GetSttMode() != "groq";
    }

    private async Task StartDeepgramModeAsync()
    {
        var loc = LocalizationManager.Instance;
        CaptionTranslated.Text = loc.Get("ConnectingDeepgram");
        CaptionSpeaker.Visibility = Visibility.Visible;
        CaptionSpeaker.Text = loc.Get("SpeakerDetectionOn");

        var service = new DiarizedSttService(TxtDeepgramKey.Password);
        _diarizedSttService = service;
        bool IsCurrent() => _isAudioMode && ReferenceEquals(_diarizedSttService, service);
        string sourceLang = GetSourceLanguage();
        _diarizedSttService.SetLanguage(sourceLang == "auto" ? "multi" : sourceLang);
        
        bool ok = await service.ConnectAsync(msg =>
            Dispatcher.Invoke(() => { if (IsCurrent()) { TxtAudioStatus.Text = msg; CaptionTranslated.Text = msg; } }));

        if (!IsCurrent()) { await service.DisposeAsync(); return; }

        if (!ok) throw new InvalidOperationException(loc.Get("FailedDeepgram"));

        service.TranscriptReady += async (s, args) =>
        {
            if (!IsCurrent()) return;

            string color = args.SpeakerIndex >= 0 ? SpeakerColors[args.SpeakerIndex % SpeakerColors.Length] : "#A1A1AA";
            string speakerLabel = args.SpeakerIndex >= 0 ? LocalizationManager.Instance.Get("SpeakerLabel", args.SpeakerIndex + 1) : LocalizationManager.Instance.Get("SpeakerUnknown");

            if (args.IsInterim)
            {
                // ── INTERIM: display raw transcript immediately, no translation ──
                // This makes words appear in real-time as the person is speaking
                Dispatcher.Invoke(() =>
                {
                    if (!IsCurrent() || _translateSemaphore.CurrentCount == 0) return;
                    CaptionSpeaker.Text = LocalizationManager.Instance.Get("SpeakerPreview");
                    CaptionSpeaker.Foreground = Brushes.Gray;
                    CaptionSpeaker.Visibility = Visibility.Visible;
                    CaptionOriginal.Text = args.Text + "..."; // interim indicator
                    CaptionTranslated.Text = "";
                    ShowSubtitleWithAnimation();
                });
                return; // Don't translate interim — wait for final
            }

            // ── FINAL: sentence is complete, translate it ──
            string targetLang = "", sourceLang = "";
            Dispatcher.Invoke(() => { targetLang = GetTargetLanguage(); sourceLang = GetSourceLanguage(); });

            // Wait up to 5s for translation slot (queue instead of drop)
            if (!await _translateSemaphore.WaitAsync(5000)) return;
            try
            {
                if (!IsCurrent()) return;
                string displayOriginal = await FormatJapaneseTextIfNeededAsync(args.Text, sourceLang);
                if (!IsCurrent()) return;
                Dispatcher.Invoke(() =>
                {
                    if (!IsCurrent()) return;
                    CaptionSpeaker.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                    CaptionSpeaker.Text = speakerLabel;
                    CaptionSpeaker.Visibility = Visibility.Visible;
                    CaptionOriginal.Text = displayOriginal;
                    CaptionTranslated.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
                    CaptionTranslated.Text = "";
                    ShowSubtitleWithAnimation();
                });
                
                string finalTranslation = "";
                await _translationProvider.TranslateStreamAsync(args.Text, targetLang, sourceLang, chunk => 
                {
                    finalTranslation = chunk;
                    Dispatcher.Invoke(() =>
                    {
                        if (!IsCurrent()) return;
                        if (IsWhisperHallucination(chunk))
                        {
                            CaptionTranslated.Text = "";
                            return;
                        }
                        CaptionTranslated.Text = chunk;
                        ShowSubtitleWithAnimation();
                    });
                });

                if (JapaneseScriptConverter.IsMultiScriptLanguage(targetLang) && GetSelectedJapaneseScriptStyle() != JapaneseScriptStyle.Standard)
                {
                    string formatted = await FormatJapaneseTextIfNeededAsync(finalTranslation, targetLang);
                    if (!string.IsNullOrWhiteSpace(formatted) && formatted != finalTranslation)
                    {
                        finalTranslation = formatted;
                        Dispatcher.Invoke(() =>
                        {
                            if (!IsCurrent()) return;
                            CaptionTranslated.Text = finalTranslation;
                        });
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    if (!IsCurrent()) return;
                    AppendToHistory($"[{speakerLabel}] {args.Text}", finalTranslation);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => { if (IsCurrent()) CaptionTranslated.Text = $"[Error: {ex.Message}]"; });
            }
            finally
            {
                _translateSemaphore.Release();
            }
        };

        var capture = new AudioCaptureService();
        _audioCaptureService = capture;
        capture.RawPcmBytesAvailable += async (s, pcmBytes) =>
        {
            if (IsCurrent()) await service.SendAudioAsync(pcmBytes);
        };
        await capture.StartAsync(GetSelectedDeviceId(), GetSelectedProcessId(), _audioCts!.Token);
        if (!IsCurrent()) { capture.Dispose(); return; }
        StartUsageClock(capture);
        TxtAudioStatus.Text = $"Active speech: Deepgram · {DiarizedSttService.ModelName}";
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

    private async Task StartWhisperModeAsync(string mode, CancellationToken sessionToken)
    {
        CaptionSpeaker.Visibility = Visibility.Collapsed;
        string source = GetSourceLanguage();
        LocalSpeechService? localService = null;
        WhisperSttService? cloudService = null;
        if (mode == "groq")
        {
            cloudService = _whisperSttService = new WhisperSttService(TxtApiKey.Password);
            cloudService.RateLimitCooldownStarted += (_, seconds) => Dispatcher.BeginInvoke(() =>
            {
                if (!sessionToken.IsCancellationRequested)
                    TxtAudioStatus.Text = $"Groq · {WhisperSttService.ModelName} · {LocalizationManager.Instance.Get("RateLimitCooldown", seconds)}";
            });
            await cloudService.ChangeLanguageAsync(source);
            if (!await cloudService.InitializeAsync(null))
                throw new InvalidOperationException(LocalizationManager.Instance.Get("FailedWhisper"));
        }
        else
        {
            localService = _localSpeechService = new LocalSpeechService(mode == "local-small");
            await localService.InitializeAsync(source, sessionToken, message => Dispatcher.Invoke(() =>
            {
                if (!sessionToken.IsCancellationRequested)
                    TxtAudioStatus.Text = CaptionTranslated.Text = message;
            }));
        }
        sessionToken.ThrowIfCancellationRequested();

        AcousticSpeakerDetector.Reset();
        var capture = new AudioCaptureService();
        _audioCaptureService = capture;
        capture.AudioChunkHandler = async chunk =>
        {
            if (sessionToken.IsCancellationRequested) return;
            try { await _translateSemaphore.WaitAsync(sessionToken); }
            catch (OperationCanceledException) { return; }
            try
            {
                string targetLang = "", sourceLang = "";
                Dispatcher.Invoke(() => { targetLang = GetTargetLanguage(); sourceLang = GetSourceLanguage(); });

                string transcribed = localService != null
                    ? await localService.TranscribeAsync(chunk, sessionToken)
                    : await cloudService!.ProcessAudioChunkAsync(chunk, sessionToken);
                if (sessionToken.IsCancellationRequested || string.IsNullOrWhiteSpace(transcribed)) return;

                // ── HALLUCINATION FILTER (same as Deepgram path) ──
                // Groq Whisper also hallucinates "subtitles by...", "спасибо", etc. from noise
                if (IsWhisperHallucination(transcribed) || HallucinationDetector.IsWhisperArtifact(transcribed)) return;

                // Whisper has no word-level diarization; keep its local estimates explicit.
                var speaker = AcousticSpeakerDetector.DetectSpeaker(chunk, sessionToken);
                if (sessionToken.IsCancellationRequested) return;
                string speakerLabel = speaker.SpeakerIndex >= 0 ? LocalizationManager.Instance.Get("SpeakerEstimateLabel", speaker.SpeakerIndex + 1) : LocalizationManager.Instance.Get("SpeakerUnknown");

                string displayOriginal = await FormatJapaneseTextIfNeededAsync(transcribed, sourceLang);

                Dispatcher.Invoke(() =>
                {
                    if (!_isAudioMode || sessionToken.IsCancellationRequested) return;
                    CaptionSpeaker.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(speaker.ColorHex));
                    CaptionSpeaker.Text = speakerLabel;
                    CaptionSpeaker.Visibility = Visibility.Visible;
                    CaptionOriginal.Text = displayOriginal;
                    CaptionTranslated.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(speaker.ColorHex));
                    CaptionTranslated.Text = "";
                    ShowSubtitleWithAnimation();
                });

                if (sessionToken.IsCancellationRequested) return;
                
                string finalTranslation = "";
                await _translationProvider.TranslateStreamAsync(transcribed, targetLang, sourceLang, chunk => 
                {
                    finalTranslation = chunk;
                    Dispatcher.Invoke(() =>
                    {
                        if (!_isAudioMode || sessionToken.IsCancellationRequested) return;
                        if (IsWhisperHallucination(chunk))
                        {
                            CaptionTranslated.Text = "";
                            return;
                        }
                        CaptionTranslated.Text = chunk;
                        ShowSubtitleWithAnimation();
                    });
                });

                if (JapaneseScriptConverter.IsMultiScriptLanguage(targetLang) && GetSelectedJapaneseScriptStyle() != JapaneseScriptStyle.Standard)
                {
                    string formatted = await FormatJapaneseTextIfNeededAsync(finalTranslation, targetLang);
                    if (!string.IsNullOrWhiteSpace(formatted) && formatted != finalTranslation)
                    {
                        finalTranslation = formatted;
                        Dispatcher.Invoke(() =>
                        {
                            if (!_isAudioMode || sessionToken.IsCancellationRequested) return;
                            CaptionTranslated.Text = finalTranslation;
                        });
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    if (!_isAudioMode || sessionToken.IsCancellationRequested) return;
                    AppendToHistory($"[{speakerLabel}] {transcribed}", finalTranslation);
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => { if (!sessionToken.IsCancellationRequested) CaptionTranslated.Text = $"[Error: {ex.Message}]"; });
            }
            finally { _translateSemaphore.Release(); }
        };

        await capture.StartAsync(GetSelectedDeviceId(), GetSelectedProcessId(), sessionToken);
        if (sessionToken.IsCancellationRequested) { capture.Dispose(); return; }
        StartUsageClock(capture);
        TxtAudioStatus.Text = $"Active speech: {(localService == null ? $"Groq · {WhisperSttService.ModelName}" : mode == "local-small" ? "Local Whisper small" : "Local Whisper base")}. Speaker labels are estimates.";
        CaptionTranslated.Text = "[Waiting for audio...]";
    }

    private async Task StopAudioModeAsync()
    {
        _isAudioMode = false;
        StopUsageClock();
        BtnStartAudio.IsEnabled = false;
        var cancellation = _audioCts;
        _audioCts = null;
        cancellation?.Cancel();
        _audioCaptureService?.Dispose();
        _audioCaptureService = null;
        AcousticSpeakerDetector.Reset();

        var stoppedLocal = _localSpeechService;
        _localSpeechService = null;
        var stoppedGroq = _whisperSttService;
        _whisperSttService = null;
        var stoppedDeepgram = _diarizedSttService;
        _diarizedSttService = null;

        _subtitleHideTimer.Stop();
        CaptionBar.Visibility = Visibility.Collapsed;
        CaptionBar.Opacity = 0;
        CaptionSpeaker.Visibility = Visibility.Collapsed;
        CaptionOriginal.Text = CaptionTranslated.Text = "";
        CaptionTranslated.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00FF88"));
        _isCaptionVisible = false;
        TxtAudioStatus.Text = "Stopping audio…";
        try
        {
            if (stoppedLocal != null) await stoppedLocal.DisposeAsync();
            if (stoppedGroq != null) await stoppedGroq.DisposeAsync();
            if (stoppedDeepgram != null) await stoppedDeepgram.DisposeAsync();
        }
        finally
        {
            cancellation?.Dispose();
            BtnStartAudio.Content = LocalizationManager.Instance.Get("BtnStartAudio");
            BtnStartAudio.Style = (Style)FindResource("ActionPrimaryButton");
            TxtAudioStatus.Text = "";
            SetSpeechControlsLocked(false);
            BtnStartAudio.IsEnabled = true;
            UpdateProviderUi();
        }
    }

    private void ShowSubtitleWithAnimation()
    {
        _subtitleHideTimer.Stop();
        _subtitleHideTimer.Start();
        
        if (!_isCaptionVisible)
        {
            _fadeInStoryboard?.Begin(this);
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
            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ControlPanel.BeginAnimation(UIElement.OpacityProperty, fade);
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
            ApplyLocalization();
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
        if (ItemUiLangRu != null) ItemUiLangRu.Content = loc.Get("UiLangRu");
        if (ItemUiLangAuto != null) ItemUiLangAuto.Content = loc.Get("UiLangAuto");

        // Silent Auto Update
        if (LblAutoUpdate != null) LblAutoUpdate.Text = loc.Get("LblUpdates");
        if (BtnReleaseNotes != null) BtnReleaseNotes.Content = loc.Get("WhatsNew");
        if (BtnCheckUpdate != null) BtnCheckUpdate.Content = loc.Get("BtnCheckUpdate");
        if (BtnApplyUpdateNow != null && BtnApplyUpdateNow.Visibility == Visibility.Visible)
            BtnApplyUpdateNow.Content = loc.Get("BtnRestartUpdate");
        if (BannerUpdateReady != null && BannerUpdateReady.Visibility == Visibility.Visible)
        {
            TxtBannerUpdateTitle.Text = loc.Get("UpdateAvailable", AutoUpdaterService.Instance.PendingVersion);
            TxtBannerUpdateDesc.Text = loc.Get("BtnRestartUpdate");
            BtnBannerRestart.Content = loc.Get("BtnRestartUpdate");
        }

        // Labels
        if (LblSourceLang != null) LblSourceLang.Text = loc.Get("LblSourceLanguage");
        if (LblTargetLang != null) LblTargetLang.Text = loc.Get("LblTargetLanguage");
        if (LblJapaneseStyle != null) LblJapaneseStyle.Text = loc.Get("LblJapaneseStyle");
        if (ItemJpStandard != null) ItemJpStandard.Content = loc.Get("ItemJpStandard");
        if (ItemJpHiragana != null) ItemJpHiragana.Content = loc.Get("ItemJpHiragana");
        if (ItemJpFurigana != null) ItemJpFurigana.Content = loc.Get("ItemJpFurigana");
        if (ItemJpRomaji != null) ItemJpRomaji.Content = loc.Get("ItemJpRomaji");
        if (ItemJpKatakana != null) ItemJpKatakana.Content = loc.Get("ItemJpKatakana");
        if (LblEngine != null) LblEngine.Text = loc.Get("LblEngine");
        if (LblApiKey != null) LblApiKey.Text = loc.Get("LblApiKey");
        if (BtnVerifyApiKey != null) BtnVerifyApiKey.Content = loc.Get("BtnVerifyKey");
        if (LblDeepgramKey != null) LblDeepgramKey.Text = loc.Get("LblDeepgramKey");
        if (LblGroqModel != null) LblGroqModel.Text = loc.Get("LblGroqModel");
        if (BtnRefreshModels != null) BtnRefreshModels.Content = loc.Get("BtnRefresh");

        if (TxtTitleOcr != null) TxtTitleOcr.Text = loc.Get("TitleOcr");
        if (TxtOcrResultTitle != null) TxtOcrResultTitle.Text = loc.Get("TitleOcr");
        if (BtnCloseOcr != null) BtnCloseOcr.Content = loc.Get("FeedbackClose");
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
        if (ItemSrcId != null) ItemSrcId.Content = loc.Get("LangIndonesian");
        if (ItemSrcJa != null) ItemSrcJa.Content = loc.Get("LangJapanese");
        if (ItemSrcKo != null) ItemSrcKo.Content = loc.Get("LangKorean");
        if (ItemSrcZh != null) ItemSrcZh.Content = loc.Get("LangChineseSimp");
        if (ItemSrcZhTw != null) ItemSrcZhTw.Content = loc.Get("LangChineseTrad");
        if (ItemSrcRu != null) ItemSrcRu.Content = loc.Get("LangRussian");
        if (ItemSrcUk != null) ItemSrcUk.Content = loc.Get("LangUkrainian");
        if (ItemSrcAz != null) ItemSrcAz.Content = loc.Get("LangAzerbaijani");
        if (ItemSrcUz != null) ItemSrcUz.Content = loc.Get("LangUzbek");
        if (ItemSrcMn != null) ItemSrcMn.Content = loc.Get("LangMongolian");
        if (ItemSrcKa != null) ItemSrcKa.Content = loc.Get("LangGeorgian");
        if (ItemSrcSr != null) ItemSrcSr.Content = loc.Get("LangSerbian");
        if (ItemSrcHr != null) ItemSrcHr.Content = loc.Get("LangCroatian");
        if (ItemSrcBs != null) ItemSrcBs.Content = loc.Get("LangBosnian");
        if (ItemSrcBg != null) ItemSrcBg.Content = loc.Get("LangBulgarian");
        if (ItemSrcRo != null) ItemSrcRo.Content = loc.Get("LangRomanian");
        if (ItemSrcEl != null) ItemSrcEl.Content = loc.Get("LangGreek");
        if (ItemSrcSq != null) ItemSrcSq.Content = loc.Get("LangAlbanian");
        if (ItemSrcMk != null) ItemSrcMk.Content = loc.Get("LangMacedonian");
        if (ItemSrcSl != null) ItemSrcSl.Content = loc.Get("LangSlovenian");
        if (ItemSrcEs != null) ItemSrcEs.Content = loc.Get("LangSpanish");
        if (ItemSrcFr != null) ItemSrcFr.Content = loc.Get("LangFrench");
        if (ItemSrcDe != null) ItemSrcDe.Content = loc.Get("LangGerman");
        if (ItemSrcAr != null) ItemSrcAr.Content = loc.Get("LangArabic");

        if (ItemTgtAuto != null) ItemTgtAuto.Content = loc.Get("TargetAuto");
        if (ItemTgtId != null) ItemTgtId.Content = loc.Get("LangIndonesian");
        if (ItemTgtEn != null) ItemTgtEn.Content = loc.Get("LangEnglish");
        if (ItemTgtJa != null) ItemTgtJa.Content = loc.Get("LangJapanese");
        if (ItemTgtKo != null) ItemTgtKo.Content = loc.Get("LangKorean");
        if (ItemTgtZh != null) ItemTgtZh.Content = loc.Get("LangChineseSimp");
        if (ItemTgtZhTw != null) ItemTgtZhTw.Content = loc.Get("LangChineseTrad");
        if (ItemTgtRu != null) ItemTgtRu.Content = loc.Get("LangRussian");
        if (ItemTgtUk != null) ItemTgtUk.Content = loc.Get("LangUkrainian");
        if (ItemTgtAz != null) ItemTgtAz.Content = loc.Get("LangAzerbaijani");
        if (ItemTgtUz != null) ItemTgtUz.Content = loc.Get("LangUzbek");
        if (ItemTgtMn != null) ItemTgtMn.Content = loc.Get("LangMongolian");
        if (ItemTgtKa != null) ItemTgtKa.Content = loc.Get("LangGeorgian");
        if (ItemTgtSr != null) ItemTgtSr.Content = loc.Get("LangSerbian");
        if (ItemTgtHr != null) ItemTgtHr.Content = loc.Get("LangCroatian");
        if (ItemTgtBs != null) ItemTgtBs.Content = loc.Get("LangBosnian");
        if (ItemTgtBg != null) ItemTgtBg.Content = loc.Get("LangBulgarian");
        if (ItemTgtRo != null) ItemTgtRo.Content = loc.Get("LangRomanian");
        if (ItemTgtEl != null) ItemTgtEl.Content = loc.Get("LangGreek");
        if (ItemTgtSq != null) ItemTgtSq.Content = loc.Get("LangAlbanian");
        if (ItemTgtMk != null) ItemTgtMk.Content = loc.Get("LangMacedonian");
        if (ItemTgtSl != null) ItemTgtSl.Content = loc.Get("LangSlovenian");
        if (ItemTgtEs != null) ItemTgtEs.Content = loc.Get("LangSpanish");
        if (ItemTgtFr != null) ItemTgtFr.Content = loc.Get("LangFrench");
        if (ItemTgtDe != null) ItemTgtDe.Content = loc.Get("LangGerman");
        if (ItemTgtAr != null) ItemTgtAr.Content = loc.Get("LangArabic");

        SortLanguageComboBoxAscending(CmbSourceLang);
        SortLanguageComboBoxAscending(CmbTargetLang);
        UpdateScriptStyleVisibilityAndOptions(animate: false);

        if (ItemEngineGoogle != null) ItemEngineGoogle.Content = loc.Get("EngineGoogle");
        if (ItemEngineGroq != null) ItemEngineGroq.Content = loc.Get("EngineGroq");

        if (ItemSttDeepgram != null) ItemSttDeepgram.Content = loc.Get("SttDeepgram");
        if (ItemSttGroq != null) ItemSttGroq.Content = loc.Get("SttGroq");
        if (ItemSttGroq != null) ItemSttGroq.ToolTip = loc.Get("WhisperSpeakerHint");

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

        UpdateApiKeyStatusVisual();
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

        if (_localSpeechService != null)
        {
            await _localSpeechService.DisposeAsync();
            _localSpeechService = null;
        }
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

        SaveCurrentSettings();

        // Opsi A: Seamlessly apply update when closing window


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



