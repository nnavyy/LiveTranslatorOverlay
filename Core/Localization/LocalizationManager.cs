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
        private string _activeLang = "en";     // "en" or "id"

        public string ConfiguredLanguage => _configuredLang;
        public string ActiveLanguage => _activeLang;

        public LocalizationManager()
        {
            SetLanguage("en");
        }

        public void SetLanguage(string langCode)
        {
            _configuredLang = string.IsNullOrWhiteSpace(langCode) ? "en" : langCode.ToLowerInvariant();

            if (_configuredLang == "auto")
            {
                string sysTwoLetter = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                _activeLang = sysTwoLetter == "id" ? "id" : "en";
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
                ["LangChinese"] = "Chinese (zh)",
                ["LangChineseSimp"] = "Chinese Simplified (zh)",
                ["LangIndonesian"] = "Indonesian (id)",
                ["LangSpanish"] = "Spanish (es)",
                ["LangFrench"] = "French (fr)",
                ["LangGerman"] = "German (de)",
                ["LangRussian"] = "Russian (ru)",
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
                ["UiLangAuto"] = "Auto (System Default)",
                ["BtnOpenHistory"] = "Open Translation History",
                ["BtnFeedback"] = "Report Bug / Feedback",
                ["BtnCloseApp"] = "Close App",

                // Feedback & Bug Report
                ["FeedbackTitle"] = "Report Bug & Feedback",
                ["FeedbackSubtitle"] = "Send bug reports, suggestions, or feedback to developer",
                ["FeedbackType"] = "Report Type:",
                ["FeedbackTypeBug"] = "Bug Report",
                ["FeedbackTypeFeature"] = "Feature Suggestion",
                ["FeedbackTypeGeneral"] = "General Feedback",
                ["FeedbackContact"] = "Your Email (Optional, for replies):",
                ["FeedbackSubject"] = "Subject:",
                ["FeedbackMessage"] = "Description / Details:",
                ["FeedbackIncludeSystem"] = "Include app & OS version info",
                ["FeedbackSend"] = "Send Report",
                ["FeedbackMailto"] = "Open Email App",
                ["FeedbackSending"] = "Sending report, please wait...",
                ["FeedbackSuccess"] = "Thank you! Your report has been sent successfully to nandazhafran@gmail.com.",
                ["FeedbackError"] = "Failed to send report. Please check your internet connection or use Open Email App.",
                ["FeedbackEmpty"] = "Please fill in the Subject and Description.",

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
                ["LangChinese"] = "Mandarin (zh)",
                ["LangChineseSimp"] = "Mandarin Sederhana (zh)",
                ["LangIndonesian"] = "Indonesia (id)",
                ["LangSpanish"] = "Spanyol (es)",
                ["LangFrench"] = "Prancis (fr)",
                ["LangGerman"] = "Jerman (de)",
                ["LangRussian"] = "Rusia (ru)",
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
                ["UiLangAuto"] = "Otomatis (Sesuai Sistem)",
                ["BtnOpenHistory"] = "Buka Riwayat Terjemahan",
                ["BtnFeedback"] = "Lapor Bug / Saran",
                ["BtnCloseApp"] = "Tutup Aplikasi",

                // Feedback & Bug Report
                ["FeedbackTitle"] = "Lapor Bug & Saran",
                ["FeedbackSubtitle"] = "Kirim laporan bug, saran, atau masukan ke developer",
                ["FeedbackType"] = "Tipe Laporan:",
                ["FeedbackTypeBug"] = "Laporan Bug",
                ["FeedbackTypeFeature"] = "Saran Fitur",
                ["FeedbackTypeGeneral"] = "Kritik & Masukan",
                ["FeedbackContact"] = "Email Anda (Opsional, jika ingin dibalas):",
                ["FeedbackSubject"] = "Judul / Subjek:",
                ["FeedbackMessage"] = "Deskripsi / Detail:",
                ["FeedbackIncludeSystem"] = "Sertakan info versi aplikasi & sistem",
                ["FeedbackSend"] = "Kirim Laporan",
                ["FeedbackMailto"] = "Buka Aplikasi Email",
                ["FeedbackSending"] = "Sedang mengirim laporan, mohon tunggu...",
                ["FeedbackSuccess"] = "Terima kasih! Laporan Anda berhasil dikirim ke nandazhafran@gmail.com.",
                ["FeedbackError"] = "Gagal mengirim laporan. Periksa koneksi internet atau gunakan Buka Aplikasi Email.",
                ["FeedbackEmpty"] = "Harap isi Judul dan Deskripsi terlebih dahulu.",

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
            }
        };
    }
}
