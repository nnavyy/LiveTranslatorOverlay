using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiveTranslatorOverlay.Core.Translation
{
    public class GoogleFreeTranslateProvider : ITranslationProvider
    {
        private readonly HttpClient _httpClient;

        // Simple cache
        private string _cachedInput = "";
        private string _cachedTarget = "";
        private string _cachedSource = "";
        private string _cachedResult = "";

        public GoogleFreeTranslateProvider()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(6);
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        }

        public async Task<string> TranslateAsync(string text, string targetLanguage = "id", string sourceLanguage = "auto")
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            // Return cache if same
            if (text == _cachedInput && targetLanguage == _cachedTarget && sourceLanguage == _cachedSource)
                return _cachedResult;

            // Try Google first, then MyMemory fallback
            string result = await TryGoogleTranslateAsync(text, targetLanguage, sourceLanguage);

            // Validate result: if result is identical to input, it was probably not translated
            // This happens when Google ignores the source lang and returns original
            if (!string.IsNullOrWhiteSpace(result) && result.Trim() != text.Trim())
            {
                _cachedInput = text;
                _cachedTarget = targetLanguage;
                _cachedSource = sourceLanguage;
                _cachedResult = result;
                return result;
            }

            // Fallback to MyMemory
            result = await TryMyMemoryAsync(text, targetLanguage, sourceLanguage);
            if (!string.IsNullOrWhiteSpace(result))
            {
                _cachedInput = text;
                _cachedTarget = targetLanguage;
                _cachedSource = sourceLanguage;
                _cachedResult = result;
                return result;
            }

            return text; // Return original if all fail
        }

        public async Task TranslateStreamAsync(string text, string targetLanguage, string sourceLanguage, System.Action<string> onTokenReceived)
        {
            // Google Free API tidak mendukung streaming, jadi kita panggil method biasa lalu kirim hasilnya sekaligus
            string result = await TranslateAsync(text, targetLanguage, sourceLanguage);
            onTokenReceived(result);
        }

        private async Task<string> TryGoogleTranslateAsync(string text, string targetLang, string sourceLang)
        {
            try
            {
                string encoded = Uri.EscapeDataString(text);
                // dj=1 returns structured JSON, more reliable than nested arrays
                // hl=en forces the interface language to English (prevents locale interference)
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLang}&tl={targetLang}&hl=en&dt=t&dj=1&q={encoded}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return string.Empty;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Parse detected source language
                string? detectedSrc = null;
                if (root.TryGetProperty("src", out var srcEl))
                    detectedSrc = srcEl.GetString();

                // Parse translated sentences
                if (root.TryGetProperty("sentences", out var sentences))
                {
                    string translated = "";
                    foreach (var s in sentences.EnumerateArray())
                    {
                        if (s.TryGetProperty("trans", out var trans))
                            translated += trans.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(translated))
                    {
                        return System.Net.WebUtility.HtmlDecode(translated).Trim();
                    }
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task<string> TryMyMemoryAsync(string text, string targetLang, string sourceLang)
        {
            try
            {
                // MyMemory uses "autodetect" instead of "auto"
                string src = sourceLang == "auto" ? "autodetect" : sourceLang;
                string langPair = $"{src}|{targetLang}";
                string encoded = Uri.EscapeDataString(text);
                string url = $"https://api.mymemory.translated.net/get?q={encoded}&langpair={langPair}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return string.Empty;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("responseData", out var data)
                    && data.TryGetProperty("translatedText", out var trans))
                {
                    string res = trans.GetString() ?? string.Empty;
                    if (res.Contains("PLEASE SELECT TWO DISTINCT LANGUAGES", StringComparison.OrdinalIgnoreCase))
                    {
                        return text;
                    }
                    return res;
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
