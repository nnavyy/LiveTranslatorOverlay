using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiveTranslatorOverlay.Core.Japanese
{
    public enum JapaneseScriptStyle
    {
        Standard = 0,   // Kanji + Kana (Default, e.g. わかりました。少々お待ちください。)
        Hiragana = 1,   // All Hiragana (e.g. わかりました。しょうしょう おまちください。)
        Romaji = 2,     // Latin alphabet (e.g. Wakarimashita. Shoushou omachi kudasai.)
        Katakana = 3    // All Katakana (e.g. ワカリマシタ。ショウショウ オマチクダサイ。)
    }

    public static class JapaneseScriptConverter
    {
        private static readonly ConcurrentDictionary<string, string> _hiraganaCache = new();
        private static readonly ConcurrentDictionary<string, string> _romajiCache = new();
        private static readonly ConcurrentDictionary<string, string> _katakanaCache = new();

        private static readonly Dictionary<string, string> TwoCharRomaji = new()
        {
            {"きゃ", "kya"}, {"きゅ", "kyu"}, {"きょ", "kyo"},
            {"しゃ", "sha"}, {"しゅ", "shu"}, {"しょ", "sho"},
            {"ちゃ", "cha"}, {"ちゅ", "chu"}, {"ちょ", "cho"},
            {"にゃ", "nya"}, {"にゅ", "nyu"}, {"にょ", "nyo"},
            {"ひゃ", "hya"}, {"ひゅ", "hyu"}, {"ひょ", "hyo"},
            {"みゃ", "mya"}, {"みゅ", "myu"}, {"みょ", "myo"},
            {"りゃ", "rya"}, {"りゅ", "ryu"}, {"りょ", "ryo"},
            {"ぎゃ", "gya"}, {"ぎゅ", "gyu"}, {"ぎょ", "gyo"},
            {"じゃ", "ja"},  {"じゅ", "ju"},  {"じょ", "jo"},
            {"びゃ", "bya"}, {"びゅ", "byu"}, {"びょ", "byo"},
            {"ぴゃ", "pya"}, {"ぴゅ", "pyu"}, {"ぴょ", "pyo"},
            {"ふぁ", "fa"},  {"ふぃ", "fi"},  {"ふぇ", "fe"},  {"ふぉ", "fo"},
            {"てぃ", "ti"},  {"でぃ", "di"}
        };

        private static readonly Dictionary<char, string> SingleCharRomaji = new()
        {
            {'あ', "a"}, {'い', "i"}, {'う', "u"}, {'え', "e"}, {'お', "o"},
            {'か', "ka"}, {'き', "ki"}, {'く', "ku"}, {'け', "ke"}, {'こ', "ko"},
            {'さ', "sa"}, {'し', "shi"}, {'す', "su"}, {'せ', "se"}, {'そ', "so"},
            {'た', "ta"}, {'ち', "chi"}, {'つ', "tsu"}, {'て', "te"}, {'と', "to"},
            {'な', "na"}, {'に', "ni"}, {'ぬ', "nu"}, {'ね', "ne"}, {'の', "no"},
            {'は', "ha"}, {'ひ', "hi"}, {'ふ', "fu"}, {'へ', "he"}, {'ほ', "ho"},
            {'ま', "ma"}, {'み', "mi"}, {'む', "mu"}, {'め', "me"}, {'も', "mo"},
            {'や', "ya"},               {'ゆ', "yu"},               {'よ', "yo"},
            {'ら', "ra"}, {'り', "ri"}, {'る', "ru"}, {'れ', "re"}, {'ろ', "ro"},
            {'わ', "wa"}, {'ゐ', "wi"},               {'ゑ', "we"}, {'を', "wo"},
            {'ん', "n"},
            {'が', "ga"}, {'ぎ', "gi"}, {'ぐ', "gu"}, {'げ', "ge"}, {'ご', "go"},
            {'ざ', "za"}, {'じ', "ji"}, {'ず', "zu"}, {'ぜ', "ze"}, {'ぞ', "zo"},
            {'だ', "da"}, {'ぢ', "ji"}, {'づ', "zu"}, {'で', "de"}, {'ど', "do"},
            {'ば', "ba"}, {'び', "bi"}, {'ぶ', "bu"}, {'べ', "be"}, {'ぼ', "bo"},
            {'ぱ', "pa"}, {'ぴ', "pi"}, {'ぷ', "pu"}, {'ぺ', "pe"}, {'ぽ', "po"},
            {'ぁ', "a"}, {'ぃ', "i"}, {'ぅ', "u"}, {'ぇ', "e"}, {'ぉ', "o"},
            {'、', ", "}, {'。', ". "}, {'！', "! "}, {'？', "? "}, {'〜', "~"}, {'ー', "-"}
        };

        /// <summary>
        /// Converts Japanese text into the chosen script style.
        /// </summary>
        public static async Task<string> ConvertAsync(string text, JapaneseScriptStyle style, string? apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(text) || style == JapaneseScriptStyle.Standard)
                return text;

            switch (style)
            {
                case JapaneseScriptStyle.Hiragana:
                    return await ToHiraganaAsync(text, apiKey);
                case JapaneseScriptStyle.Katakana:
                    return await ToKatakanaAsync(text, apiKey);
                case JapaneseScriptStyle.Romaji:
                    return await ToRomajiAsync(text, apiKey);
                default:
                    return text;
            }
        }

        public static async Task<string> ToHiraganaAsync(string text, string? apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (_hiraganaCache.TryGetValue(text, out var cached)) return cached;

            string result = TryWindowsImeToHiragana(text);
            if (string.IsNullOrWhiteSpace(result) || result == text)
            {
                // Fallback via AI if API key is provided and Windows IME did not convert Kanji
                if (!string.IsNullOrWhiteSpace(apiKey) && HasKanji(text))
                {
                    result = await QueryAiConversionAsync(text, "Hiragana", apiKey);
                }
            }

            if (string.IsNullOrWhiteSpace(result)) result = text;
            _hiraganaCache[text] = result;
            return result;
        }

        public static async Task<string> ToKatakanaAsync(string text, string? apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (_katakanaCache.TryGetValue(text, out var cached)) return cached;

            string hiragana = await ToHiraganaAsync(text, apiKey);
            string result = HiraganaToKatakana(hiragana);

            _katakanaCache[text] = result;
            return result;
        }

        public static async Task<string> ToRomajiAsync(string text, string? apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (_romajiCache.TryGetValue(text, out var cached)) return cached;

            string hiragana = await ToHiraganaAsync(text, apiKey);
            string result = HiraganaToRomaji(hiragana);

            // Clean spacing & capitalization
            result = FormatRomaji(result);

            _romajiCache[text] = result;
            return result;
        }

        public static bool HasKanji(string text)
        {
            foreach (char c in text)
            {
                // CJK Unified Ideographs block
                if (c >= 0x4E00 && c <= 0x9FAF) return true;
                if (c >= 0x3400 && c <= 0x4DBF) return true;
            }
            return false;
        }

        private static string TryWindowsImeToHiragana(string text)
        {
            try
            {
                var words = Windows.Globalization.JapanesePhoneticAnalyzer.GetWords(text);
                if (words == null || words.Count == 0) return text;

                var sb = new StringBuilder();
                foreach (var word in words)
                {
                    string yomi = word.YomiText;
                    if (!string.IsNullOrWhiteSpace(yomi))
                    {
                        // Windows JapanesePhoneticAnalyzer returns YomiText in Katakana
                        sb.Append(KatakanaToHiragana(yomi));
                    }
                    else
                    {
                        sb.Append(word.DisplayText);
                    }
                }
                return sb.ToString();
            }
            catch
            {
                return text;
            }
        }

        public static string KatakanaToHiragana(string katakana)
        {
            var sb = new StringBuilder(katakana.Length);
            foreach (char c in katakana)
            {
                if (c >= 0x30A1 && c <= 0x30F6)
                {
                    sb.Append((char)(c - 0x60));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static string HiraganaToKatakana(string hiragana)
        {
            var sb = new StringBuilder(hiragana.Length);
            foreach (char c in hiragana)
            {
                if (c >= 0x3041 && c <= 0x3096)
                {
                    sb.Append((char)(c + 0x60));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static string HiraganaToRomaji(string hiragana)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < hiragana.Length)
            {
                // Check double consonant (っ / ッ)
                if (hiragana[i] == 'っ' || hiragana[i] == 'ッ')
                {
                    if (i + 1 < hiragana.Length)
                    {
                        string nextRomaji = GetSingleOrDoubleRomaji(hiragana, i + 1, out int consumed);
                        if (!string.IsNullOrEmpty(nextRomaji))
                        {
                            char firstConsonant = nextRomaji[0];
                            sb.Append(firstConsonant);
                            sb.Append(nextRomaji);
                            i += 1 + consumed;
                            continue;
                        }
                    }
                    sb.Append("t");
                    i++;
                    continue;
                }

                // Check 2-character combo (e.g. しゃ, きょ)
                if (i + 1 < hiragana.Length)
                {
                    string two = hiragana.Substring(i, 2);
                    if (TwoCharRomaji.TryGetValue(two, out var romajiTwo))
                    {
                        sb.Append(romajiTwo);
                        i += 2;
                        continue;
                    }
                }

                // Check 1-character
                char c = hiragana[i];
                if (SingleCharRomaji.TryGetValue(c, out var romajiSingle))
                {
                    sb.Append(romajiSingle);
                }
                else
                {
                    // Fallback to Katakana check or keep original
                    char h = (c >= 0x30A1 && c <= 0x30F6) ? (char)(c - 0x60) : c;
                    if (SingleCharRomaji.TryGetValue(h, out var romajiKana))
                    {
                        sb.Append(romajiKana);
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                i++;
            }

            return sb.ToString();
        }

        private static string GetSingleOrDoubleRomaji(string text, int index, out int consumed)
        {
            consumed = 1;
            if (index + 1 < text.Length)
            {
                string two = text.Substring(index, 2);
                if (TwoCharRomaji.TryGetValue(two, out var romajiTwo))
                {
                    consumed = 2;
                    return romajiTwo;
                }
            }

            char c = text[index];
            char h = (c >= 0x30A1 && c <= 0x30F6) ? (char)(c - 0x60) : c;
            if (SingleCharRomaji.TryGetValue(h, out var romajiOne))
            {
                return romajiOne;
            }

            return "";
        }

        private static string FormatRomaji(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            text = text.Trim();
            if (text.Length == 0) return text;

            // Capitalize first letter
            return char.ToUpper(text[0]) + text.Substring(1);
        }

        private static async Task<string> QueryAiConversionAsync(string text, string targetStyle, string apiKey)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                string prompt = targetStyle switch
                {
                    "Romaji" => "Convert the following Japanese text into standard Hepburn Romaji. Output ONLY the Romaji text, nothing else.",
                    "Hiragana" => "Convert the following Japanese text into pure Hiragana reading (no Kanji). Output ONLY the Hiragana text, nothing else.",
                    "Katakana" => "Convert the following Japanese text into pure Katakana reading. Output ONLY the Katakana text, nothing else.",
                    _ => "Convert the text to " + targetStyle
                };

                var payload = new
                {
                    model = "openai/gpt-oss-20b",
                    messages = new[]
                    {
                        new { role = "system", content = prompt },
                        new { role = "user", content = text }
                    },
                    temperature = 0.0,
                    max_tokens = 256
                };

                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode) return text;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var msg = choices[0].GetProperty("message").GetProperty("content").GetString();
                    return msg?.Trim() ?? text;
                }
            }
            catch { }

            return text;
        }
    }
}
