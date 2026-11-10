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
            _strings["en"]["ReleaseNotes200"] = "Live Translator Overlay v2.0.0\n\nNEW\nLocal English / Russian speech recognition for prepared NVIDIA GPU systems. Select English or Russian as source and choose the local speech engine. Local translation is available without API keys; choose explicit source and target languages.\n\nSETUP\nRun enable-local-speech.ps1 once from your prepared workspace. Models stay on your computer. Stop audio before changing its source language.\n\nUSAGE\nPurchasing is not configured. You can continue using the app. Existing API keys and settings are preserved.";
            _strings["en"]["WhatsNew"] = "What’s new";
            _strings["es"]["ReleaseNotes200"] = "Live Translator Overlay v2.0.0\n\nReconocimiento local de inglés y ruso para equipos NVIDIA preparados. Selecciona el idioma de origen y el motor local. La traducción usa el proveedor seleccionado. Ejecuta enable-local-speech.ps1 una vez. Detén el audio antes de cambiar el idioma. Las compras aún no están configuradas; puedes seguir usando la aplicación.";
            _strings["id"]["ReleaseNotes200"] = "Live Translator Overlay v2.0.0\n\nPengenalan suara Inggris dan Rusia lokal untuk komputer NVIDIA yang sudah disiapkan. Pilih bahasa sumber dan mesin lokal. Terjemahan memakai penyedia pilihan Anda. Jalankan enable-local-speech.ps1 sekali. Hentikan audio sebelum mengganti bahasa. Pembelian belum dikonfigurasi; aplikasi tetap dapat digunakan.";
            _strings["ru"]["ReleaseNotes200"] = "Live Translator Overlay v2.0.0\n\nЛокальное распознавание английской и русской речи для подготовленных систем NVIDIA. Выберите язык и локальный движок. Перевод использует выбранного провайдера. Один раз запустите enable-local-speech.ps1. Остановите аудио перед сменой языка. Покупки пока не настроены; приложением можно пользоваться дальше.";
            _strings["en"]["StopBeforeUpdate"] = "Stop audio translation and live OCR before installing the update.";
            _strings["en"]["RestartLanguage"] = "Source language changed. Stop and restart audio to apply it.";
            _strings["en"]["ReleaseNotes151"] = "Live Translator Overlay v1.5.1\n\nIMPROVED\n• The What's new popup scrolls in smaller, bounded steps in both directions. Fast wheel input no longer jumps through the notes.\n• Groq voice estimates use neutral Speaker 1 / Speaker 2 labels instead of guessing gender. Small pitch changes are less likely to create another label; harmonic pitch errors are reduced.\n• Unvoiced, ambiguous, and mixed-pitch chunks are left unassigned rather than borrowing a known speaker's label. Estimates remain session-local and are not biometric identification.\n• For actual word-level speaker separation, use Deepgram streaming with its API key. Whisper labels alone cannot reliably separate people with similar voices or overlapping speech.\n\nFIXED\n• Deepgram results without a speaker ID are no longer presented as Speaker 1.\n• Interim text no longer overwrites the speaker/original while another finalized turn is being translated. Final speaker, original, and translation stay paired.\n• Callbacks from an old connection are ignored after a new audio session starts. Connection cleanup can safely run twice and has a close deadline.\n\nCHECKS\n• Synthetic voice fixtures, real WPF wheel-event checks, and mocked Deepgram messages cover the fixes. Real conversation accuracy still needs representative recordings; no percentage improvement is claimed.";
            _strings["id"]["WhatsNew"] = "Yang baru";
            _strings["id"]["StopBeforeUpdate"] = "Hentikan terjemahan audio dan OCR sebelum memasang pembaruan.";
            _strings["id"]["RestartLanguage"] = "Bahasa sumber berubah. Hentikan dan mulai ulang audio untuk menerapkannya.";
            _strings["id"]["ReleaseNotes151"] = "Live Translator Overlay v1.5.1\n\nDITINGKATKAN\n• Pop-up Yang baru bergulir lebih pelan dan dibatasi per gerakan roda, baik ke atas maupun ke bawah.\n• Perkiraan suara Groq memakai label Pembicara 1 / 2 tanpa menebak gender. Perubahan nada kecil tidak mudah membuat label baru; kesalahan nada harmonik dikurangi.\n• Audio tanpa suara, ambigu, atau bercampur tidak diberi label pembicara lama. Label tetap berupa perkiraan selama sesi, bukan identifikasi biometrik.\n• Untuk pemisahan pembicara per kata, gunakan Deepgram streaming dengan kunci API-nya. Whisper saja tidak andal membedakan suara serupa atau bertumpuk.\n\nDIPERBAIKI\n• Hasil Deepgram tanpa ID pembicara tidak lagi dianggap sebagai Pembicara 1.\n• Teks sementara tidak menimpa pasangan pembicara/teks saat giliran final diterjemahkan.\n• Koneksi sesi lama tidak lagi mengubah sesi baru. Penutupan koneksi dibatasi waktunya dan aman diulang.\n\nPENGUJIAN\n• Diuji dengan suara sintetis, roda gulir WPF, dan respons Deepgram tiruan. Akurasi percakapan nyata masih perlu diuji; tidak ada klaim peningkatan persentase.";
            _strings["ru"]["WhatsNew"] = "Что нового";
            _strings["ru"]["StopBeforeUpdate"] = "Остановите перевод аудио и OCR перед установкой обновления.";
            _strings["ru"]["RestartLanguage"] = "Язык изменён. Остановите и снова запустите аудио.";
            _strings["ru"]["ReleaseNotes151"] = "Live Translator Overlay v1.5.1\n\nУЛУЧШЕНО\n• Окно «Что нового» прокручивается меньшими ограниченными шагами в обе стороны.\n• Groq использует нейтральные метки Говорящий 1 / 2 без определения пола. Небольшие изменения высоты голоса реже создают новую метку; уменьшены гармонические ошибки.\n• Неозвученные, неоднозначные и смешанные фрагменты не получают чужую метку. Это оценка внутри сеанса, а не биометрическая идентификация.\n• Для разделения речи по словам используйте Deepgram с его API-ключом. Whisper не разделяет похожие и перекрывающиеся голоса надёжно.\n\nИСПРАВЛЕНО\n• Результаты Deepgram без ID больше не помечаются как Говорящий 1.\n• Промежуточный текст не заменяет говорящего и оригинал во время перевода завершённой реплики.\n• Старая связь не меняет новый сеанс. Закрытие ограничено по времени и безопасно повторяется.\n\nПРОВЕРКИ\n• Синтетический звук, события колеса WPF и имитированные ответы Deepgram. Реальная точность требует проверки на записях; процент улучшения не заявляется.";
            _strings["es"]["WhatsNew"] = "Novedades";
            _strings["es"]["StopBeforeUpdate"] = "Detén la traducción de audio y el OCR antes de instalar la actualización.";
            _strings["es"]["RestartLanguage"] = "Idioma de origen cambiado. Detén y reinicia el audio para aplicarlo.";
            _strings["es"]["ReleaseNotes151"] = "Live Translator Overlay v1.5.1\n\nMEJORADO\n• Novedades se desplaza con pasos más pequeños y limitados en ambas direcciones.\n• Groq usa Hablante 1 / 2 sin adivinar el género. Los cambios pequeños de tono crean menos etiquetas nuevas y se reducen los errores armónicos.\n• El audio sin voz, ambiguo o con varios tonos queda sin asignar. Las etiquetas son estimaciones de la sesión, no identificación biométrica.\n• Para separar hablantes por palabra, usa Deepgram con su clave API. Whisper no distingue de forma fiable voces similares o superpuestas.\n\nCORREGIDO\n• Los resultados de Deepgram sin ID ya no se atribuyen a Hablante 1.\n• El texto provisional no sustituye al hablante/original mientras se traduce otro turno finalizado.\n• Las conexiones antiguas no alteran una sesión nueva. El cierre tiene un límite de tiempo y puede repetirse con seguridad.\n\nPRUEBAS\n• Audio sintético, eventos de rueda WPF y respuestas simuladas de Deepgram. La precisión en conversaciones reales requiere grabaciones representativas; no se afirma una mejora porcentual.";
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
            _strings["en"]["SpeakerUnknown"] = "Speaker unclear / mixed";
            _strings["en"]["SpeakerEstimateLabel"] = "Speaker {0} (estimate)";
            _strings["en"]["SpeakerPreview"] = "Speaking…";
            _strings["en"]["WhisperSpeakerHint"] = "Groq Whisper active. Speaker labels are estimates. For word-level speaker separation, select Deepgram streaming and supply its API key.";
            _strings["en"]["SttGroq"] = "Groq Whisper (estimated speakers)";
            _strings["id"]["SpeakerUnknown"] = "Pembicara belum jelas / campuran";
            _strings["id"]["SpeakerEstimateLabel"] = "Pembicara {0} (perkiraan)";
            _strings["id"]["SpeakerPreview"] = "Sedang berbicara…";
            _strings["id"]["WhisperSpeakerHint"] = "Groq Whisper aktif. Label pembicara berupa perkiraan. Untuk pemisahan pembicara per kata, pilih Deepgram streaming dan masukkan kunci API-nya.";
            _strings["id"]["SttGroq"] = "Groq Whisper (perkiraan pembicara)";
            _strings["ru"]["SpeakerUnknown"] = "Говорящий не определён / несколько голосов";
            _strings["ru"]["SpeakerEstimateLabel"] = "Говорящий {0} (оценка)";
            _strings["ru"]["SpeakerPreview"] = "Речь…";
            _strings["ru"]["WhisperSpeakerHint"] = "Groq Whisper активен. Метки говорящих приблизительные. Для разделения речи по словам выберите Deepgram и укажите его API-ключ.";
            _strings["ru"]["SttGroq"] = "Groq Whisper (оценка говорящих)";
            _strings["es"]["SpeakerUnknown"] = "Hablante incierto / varios";
            _strings["es"]["SpeakerEstimateLabel"] = "Hablante {0} (estimado)";
            _strings["es"]["SpeakerPreview"] = "Hablando…";
            _strings["es"]["WhisperSpeakerHint"] = "Groq Whisper activo. Las etiquetas son estimaciones. Para separar hablantes por palabra, selecciona Deepgram e introduce su clave API.";
            _strings["es"]["SttGroq"] = "Groq Whisper (hablantes estimados)";
            _strings["en"]["ReleaseNotes211"] = "Live Translator Overlay v2.1.1\n\nNO KEY OR GPU REQUIRED\nLocal multilingual speech now runs on the CPU. Choose Balanced (148 MB) or Larger model (488 MB). The first start downloads and verifies the model automatically. No Python or developer setup. Auto-detect is supported.\n\nTRANSLATION\nGoogle translation is key-free and requires internet. Speech stays on your computer with the local engine. Cloud providers are optional.\n\nCOMPATIBILITY\nThe Windows x64 download bundles .NET and the native runtime, including an older-CPU fallback. Windows 10/11 is required.\n\nACCURACY\nChoose the spoken language for best results. The larger model can help, but runs more slowly. Accuracy varies by language, recording and hardware; these models do not inherit the previous English/Russian research scores.";
            _strings["id"]["ReleaseNotes211"] = "Live Translator Overlay v2.1.1\n\nTANPA KUNCI ATAU GPU\nPengenalan suara multibahasa lokal berjalan di CPU. Pilih model seimbang (148 MB) atau lebih besar (488 MB). Model diunduh dan diverifikasi otomatis saat pertama dipakai. Tidak perlu Python atau pengaturan pengembang. Deteksi bahasa otomatis tersedia.\n\nTerjemahan Google tanpa kunci memerlukan internet. Suara tetap di komputer dengan mesin lokal. Penyedia cloud opsional. Paket Windows x64 menyertakan .NET dan runtime CPU, termasuk CPU lama. Memerlukan Windows 10/11.\n\nPilih bahasa yang diucapkan untuk hasil terbaik. Model lebih besar lebih lambat. Akurasi berbeda menurut bahasa dan rekaman; skor riset Inggris/Rusia sebelumnya tidak berlaku untuk model ini.";
            _strings["es"]["ReleaseNotes211"] = "Live Translator Overlay v2.1.1\n\nSIN CLAVE NI GPU\nEl reconocimiento local multilingüe funciona en CPU. Elige el modelo equilibrado (148 MB) o el mayor (488 MB). Se descarga y verifica automáticamente al empezar. No requiere Python ni configuración de desarrollo. Incluye detección automática del idioma.\n\nGoogle traduce sin clave y requiere internet. El audio se procesa en tu equipo con el motor local. Los proveedores de nube son opcionales. El paquete Windows x64 incluye .NET y soporte para CPU antiguas. Requiere Windows 10/11.\n\nSelecciona el idioma hablado para mejores resultados. El modelo mayor es más lento. La precisión varía; las cifras anteriores de investigación en inglés/ruso no corresponden a estos modelos.";
            _strings["ru"]["ReleaseNotes211"] = "Live Translator Overlay v2.1.1\n\nБЕЗ КЛЮЧА И ВИДЕОКАРТЫ\nЛокальное многоязычное распознавание работает на CPU. Выберите обычную (148 МБ) или большую (488 МБ) модель. При первом запуске модель загружается и проверяется автоматически. Python и настройка разработчика не нужны. Есть автоопределение языка.\n\nПеревод Google не требует ключа, но нужен интернет. При локальном распознавании звук остаётся на компьютере. Облачные сервисы необязательны. Пакет Windows x64 включает .NET и поддержку старых CPU. Нужна Windows 10/11.\n\nДля лучшего результата выберите язык речи. Большая модель медленнее. Точность зависит от языка и записи; прошлые исследовательские оценки английской/русской речи к этим моделям не относятся.";
            _strings["en"]["ReleaseNotes212"] = "Live Translator Overlay v2.1.2\n\nStop audio to change speech models. The app shows which model is active and lists cloud usage separately for speech and translation. Key controls appear for the selected cloud provider.\n\nThe Windows installer presents the privacy policy and terms on first install, then prepares any missing local speech files. Regular app launches skip setup. The app also filters the reported Whisper subtitle credit.";
            foreach (var language in new[] { "id", "es", "ru" })
                _strings[language]["ReleaseNotes212"] = _strings["en"]["ReleaseNotes212"];
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
