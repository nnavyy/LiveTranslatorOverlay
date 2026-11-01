using System;
using System.Collections.Generic;
using System.Globalization;

namespace LiveTranslatorOverlay.Core.Localization
{
    public class LocalizationManager
    {
        private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
        public static LocalizationManager Instance => _instance.Value;

        public event EventHandler? LanguageChanged;

        private string _configuredLang = "en"; // "en", "id", or "auto"
        private string _activeLang = "en";     // "en", "id", or "ru"

        public string ConfiguredLanguage => _configuredLang;
        public string ActiveLanguage => _activeLang;

        public LocalizationManager()
        {
            using var spanish = typeof(LocalizationManager).Assembly.GetManifestResourceStream("LiveTranslatorOverlay.Core.Localization.es.json")
                ?? throw new InvalidOperationException("Spanish localization resource missing.");
            _strings["es"] = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(spanish)
                ?? throw new InvalidOperationException("Spanish localization resource invalid.");
            _strings["en"]["WhatsNew"] = "What’s new";
            _strings["en"]["StopBeforeUpdate"] = "Stop audio translation and live OCR before installing the update.";
            _strings["en"]["RestartLanguage"] = "Source language changed. Stop and restart audio to apply it.";
            _strings["en"]["ReleaseNotes150"] = "Live Translator Overlay v1.5.0\n\nFIXED\n• Translation cleanup preserves arrows and complete instructions.\n• Changing the Groq model or key clears stale cached output.\n• Truncated or stalled translation streams are not cached as complete.\n• Integer audio is decoded using its actual sample format.\n• Whisper chunk processing now awaits consumers and respects its two-slot limit.\n• Closed history and feedback windows release localization subscriptions.\n• Summary loading windows close on errors; summary windows follow capture privacy.\n\nIMPROVED\n• Feedback delivery is tracked separately: Sending, Sent, Failed, or Email composer opened (not confirmed sent). Older reports have unknown delivery state.\n• Concurrent feedback writes preserve records. Status refresh reloads current records before applying changes.\n• Settings use per-user storage and atomic writes, with migration of existing Windows settings.\n• Session history retains the latest 500 entries and can be cleared or exported.\n\nMAC PREVIEW\n• Text translation, session history, clear/export, and an issue-report link for Apple Silicon and Intel.\n• English interface initially; Groq keys stay in memory for the session.\n• Audio, screen OCR, and capture exclusion are not available on Mac yet.\n• The Mac preview is ad-hoc signed, not Developer ID signed or notarized. It is a developer preview, not a production Mac release.\n\nQUALITY LIMITS\n• Small text smoke checks do not establish general translation or speech accuracy. Real speech, device changes, and screen sharing still require broader testing.\n• API keys in Windows settings remain plaintext. Full updater rollback/signing and native Mac capture remain follow-up work.";
            _strings["id"]["WhatsNew"] = "Yang baru";
            _strings["id"]["StopBeforeUpdate"] = "Hentikan terjemahan audio dan OCR sebelum memasang pembaruan.";
            _strings["id"]["RestartLanguage"] = "Bahasa sumber berubah. Hentikan dan mulai ulang audio untuk menerapkannya.";
            _strings["id"]["ReleaseNotes150"] = "Live Translator Overlay v1.5.0\n\nDIPERBAIKI\n• Tanda panah dan instruksi lengkap tidak lagi terpotong saat membersihkan terjemahan.\n• Penggantian model atau kunci Groq menghapus cache lama.\n• Aliran terjemahan terpotong tidak disimpan sebagai hasil lengkap.\n• Audio integer dibaca sesuai format sampelnya; pemrosesan Whisper menunggu konsumen sesuai batas antrean.\n• Jendela riwayat/umpan balik yang ditutup melepas langganan bahasa.\n• Jendela pemuatan ringkasan ditutup saat gagal; ringkasan mengikuti privasi tangkapan layar.\n\nDITINGKATKAN\n• Status pengiriman laporan dipisahkan dari status penyelesaian masalah.\n• Penyimpanan laporan serentak mempertahankan data; pembaruan status membaca ulang data terkini.\n• Pengaturan disimpan per pengguna secara atomik dengan migrasi pengaturan Windows lama.\n• Riwayat sesi menyimpan 500 entri terbaru dan dapat dihapus atau diekspor.\n\nPRATINJAU MAC\n• Terjemahan teks, riwayat, ekspor, dan tautan laporan masalah untuk Apple Silicon dan Intel.\n• Antarmuka awal berbahasa Inggris; kunci Groq hanya disimpan dalam memori selama sesi.\n• Audio, OCR layar, dan pengecualian tangkapan layar belum tersedia di Mac.\n• Pratinjau ditandatangani ad-hoc dan belum dinotarisasi; bukan rilis Mac produksi.\n\nBATASAN\n• Pengujian teks singkat bukan ukuran akurasi umum atau transkripsi suara.\n• Kunci API Windows masih berupa teks biasa; pemulihan pembaruan dan tangkapan native Mac masih perlu dikerjakan.";
            _strings["ru"]["WhatsNew"] = "Что нового";
            _strings["ru"]["StopBeforeUpdate"] = "Остановите перевод аудио и OCR перед установкой обновления.";
            _strings["ru"]["RestartLanguage"] = "Язык изменён. Остановите и снова запустите аудио.";
            _strings["ru"]["ReleaseNotes150"] = "Live Translator Overlay v1.5.0\n\nИСПРАВЛЕНО\n• При очистке перевода сохраняются стрелки и полные инструкции.\n• Смена модели или ключа Groq сбрасывает старый кэш.\n• Обрезанный поток не кэшируется как завершённый перевод.\n• Целочисленное аудио декодируется согласно формату; обработка Whisper ожидает завершения потребителей очереди.\n• Закрытые окна освобождают подписки на смену языка.\n• Окно загрузки резюме закрывается при ошибках; резюме учитывает исключение из захвата.\n\nУЛУЧШЕНО\n• Доставка отчёта и решение проблемы имеют отдельные статусы.\n• Параллельная запись отчётов сохраняет данные; обновление статусов перечитывает актуальную историю.\n• Настройки пользователя записываются атомарно с переносом старых настроек Windows.\n• История хранит последние 500 записей, поддерживает очистку и экспорт.\n\nПРЕДВАРИТЕЛЬНАЯ ВЕРСИЯ ДЛЯ MAC\n• Перевод текста, история, экспорт и ссылка на отчёт об ошибке для Apple Silicon и Intel.\n• Интерфейс пока на английском; ключ Groq хранится только в памяти сеанса.\n• Аудио, OCR и исключение из захвата на Mac пока недоступны.\n• Подпись ad-hoc, без нотариального заверения: это версия для разработчиков.\n\nОГРАНИЧЕНИЯ\n• Короткие текстовые проверки не оценивают общую точность или распознавание речи.\n• Ключи Windows пока хранятся открыто; восстановление обновлений и нативный захват Mac требуют доработки.";
            _strings["es"]["WhatsNew"] = "Novedades";
            _strings["es"]["StopBeforeUpdate"] = "Detén la traducción de audio y el OCR antes de instalar la actualización.";
            _strings["es"]["RestartLanguage"] = "Idioma de origen cambiado. Detén y reinicia el audio para aplicarlo.";
            _strings["es"]["ReleaseNotes150"] = "Live Translator Overlay v1.5.0\n\nCORREGIDO\n• Se conservan las flechas y las instrucciones completas al limpiar traducciones.\n• Cambiar el modelo o la clave de Groq invalida la caché anterior.\n• Los flujos truncados no se guardan como traducciones completas.\n• El audio entero se decodifica según su formato; Whisper espera a los consumidores de su cola.\n• Las ventanas cerradas liberan sus suscripciones de idioma.\n• La ventana de carga del resumen se cierra también tras errores y los resúmenes respetan la privacidad de captura.\n\nMEJORADO\n• El estado de envío de un informe se distingue de su resolución.\n• Las escrituras concurrentes conservan los informes y la actualización de estados vuelve a leer los datos actuales.\n• Ajustes por usuario con escritura atómica y migración de los ajustes de Windows.\n• Historial de las últimas 500 entradas, con borrado y exportación.\n\nVISTA PREVIA PARA MAC\n• Traducción de texto, historial, exportación y enlace para informar de problemas en Apple Silicon e Intel.\n• Interfaz inicial en inglés; la clave de Groq permanece solo en memoria durante la sesión.\n• Aún no incluye audio, OCR ni exclusión de captura en Mac.\n• Firma ad-hoc, sin notarización: es una vista previa de desarrollo.\n\nLIMITACIONES\n• Las pruebas breves de texto no demuestran precisión general ni de transcripción.\n• Las claves de Windows siguen en texto plano; quedan pendientes la recuperación del actualizador y la captura nativa de Mac.";
            _strings["en"]["EngineGoogle"] = "Google Translate (No key; service limits apply)";
            _strings["en"]["DeepgramTooltip"] = "A Deepgram account and API key are required; check your plan limits.";
            _strings["id"]["EngineGoogle"] = "Google Translate (Tanpa kunci; mengikuti batas layanan)";
            _strings["id"]["DeepgramTooltip"] = "Akun dan kunci API Deepgram diperlukan; periksa batas paket Anda.";
            _strings["ru"]["EngineGoogle"] = "Google Translate (Без ключа; действуют ограничения сервиса)";
            _strings["ru"]["DeepgramTooltip"] = "Нужны аккаунт и ключ Deepgram; проверьте ограничения тарифа.";
            _strings["en"]["DeliverySending"] = "Sending report…";
            _strings["en"]["DeliverySent"] = "Delivery accepted by service";
            _strings["en"]["DeliveryFailed"] = "Delivery failed — report was not confirmed sent";
            _strings["en"]["DeliveryComposerOpened"] = "Email composer opened — sending is not confirmed";
            _strings["en"]["DeliveryUnknown"] = "Delivery unknown (older report)";
            _strings["id"]["DeliverySending"] = "Mengirim laporan…";
            _strings["id"]["DeliverySent"] = "Layanan menerima pengiriman";
            _strings["id"]["DeliveryFailed"] = "Pengiriman gagal — laporan belum terkonfirmasi terkirim";
            _strings["id"]["DeliveryComposerOpened"] = "Aplikasi email dibuka — pengiriman belum terkonfirmasi";
            _strings["id"]["DeliveryUnknown"] = "Status pengiriman tidak diketahui (laporan lama)";
            _strings["es"]["DeliverySending"] = "Enviando informe…";
            _strings["es"]["DeliverySent"] = "El servicio aceptó el envío";
            _strings["es"]["DeliveryFailed"] = "Envío fallido: no se confirmó la entrega";
            _strings["es"]["DeliveryComposerOpened"] = "Correo abierto: no se confirmó el envío";
            _strings["es"]["DeliveryUnknown"] = "Entrega desconocida (informe anterior)";
            _strings["ru"]["DeliverySending"] = "Отправка отчёта…";
            _strings["ru"]["DeliverySent"] = "Сервис принял отправку";
            _strings["ru"]["DeliveryFailed"] = "Ошибка доставки — отправка не подтверждена";
            _strings["ru"]["DeliveryComposerOpened"] = "Открыт почтовый клиент — отправка не подтверждена";
            _strings["ru"]["DeliveryUnknown"] = "Доставка неизвестна (старый отчёт)";
            SetLanguage("en");
        }

        public void SetLanguage(string langCode)
        {
            _configuredLang = string.IsNullOrWhiteSpace(langCode) ? "en" : langCode.ToLowerInvariant();

            if (_configuredLang == "auto")
            {
                string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                string sysInstalled = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                
                if (sys == "es")
                    _activeLang = "es";
                else if (sys == "ru" || sysInstalled == "ru")
                    _activeLang = "ru";
                else if (sys == "id" || sysInstalled == "id")
                    _activeLang = "id";
                else
                    _activeLang = "en";
            }
            else if (_configuredLang == "es")
            {
                _activeLang = "es";
            }
            else if (_configuredLang == "ru")
            {
                _activeLang = "ru";
            }
            else if (_configuredLang == "id")
            {
                _activeLang = "id";
            }
            else
            {
                _activeLang = "en";
            }

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public string Get(string key, params object[] args)
        {
            if (_strings.TryGetValue(_activeLang, out var dict) && dict.TryGetValue(key, out var val))
            {
                return args.Length > 0 ? string.Format(val, args) : val;
            }
            if (_strings["en"].TryGetValue(key, out var fallback))
            {
                return args.Length > 0 ? string.Format(fallback, args) : fallback;
            }
            return key;
        }

        private readonly Dictionary<string, Dictionary<string, string>> _strings = new()
        {
            ["en"] = new Dictionary<string, string>
            {
                // Top Header
                ["BtnSettings"] = "Settings",
                ["BtnShowSettings"] = "Show Settings",
                ["BtnHideSettings"] = "Hide Settings",
                ["BtnHideAllWindows"] = "Hide All Windows",
                ["BtnShowAllWindows"] = "Show All Windows",
                ["DragTooltip"] = "Drag to move settings panel",
                ["HideAllTooltip"] = "Toggle hide/show all windows",
                ["AppTitle"] = "Live Translator",

                // Language & UI Settings
                ["LblUiLanguage"] = "UI Language:",
                ["LblSourceLanguage"] = "Source Language:",
                ["LblTargetLanguage"] = "Target Language:",
                ["AutoDetect"] = "Auto Detect",
                ["LangEnglish"] = "English (en)",
                ["LangJapanese"] = "Japanese (ja)",
                ["LangKorean"] = "Korean (ko)",
                ["LangChinese"] = "Chinese Simplified (zh)",
                ["LangChineseSimp"] = "Chinese Simplified (zh)",
                ["LangChineseTrad"] = "Chinese Traditional (zh-TW)",
                ["LangIndonesian"] = "Indonesian (id)",
                ["LangSpanish"] = "Spanish (es)",
                ["LangFrench"] = "French (fr)",
                ["LangGerman"] = "German (de)",
                ["LangRussian"] = "Russian (ru)",
                ["LangUkrainian"] = "Ukrainian (uk)",
                ["LangAzerbaijani"] = "Azerbaijani (az)",
                ["LangUzbek"] = "Uzbek (uz)",
                ["LangMongolian"] = "Mongolian (mn)",
                ["LangGeorgian"] = "Georgian (ka)",
                ["LangSerbian"] = "Serbian (sr)",
                ["LangCroatian"] = "Croatian (hr)",
                ["LangBosnian"] = "Bosnian (bs)",
                ["LangBulgarian"] = "Bulgarian (bg)",
                ["LangRomanian"] = "Romanian (ro)",
                ["LangGreek"] = "Greek (el)",
                ["LangAlbanian"] = "Albanian (sq)",
                ["LangMacedonian"] = "Macedonian (mk)",
                ["LangSlovenian"] = "Slovenian (sl)",
                ["LangArabic"] = "Arabic (ar)",

                // Engine
                ["LblEngine"] = "Translation Engine:",
                ["EngineGoogle"] = "Google Translate (Free & Fast, No Quota)",
                ["EngineGroq"] = "Groq AI (Requires API Key, Limited Quota)",
                ["LblApiKey"] = "API Key (Groq):",
                ["LblDeepgramKey"] = "Deepgram API Key:",
                ["DeepgramTooltip"] = "Sign up for free at deepgram.com - 12,000 minutes/month",
                ["LblGroqModel"] = "Groq Model:",
                ["ModelAuto"] = "(Auto)",
                ["BtnRefresh"] = "Refresh",
                ["RefreshModelsTooltip"] = "Refresh model list",

                // Screen OCR
                ["TitleOcr"] = "Screen OCR",
                ["BtnSelectArea"] = "Select Screen Area",
                ["BtnStartOcr"] = "Start Live OCR",
                ["BtnStopOcr"] = "Stop Live OCR",
                ["OcrPlaceholder"] = "[Translation will appear here]",
                ["OcrStarting"] = "Starting OCR...",
                ["OcrStopped"] = "[OCR Stopped]",
                ["OcrSelectPrompt"] = "Please click 'Select Screen Area' and draw an area first.",

                // Audio STT
                ["TitleAudio"] = "Audio Transcription",
                ["LblSttMode"] = "STT Mode:",
                ["SttDeepgram"] = "Deepgram (Streaming + Speaker)",
                ["SttGroq"] = "Groq Whisper (Batch + Speaker)",
                ["LblFontSize"] = "Subtitle Font Size:",
                ["FontSmall"] = "Small (14px)",
                ["FontMedium"] = "Medium (20px)",
                ["FontLarge"] = "Large (26px)",
                ["FontExtraLarge"] = "Extra Large (36px)",
                ["LblAudioDevice"] = "Audio Capture Device:",
                ["DefaultAudioDevice"] = "Default System Playback",
                ["RefreshAudioTooltip"] = "Refresh audio devices",
                ["BtnStartAudio"] = "Start Audio Translate",
                ["BtnStopAudio"] = "Stop Audio Translate",
                ["SpeakerDetectionOn"] = "Speaker Detection: ON",
                ["WaitingForAudio"] = "[Waiting for audio...]",
                ["ConnectingDeepgram"] = "Connecting to Deepgram...",
                ["FailedDeepgram"] = "Failed to connect to Deepgram. Check API key.",
                ["FailedWhisper"] = "Failed to initialize Groq Whisper.",
                ["RateLimitCooldown"] = "Groq rate limit reached (cooldown {0}s)...",
                ["DeviceOutput"] = "Output: {0}",
                ["DeviceMic"] = "Mic: {0}",
                ["DeviceApp"] = "App: {0} - {1}",
                ["SpeakerLabel"] = "Speaker {0}",

                // API Quota
                ["TitleQuota"] = "API Quota",
                ["QuotaUnlimited"] = "Unlimited",
                ["QuotaTracking"] = "Tracking...",
                ["QuotaCooldown"] = "Cooldown: {0}s",

                // Extras & Preferences
                ["TxtToggleHideTitle"] = "Hide All Windows",
                ["TxtToggleHideDesc"] = "Hide overlay & all windows from desktop",
                ["TxtTitlePreferences"] = "App Preferences",
                ["UiLangEn"] = "English (Default)",
                ["UiLangId"] = "Indonesian (Bahasa Indonesia)",
                ["UiLangRu"] = "Russian (Русский)",
                ["UiLangAuto"] = "Auto (System Default)",
                ["TargetAuto"] = "Auto (System Language)",
                ["BtnOpenHistory"] = "Open Translation History",
                ["BtnFeedback"] = "Report Bug / Feedback",
                ["BtnCloseApp"] = "Close App",

                // Japanese & Multi-Script Style
                ["LblJapaneseStyle"] = "Japanese Script Style:",
                ["LblKoreanStyle"] = "Korean Script Style:",
                ["LblChineseStyle"] = "Chinese Script Style:",
                ["LblDualScriptStyle"] = "Writing Script Style:",
                ["ItemJpStandard"] = "Standard (Kanji + Kana Mix)",
                ["ItemJpHiragana"] = "Hiragana / Kana Mix (ひらがな)",
                ["ItemJpFurigana"] = "Furigana: 漢字(かんじ)",
                ["ItemJpRomaji"] = "Romaji (Latin ABC)",
                ["ItemJpKatakana"] = "Katakana (カタカナ)",
                ["ItemKoStandard"] = "Hangul Standard (한글)",
                ["ItemKoFurigana"] = "Hangul + Romaja: 한글(hangeul)",
                ["ItemKoRomaji"] = "Romaja (Revised Romanization)",
                ["ItemKoHanja"] = "Hangul + Hanja: 한글(漢字)",
                ["ItemZhStandard"] = "Simplified Hanzi (简体中文)",
                ["ItemZhTrad"] = "Traditional Hanzi (繁體中文)",
                ["ItemZhFurigana"] = "Hanzi + Pinyin: 汉字(hànzì)",
                ["ItemZhRomaji"] = "Pinyin Only (Hànyǔ Pīnyīn)",
                ["ItemDualStandard"] = "Standard (Native Default)",
                ["ItemDualLatin"] = "Latin Script (Latinica / Lotin)",
                ["ItemDualCyrillic"] = "Cyrillic Script (Ћирилица / Кирилл)",

                // API Key & Auto Update
                ["BtnVerifyKey"] = "Verify",
                ["ApiKeyEmpty"] = "Free Google Translate active",
                ["ApiKeyChecking"] = "Verifying key with Groq...",
                ["ApiKeyValid"] = "Groq AI connected and active",
                ["ApiKeyInvalid"] = "Invalid Groq API key",
                ["LblUpdates"] = "App Updates:",
                ["BtnCheckUpdate"] = "Check",
                ["UpdateChecking"] = "Checking for updates...",
                ["UpdateUpToDate"] = "Up to date ({0})",
                ["UpdateAvailable"] = "Update {0} ready",
                ["BtnRestartUpdate"] = "Restart to apply",

                // Feedback & Bug Report
                ["FeedbackTitle"] = "Report Bug & Feedback",
                ["FeedbackSubtitle"] = "Send bug reports, suggestions, or feedback to developer",
                ["FeedbackType"] = "Report Type:",
                ["FeedbackTypeBug"] = "Bug Report",
                ["FeedbackTypeFeature"] = "Feature Suggestion",
                ["FeedbackTypeGeneral"] = "General Feedback",
                ["FeedbackContact"] = "Your Email (Optional):",
                ["FeedbackSubject"] = "Subject:",
                ["FeedbackMessage"] = "Description / Details:",
                ["FeedbackSend"] = "Send Report",
                ["FeedbackMailto"] = "Open Email App",
                ["FeedbackClose"] = "Close",
                ["FeedbackSending"] = "Sending email, please wait...",
                ["FeedbackSuccess"] = "Email sent successfully.",
                ["FeedbackError"] = "Failed to send email. Please check your internet connection or use Open Email App.",
                ["FeedbackEmpty"] = "Please fill in the Subject and Description.",
                ["FeedbackMailtoError"] = "Failed to open email client: {0}",
                ["FeedbackTabSubmit"] = "New Report",
                ["FeedbackTabHistory"] = "My Reports ({0})",
                ["FeedbackHistoryEmpty"] = "No reports submitted from this device yet.",
                ["FeedbackStatusPending"] = "Pending",
                ["FeedbackStatusInProgress"] = "In Progress",
                ["FeedbackStatusFixed"] = "Fixed",
                ["FeedbackRefreshStatus"] = "Refresh Status",
                ["FeedbackRefreshing"] = "Checking updates...",
                ["FeedbackRefreshed"] = "Status up to date.",
                ["FeedbackReportId"] = "ID: {0}",
                ["FeedbackReportDate"] = "Reported: {0}",

                // History Window
                ["HistoryTitle"] = "Live Transcript & Translator",
                ["BtnSummarize"] = "AI Summary",
                ["BtnClear"] = "Clear",
                ["BtnSave"] = "Save to File",
                ["MsgEmptyHistory"] = "No conversation history to summarize yet.",
                ["TitleEmpty"] = "Empty",
                ["MsgMissingApiKey"] = "Groq API Key is not set in the main menu.",
                ["TitleError"] = "Error",
                ["WindowSummarizingTitle"] = "AI Summarizing...",
                ["TxtSummarizing"] = "Summarizing transcript...\nPlease wait a moment.",
                ["WindowSummaryTitle"] = "Transcript Summary (AI Summary)",
                ["MsgSavedSuccess"] = "Saved to:\n{0}",
                ["TitleSuccess"] = "Success",
                ["MsgSaveFailed"] = "Failed to save: {0}",
                ["AiSummaryPrompt"] = "You are a professional AI Assistant. Below is a live transcript of conversation/audio/translation. Your task: 1. Create a clear, concise Executive Summary. 2. List Key Highlights / Bullet Points. 3. List any action items or key takeaways if applicable. Format cleanly and professionally in English."
            },
            ["id"] = new Dictionary<string, string>
            {
                // Top Header
                ["BtnSettings"] = "Pengaturan",
                ["BtnShowSettings"] = "Tampilkan Pengaturan",
                ["BtnHideSettings"] = "Sembunyikan Pengaturan",
                ["BtnHideAllWindows"] = "Sembunyikan Semua Jendela",
                ["BtnShowAllWindows"] = "Tampilkan Semua Jendela",
                ["DragTooltip"] = "Geser untuk memindahkan panel pengaturan",
                ["HideAllTooltip"] = "Sembunyikan atau tampilkan semua jendela",
                ["AppTitle"] = "Live Translator",

                // Language & UI Settings
                ["LblUiLanguage"] = "Bahasa Tampilan:",
                ["LblSourceLanguage"] = "Bahasa Asal:",
                ["LblTargetLanguage"] = "Bahasa Terjemahan:",
                ["AutoDetect"] = "Deteksi Otomatis",
                ["LangEnglish"] = "Inggris (en)",
                ["LangJapanese"] = "Jepang (ja)",
                ["LangKorean"] = "Korea (ko)",
                ["LangChinese"] = "Mandarin Sederhana (zh)",
                ["LangChineseSimp"] = "Mandarin Sederhana (zh)",
                ["LangChineseTrad"] = "Mandarin Tradisional (zh-TW)",
                ["LangIndonesian"] = "Indonesia (id)",
                ["LangSpanish"] = "Spanyol (es)",
                ["LangFrench"] = "Prancis (fr)",
                ["LangGerman"] = "Jerman (de)",
                ["LangRussian"] = "Rusia (ru)",
                ["LangUkrainian"] = "Ukraina (uk)",
                ["LangAzerbaijani"] = "Azerbaijan (az)",
                ["LangUzbek"] = "Uzbekistan (uz)",
                ["LangMongolian"] = "Mongolia (mn)",
                ["LangGeorgian"] = "Georgia (ka)",
                ["LangSerbian"] = "Serbia (sr)",
                ["LangCroatian"] = "Kroasia (hr)",
                ["LangBosnian"] = "Bosnia (bs)",
                ["LangBulgarian"] = "Bulgaria (bg)",
                ["LangRomanian"] = "Rumania (ro)",
                ["LangGreek"] = "Yunani (el)",
                ["LangAlbanian"] = "Albania (sq)",
                ["LangMacedonian"] = "Makedonia (mk)",
                ["LangSlovenian"] = "Slovenia (sl)",
                ["LangArabic"] = "Arab (ar)",

                // Engine
                ["LblEngine"] = "Mesin Terjemahan:",
                ["EngineGoogle"] = "Google Translate (Gratis & Cepat, Tanpa Kuota)",
                ["EngineGroq"] = "Groq AI (Perlu API Key, Kuota Terbatas)",
                ["LblApiKey"] = "API Key (Groq):",
                ["LblDeepgramKey"] = "Deepgram API Key:",
                ["DeepgramTooltip"] = "Daftar gratis di deepgram.com - 12.000 menit/bulan",
                ["LblGroqModel"] = "Model Groq:",
                ["ModelAuto"] = "(Otomatis)",
                ["BtnRefresh"] = "Segarkan",
                ["RefreshModelsTooltip"] = "Segarkan daftar model",

                // Screen OCR
                ["TitleOcr"] = "Screen OCR",
                ["BtnSelectArea"] = "Pilih Area Layar",
                ["BtnStartOcr"] = "Mulai Live OCR",
                ["BtnStopOcr"] = "Hentikan Live OCR",
                ["OcrPlaceholder"] = "[Terjemahan akan muncul di sini]",
                ["OcrStarting"] = "Memulai OCR...",
                ["OcrStopped"] = "[OCR Berhenti]",
                ["OcrSelectPrompt"] = "Silakan klik 'Pilih Area Layar' dan gambar area terlebih dahulu.",

                // Audio STT
                ["TitleAudio"] = "Transkripsi Audio",
                ["LblSttMode"] = "Mode STT:",
                ["SttDeepgram"] = "Deepgram (Streaming + Speaker)",
                ["SttGroq"] = "Groq Whisper (Batch + Speaker)",
                ["LblFontSize"] = "Ukuran Font Subtitle:",
                ["FontSmall"] = "Kecil (14px)",
                ["FontMedium"] = "Sedang (20px)",
                ["FontLarge"] = "Besar (26px)",
                ["FontExtraLarge"] = "Sangat Besar (36px)",
                ["LblAudioDevice"] = "Perangkat Rekam Audio:",
                ["DefaultAudioDevice"] = "Pemutaran Sistem Default",
                ["RefreshAudioTooltip"] = "Segarkan daftar perangkat audio",
                ["BtnStartAudio"] = "Mulai Terjemah Audio",
                ["BtnStopAudio"] = "Hentikan Terjemah Audio",
                ["SpeakerDetectionOn"] = "Deteksi Pembicara: ON",
                ["WaitingForAudio"] = "[Menunggu audio...]",
                ["ConnectingDeepgram"] = "Menghubungkan ke Deepgram...",
                ["FailedDeepgram"] = "Gagal terhubung ke Deepgram. Periksa API key.",
                ["FailedWhisper"] = "Gagal menginisialisasi Groq Whisper.",
                ["RateLimitCooldown"] = "Batas kuota Groq tercapai (tunggu {0}d)...",
                ["DeviceOutput"] = "Output: {0}",
                ["DeviceMic"] = "Mic: {0}",
                ["DeviceApp"] = "App: {0} - {1}",
                ["SpeakerLabel"] = "Pembicara {0}",

                // API Quota
                ["TitleQuota"] = "Kuota API",
                ["QuotaUnlimited"] = "Tanpa Batas",
                ["QuotaTracking"] = "Melacak...",
                ["QuotaCooldown"] = "Jeda: {0}d",

                // Extras & Preferences
                ["TxtToggleHideTitle"] = "Sembunyikan Semua Jendela",
                ["TxtToggleHideDesc"] = "Sembunyikan overlay & semua jendela dari desktop",
                ["TxtTitlePreferences"] = "Preferensi Aplikasi",
                ["UiLangEn"] = "Inggris (Default)",
                ["UiLangId"] = "Bahasa Indonesia",
                ["UiLangRu"] = "Rusia (Русский)",
                ["UiLangAuto"] = "Otomatis (Sesuai Sistem)",
                ["TargetAuto"] = "Otomatis (Sesuai Sistem)",
                ["BtnOpenHistory"] = "Buka Riwayat Terjemahan",
                ["BtnFeedback"] = "Lapor Bug / Saran",
                ["BtnCloseApp"] = "Tutup Aplikasi",

                // Japanese & Multi-Script Style
                ["LblJapaneseStyle"] = "Gaya Huruf Jepang:",
                ["LblKoreanStyle"] = "Gaya Huruf Korea:",
                ["LblChineseStyle"] = "Gaya Huruf Mandarin:",
                ["LblDualScriptStyle"] = "Gaya Aksara Tulisan:",
                ["ItemJpStandard"] = "Standar (Kanji + Campuran Kana)",
                ["ItemJpHiragana"] = "Hiragana / Campuran Kana (ひらがな)",
                ["ItemJpFurigana"] = "Furigana: 漢字(かんじ)",
                ["ItemJpRomaji"] = "Romaji (Alfabet Latin ABC)",
                ["ItemJpKatakana"] = "Katakana (カタカナ)",
                ["ItemKoStandard"] = "Hangul Standar (한글)",
                ["ItemKoFurigana"] = "Hangul + Romaja: 한글(hangeul)",
                ["ItemKoRomaji"] = "Romaja (Alfabet Latin)",
                ["ItemKoHanja"] = "Hangul + Hanja: 한글(漢字)",
                ["ItemZhStandard"] = "Hanzi Sederhana (简体中文)",
                ["ItemZhTrad"] = "Hanzi Tradisional (繁體中文)",
                ["ItemZhFurigana"] = "Hanzi + Pinyin: 汉字(hànzì)",
                ["ItemZhRomaji"] = "Pinyin Saja (Hànyǔ Pīnyīn)",
                ["ItemDualStandard"] = "Standar (Aksara Bawaan)",
                ["ItemDualLatin"] = "Aksara Latin (Latinica / Lotin)",
                ["ItemDualCyrillic"] = "Aksara Sirilik (Ћирилица / Кирилл)",

                // API Key & Auto Update
                ["BtnVerifyKey"] = "Verifikasi",
                ["ApiKeyEmpty"] = "Google Translate gratis aktif",
                ["ApiKeyChecking"] = "Memverifikasi key ke Groq...",
                ["ApiKeyValid"] = "Groq AI terhubung dan aktif",
                ["ApiKeyInvalid"] = "API Key Groq tidak valid",
                ["LblUpdates"] = "Pembaruan:",
                ["BtnCheckUpdate"] = "Periksa",
                ["UpdateChecking"] = "Memeriksa pembaruan...",
                ["UpdateUpToDate"] = "Versi terbaru ({0})",
                ["UpdateAvailable"] = "Pembaruan {0} siap",
                ["BtnRestartUpdate"] = "Restart untuk pasang",

                // Feedback & Bug Report
                ["FeedbackTitle"] = "Lapor Bug & Saran",
                ["FeedbackSubtitle"] = "Kirim laporan bug, saran, atau masukan ke developer",
                ["FeedbackType"] = "Tipe Laporan:",
                ["FeedbackTypeBug"] = "Laporan Bug",
                ["FeedbackTypeFeature"] = "Saran Fitur",
                ["FeedbackTypeGeneral"] = "Kritik & Masukan",
                ["FeedbackContact"] = "Email Anda (Opsional):",
                ["FeedbackSubject"] = "Judul / Subjek:",
                ["FeedbackMessage"] = "Deskripsi / Detail:",
                ["FeedbackSend"] = "Kirim Laporan",
                ["FeedbackMailto"] = "Buka Aplikasi Email",
                ["FeedbackClose"] = "Tutup",
                ["FeedbackSending"] = "Sedang mengirim email, mohon tunggu...",
                ["FeedbackSuccess"] = "Email berhasil dikirim.",
                ["FeedbackError"] = "Gagal mengirim email. Periksa koneksi internet atau gunakan Buka Aplikasi Email.",
                ["FeedbackEmpty"] = "Harap isi Judul dan Deskripsi terlebih dahulu.",
                ["FeedbackMailtoError"] = "Gagal membuka aplikasi email: {0}",
                ["FeedbackTabSubmit"] = "Laporan Baru",
                ["FeedbackTabHistory"] = "Riwayat Laporan ({0})",
                ["FeedbackHistoryEmpty"] = "Belum ada laporan yang dikirim dari perangkat ini.",
                ["FeedbackStatusPending"] = "Menunggu",
                ["FeedbackStatusInProgress"] = "Sedang Diproses",
                ["FeedbackStatusFixed"] = "Selesai",
                ["FeedbackRefreshStatus"] = "Periksa Status",
                ["FeedbackRefreshing"] = "Memeriksa status terbaru...",
                ["FeedbackRefreshed"] = "Status sudah diperbarui.",
                ["FeedbackReportId"] = "ID: {0}",
                ["FeedbackReportDate"] = "Dilaporkan: {0}",

                // History Window
                ["HistoryTitle"] = "Transkrip Langsung & Terjemahan",
                ["BtnSummarize"] = "Ringkasan AI",
                ["BtnClear"] = "Hapus",
                ["BtnSave"] = "Simpan ke File",
                ["MsgEmptyHistory"] = "Belum ada riwayat percakapan untuk dirangkum.",
                ["TitleEmpty"] = "Kosong",
                ["MsgMissingApiKey"] = "API Key Groq belum diisi di menu utama.",
                ["TitleError"] = "Error",
                ["WindowSummarizingTitle"] = "AI Sedang Merangkum...",
                ["TxtSummarizing"] = "Sedang merangkum transkrip...\nHarap tunggu sebentar.",
                ["WindowSummaryTitle"] = "Ringkasan Transkrip (AI Summary)",
                ["MsgSavedSuccess"] = "Disimpan ke:\n{0}",
                ["TitleSuccess"] = "Berhasil",
                ["MsgSaveFailed"] = "Gagal menyimpan: {0}",
                ["AiSummaryPrompt"] = "Kamu adalah AI Assistant profesional. Berikut adalah transkrip percakapan/audio/terjemahan langsung. Tugasmu: 1. Buat Ringkasan Inti (Summary) yang padat, jelas, dan akurat. 2. Tulis Poin-Poin Penting (Key Highlights). 3. Tulis detail penting atau aksi lanjutan jika ada. Format output dengan rapi, profesional, dan mudah dipahami dalam bahasa Indonesia."
            },
            ["ru"] = new Dictionary<string, string>
            {
                // Top Header
                ["BtnSettings"] = "Настройки",
                ["BtnShowSettings"] = "Показать настройки",
                ["BtnHideSettings"] = "Скрыть настройки",
                ["BtnHideAllWindows"] = "Скрыть все окна",
                ["BtnShowAllWindows"] = "Показать все окна",
                ["DragTooltip"] = "Потяните для перемещения панели",
                ["HideAllTooltip"] = "Скрыть или показать все окна",
                ["AppTitle"] = "Live Translator",

                // Language & UI Settings
                ["LblUiLanguage"] = "Язык интерфейса:",
                ["LblSourceLanguage"] = "Исходный язык:",
                ["LblTargetLanguage"] = "Язык перевода:",
                ["AutoDetect"] = "Автоопределение",
                ["TargetAuto"] = "Авто (Язык системы)",
                ["LangEnglish"] = "Английский (en)",
                ["LangJapanese"] = "Японский (ja)",
                ["LangKorean"] = "Корейский (ko)",
                ["LangChinese"] = "Китайский упрощённый (zh)",
                ["LangChineseSimp"] = "Китайский упрощённый (zh)",
                ["LangChineseTrad"] = "Китайский традиционный (zh-TW)",
                ["LangIndonesian"] = "Индонезийский (id)",
                ["LangSpanish"] = "Испанский (es)",
                ["LangFrench"] = "Французский (fr)",
                ["LangGerman"] = "Немецкий (de)",
                ["LangRussian"] = "Русский (ru)",
                ["LangUkrainian"] = "Украинский (uk)",
                ["LangAzerbaijani"] = "Азербайджанский (az)",
                ["LangUzbek"] = "Узбекский (uz)",
                ["LangMongolian"] = "Монгольский (mn)",
                ["LangGeorgian"] = "Грузинский (ka)",
                ["LangSerbian"] = "Сербский (sr)",
                ["LangCroatian"] = "Хорватский (hr)",
                ["LangBosnian"] = "Боснийский (bs)",
                ["LangBulgarian"] = "Болгарский (bg)",
                ["LangRomanian"] = "Румынский (ro)",
                ["LangGreek"] = "Греческий (el)",
                ["LangAlbanian"] = "Албанский (sq)",
                ["LangMacedonian"] = "Македонский (mk)",
                ["LangSlovenian"] = "Словенский (sl)",
                ["LangArabic"] = "Арабский (ar)",

                // Engine
                ["LblEngine"] = "Движок перевода:",
                ["EngineGoogle"] = "Google Translate (Бесплатно, без квоты)",
                ["EngineGroq"] = "Groq AI (Нужен API-ключ, точный перевод)",
                ["LblApiKey"] = "API-ключ (Groq):",
                ["LblDeepgramKey"] = "API-ключ Deepgram:",
                ["DeepgramTooltip"] = "Бесплатная регистрация на deepgram.com - 12 000 мин/месяц",
                ["LblGroqModel"] = "Модель Groq:",
                ["ModelAuto"] = "(Авто)",
                ["BtnRefresh"] = "Обновить",
                ["RefreshModelsTooltip"] = "Обновить список моделей",

                // Screen OCR
                ["TitleOcr"] = "Экранный OCR",
                ["BtnSelectArea"] = "Выбрать область экрана",
                ["BtnStartOcr"] = "Запустить Live OCR",
                ["BtnStopOcr"] = "Остановить Live OCR",
                ["OcrPlaceholder"] = "[Перевод появится здесь]",
                ["OcrStarting"] = "Запуск OCR...",
                ["OcrStopped"] = "[OCR остановлен]",
                ["OcrSelectPrompt"] = "Нажмите «Выбрать область экрана» и выделите область.",

                // Audio STT
                ["TitleAudio"] = "Транскрипция аудио",
                ["LblSttMode"] = "Режим STT:",
                ["SttDeepgram"] = "Deepgram (Потоковый + Спикеры)",
                ["SttGroq"] = "Groq Whisper (Пакетный + Спикеры)",
                ["LblFontSize"] = "Размер шрифта субтитров:",
                ["FontSmall"] = "Маленький (14px)",
                ["FontMedium"] = "Средний (20px)",
                ["FontLarge"] = "Большой (26px)",
                ["FontExtraLarge"] = "Очень большой (36px)",
                ["LblAudioDevice"] = "Устройство аудиозахвата:",
                ["DefaultAudioDevice"] = "Системный звук (по умолчанию)",
                ["RefreshAudioTooltip"] = "Обновить аудиоустройства",
                ["BtnStartAudio"] = "Начать перевод аудио",
                ["BtnStopAudio"] = "Остановить перевод аудио",
                ["SpeakerDetectionOn"] = "Определение спикеров: ВКЛ",
                ["WaitingForAudio"] = "[Ожидание аудио...]",
                ["ConnectingDeepgram"] = "Подключение к Deepgram...",
                ["FailedDeepgram"] = "Ошибка подключения к Deepgram. Проверьте API-ключ.",
                ["FailedWhisper"] = "Не удалось инициализировать Groq Whisper.",
                ["RateLimitCooldown"] = "Лимит Groq исчерпан (пауза {0}с)...",
                ["DeviceOutput"] = "Вывод: {0}",
                ["DeviceMic"] = "Микрофон: {0}",
                ["DeviceApp"] = "Приложение: {0} - {1}",
                ["SpeakerLabel"] = "Спикер {0}",

                // API Quota
                ["TitleQuota"] = "Квота API",
                ["QuotaUnlimited"] = "Безлимитно",
                ["QuotaTracking"] = "Отслеживание...",
                ["QuotaCooldown"] = "Пауза: {0}с",

                // Extras & Preferences
                ["TxtToggleHideTitle"] = "Скрыть все окна",
                ["TxtToggleHideDesc"] = "Скрыть оверлей и все окна приложения",
                ["TxtTitlePreferences"] = "Настройки приложения",
                ["UiLangEn"] = "English (Английский)",
                ["UiLangId"] = "Bahasa Indonesia (Индонезийский)",
                ["UiLangRu"] = "Русский (Russian)",
                ["UiLangAuto"] = "Авто (Язык системы)",
                ["BtnOpenHistory"] = "Открыть историю переводов",
                ["BtnFeedback"] = "Отчет об ошибке / Отзыв",
                ["BtnCloseApp"] = "Закрыть приложение",

                // Japanese & Multi-Script Style
                ["LblJapaneseStyle"] = "Стиль японского текста:",
                ["LblKoreanStyle"] = "Стиль корейского текста:",
                ["LblChineseStyle"] = "Стиль китайского текста:",
                ["LblDualScriptStyle"] = "Стиль письменности:",
                ["ItemJpStandard"] = "Стандартный (Кандзи + Кана)",
                ["ItemJpHiragana"] = "Хирагана / Смесь с Каной (ひらがな)",
                ["ItemJpFurigana"] = "Фуригана: 漢字(かんじ)",
                ["ItemJpRomaji"] = "Ромадзи (Латинский алфавит ABC)",
                ["ItemJpKatakana"] = "Катакана (カタカナ)",
                ["ItemKoStandard"] = "Хангыль стандартный (한글)",
                ["ItemKoFurigana"] = "Хангыль + Ромадзя: 한글(hangeul)",
                ["ItemKoRomaji"] = "Ромадзя (Латиница)",
                ["ItemKoHanja"] = "Хангыль + Ханча: 한글(漢字)",
                ["ItemZhStandard"] = "Упрощённые иероглифы (简体中文)",
                ["ItemZhTrad"] = "Традиционные иероглифы (繁體中文)",
                ["ItemZhFurigana"] = "Иероглифы + Пиньинь: 汉字(hànzì)",
                ["ItemZhRomaji"] = "Только Пиньинь (Hànyǔ Pīnyīn)",
                ["ItemDualStandard"] = "Стандартный (По умолчанию)",
                ["ItemDualLatin"] = "Латиница (Latinica / Lotin)",
                ["ItemDualCyrillic"] = "Кириллица (Ћирилица / Кирилл)",

                // API Key & Auto Update
                ["BtnVerifyKey"] = "Проверить",
                ["ApiKeyEmpty"] = "Используется бесплатный Google Translate",
                ["ApiKeyChecking"] = "Проверка ключа в сети Groq...",
                ["ApiKeyValid"] = "Groq AI подключен и активен",
                ["ApiKeyInvalid"] = "Недействительный API-ключ Groq",
                ["LblUpdates"] = "Обновления:",
                ["BtnCheckUpdate"] = "Проверить",
                ["UpdateChecking"] = "Проверка обновлений...",
                ["UpdateUpToDate"] = "Установлена последняя версия ({0})",
                ["UpdateAvailable"] = "Обновление {0} готово",
                ["BtnRestartUpdate"] = "Перезапустить",

                // Feedback & Bug Report
                ["FeedbackTitle"] = "Отчет об ошибке и отзывы",
                ["FeedbackSubtitle"] = "Отправьте отчет об ошибке или предложение разработчику",
                ["FeedbackType"] = "Тип обращения:",
                ["FeedbackTypeBug"] = "Ошибка в приложении",
                ["FeedbackTypeFeature"] = "Предложение функции",
                ["FeedbackTypeGeneral"] = "Общий отзыв",
                ["FeedbackContact"] = "Ваш Email (необязательно):",
                ["FeedbackSubject"] = "Тема:",
                ["FeedbackMessage"] = "Описание / Подробности:",
                ["FeedbackSend"] = "Отправить",
                ["FeedbackMailto"] = "Открыть почтовую программу",
                ["FeedbackClose"] = "Закрыть",
                ["FeedbackSending"] = "Отправка сообщения, пожалуйста, подождите...",
                ["FeedbackSuccess"] = "Сообщение успешно отправлено.",
                ["FeedbackError"] = "Не удалось отправить. Проверьте интернет или откройте почтовую программу.",
                ["FeedbackEmpty"] = "Пожалуйста, заполните Тему и Описание.",
                ["FeedbackMailtoError"] = "Не удалось открыть почтовую программу: {0}",
                ["FeedbackTabSubmit"] = "Новый отчет",
                ["FeedbackTabHistory"] = "Мои обращения ({0})",
                ["FeedbackHistoryEmpty"] = "С этого устройства пока не отправлено отчетов.",
                ["FeedbackStatusPending"] = "В ожидании",
                ["FeedbackStatusInProgress"] = "В обработке",
                ["FeedbackStatusFixed"] = "Решено",
                ["FeedbackRefreshStatus"] = "Обновить статус",
                ["FeedbackRefreshing"] = "Проверка статуса...",
                ["FeedbackRefreshed"] = "Статус актуален.",
                ["FeedbackReportId"] = "ID: {0}",
                ["FeedbackReportDate"] = "Отправлено: {0}",

                // History Window
                ["HistoryTitle"] = "Живой транскрипт и перевод",
                ["BtnSummarize"] = "AI-саммари",
                ["BtnClear"] = "Очистить",
                ["BtnSave"] = "Сохранить в файл",
                ["MsgEmptyHistory"] = "История диалога пуста.",
                ["TitleEmpty"] = "Пусто",
                ["MsgMissingApiKey"] = "API-ключ Groq не указан в главном окне.",
                ["TitleError"] = "Ошибка",
                ["WindowSummarizingTitle"] = "AI формирует саммари...",
                ["TxtSummarizing"] = "Создание краткого содержания транскрипта...\nПожалуйста, подождите.",
                ["WindowSummaryTitle"] = "Краткое содержание (AI Summary)",
                ["MsgSavedSuccess"] = "Сохранено в:\n{0}",
                ["TitleSuccess"] = "Успешно",
                ["MsgSaveFailed"] = "Ошибка сохранения: {0}",
                ["AiSummaryPrompt"] = "Вы профессиональный AI-ассистент. Ниже приведён транскрипт живого разговора. Ваша задача: 1. Составить краткое и ёмкое содержание (Executive Summary). 2. Выделить ключевые тезисы (Key Highlights). 3. Указать дальнейшие действия (Action Items), если они есть. Оформите аккуратно и на понятном русском языке."
            }
        };
    }
}
