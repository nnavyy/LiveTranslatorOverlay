using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LiveTranslatorOverlay.Core.Japanese;

namespace LiveTranslatorOverlay.Core.Translation
{
    public class GoogleFreeTranslateProvider : ITranslationProvider
    {
        private readonly HttpClient _httpClient;

        public JapaneseScriptStyle ScriptStyle { get; set; } = JapaneseScriptStyle.Standard;

        // Simple cache
        private string _cachedInput = "";
        private string _cachedTarget = "";
        private string _cachedSource = "";
        private JapaneseScriptStyle _cachedStyle = JapaneseScriptStyle.Standard;
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

            if (text == _cachedInput && targetLanguage == _cachedTarget && sourceLanguage == _cachedSource && ScriptStyle == _cachedStyle && !string.IsNullOrEmpty(_cachedResult))
                return _cachedResult;

            string effectiveTarget = ResolveGoogleLanguageCode(targetLanguage, isTarget: true);
            string effectiveSource = ResolveGoogleLanguageCode(sourceLanguage, isTarget: false);

            // Try Google first, then MyMemory fallback
            string result = await TryGoogleTranslateAsync(text, effectiveTarget, effectiveSource, targetLanguage);

            if (!string.IsNullOrWhiteSpace(result))
            {
                _cachedInput = text;
                _cachedTarget = targetLanguage;
                _cachedSource = sourceLanguage;
                _cachedStyle = ScriptStyle;
                _cachedResult = result;
                return result;
            }

            // Fallback to MyMemory
            result = await TryMyMemoryAsync(text, effectiveTarget, effectiveSource);
            if (!string.IsNullOrWhiteSpace(result))
            {
                _cachedInput = text;
                _cachedTarget = targetLanguage;
                _cachedSource = sourceLanguage;
                _cachedStyle = ScriptStyle;
                _cachedResult = result;
                return result;
            }

            return text;
        }

        public async Task TranslateStreamAsync(string text, string targetLanguage, string sourceLanguage, Action<string> onTokenReceived)
        {
            string result = await TranslateAsync(text, targetLanguage, sourceLanguage);
            onTokenReceived(result);
        }

        private string ResolveGoogleLanguageCode(string code, bool isTarget)
        {
            if (string.IsNullOrWhiteSpace(code)) return isTarget ? "en" : "auto";
            if (code.Equals("zh", StringComparison.OrdinalIgnoreCase))
            {
                if (isTarget && ScriptStyle == JapaneseScriptStyle.Hiragana)
                    return "zh-TW";
                return "zh-CN";
            }
            if (code.Equals("zh-TW", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-TW";
            }
            return code;
        }

        private async Task<string> TryGoogleTranslateAsync(string text, string targetLang, string sourceLang, string rawTargetLang)
        {
            try
            {
                string encoded = Uri.EscapeDataString(text);
                // dt=t returns translation, dt=rm returns romanization/transliteration (Pinyin, Romaji, Romaja, etc.)
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceLang}&tl={targetLang}&hl=en&dt=t&dt=rm&dj=1&q={encoded}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return string.Empty;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("sentences", out var sentences))
                {
                    string translated = "";
                    string translit = "";
                    foreach (var s in sentences.EnumerateArray())
                    {
                        if (s.TryGetProperty("trans", out var trans))
                            translated += trans.GetString();
                        if (s.TryGetProperty("translit", out var tr))
                            translit += tr.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(translated))
                    {
                        string cleanTrans = System.Net.WebUtility.HtmlDecode(translated).Trim();
                        string cleanTranslit = System.Net.WebUtility.HtmlDecode(translit).Trim();

                        // Apply free built-in romanization/pinyin for Chinese when requested
                        if ((rawTargetLang == "zh" || rawTargetLang == "zh-TW") && !string.IsNullOrWhiteSpace(cleanTranslit))
                        {
                            if (ScriptStyle == JapaneseScriptStyle.Romaji)
                                return cleanTranslit;
                            if (ScriptStyle == JapaneseScriptStyle.Furigana)
                                return $"{cleanTrans} ({cleanTranslit})";
                        }

                        return cleanTrans;
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
                string src = sourceLang == "auto" ? "autodetect" : sourceLang;
                string langPair = $"{src}|{targetLang}";
                string encoded = Uri.EscapeDataString(text);
                string url = $"https://api.mymemory.translated.net/get?q={encoded}&langpair={langPair}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return string.Empty;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("responseStatus", out var status) || !status.TryGetInt32(out int code) || code != 200)
                    return string.Empty;

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
