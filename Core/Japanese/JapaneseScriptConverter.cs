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
        Hiragana = 1,   // All Hiragana / Kana Mix (e.g. わかりました。しょうしょうおまちください。)
        Romaji = 2,     // Latin alphabet (e.g. Wakarimashita. Shoushou omachi kudasai.)
        Katakana = 3,   // All Katakana (e.g. ワカリマシタ。ショウショウ オマチクダサイ。)
        Furigana = 4    // Kanji with Furigana reading in brackets (e.g. わかりました。少々(しょうしょう)お待ち(まち)ください。)
    }

    public static class JapaneseScriptConverter
    {
        private static readonly ConcurrentDictionary<string, string> _hiraganaCache = new();
        private static readonly ConcurrentDictionary<string, string> _romajiCache = new();
        private static readonly ConcurrentDictionary<string, string> _katakanaCache = new();
        private static readonly ConcurrentDictionary<string, string> _furiganaCache = new();
        private static readonly ConcurrentDictionary<string, string> _multiScriptCache = new();

        /// <summary>
        /// Returns true if the language has 2+ writing systems, phonetic scripts, or digraphic options
        /// (Japanese, Korean, Chinese, Serbian, Bosnian, Uzbek, Azerbaijani, Mongolian).
        /// </summary>
        public static bool IsMultiScriptLanguage(string? langCode)
        {
            if (string.IsNullOrWhiteSpace(langCode)) return false;
            return langCode.ToLowerInvariant() switch
            {
                "ja" or "ko" or "zh" or "zh-cn" or "zh-tw" or "sr" or "bs" or "uz" or "az" or "mn" => true,
                _ => false
            };
        }

        public static string GetScriptCategory(string? langCode)
        {
            if (string.IsNullOrWhiteSpace(langCode)) return "none";
            return langCode.ToLowerInvariant() switch
            {
                "ja" => "ja",
                "ko" => "ko",
                "zh" or "zh-cn" or "zh-tw" => "zh",
                "sr" or "bs" or "uz" or "az" or "mn" => "dual",
                _ => "none"
            };
        }

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
                case JapaneseScriptStyle.Furigana:
                    return await ToFuriganaAsync(text, apiKey);
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

        public static async Task<string> ToFuriganaAsync(string text, string? apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (!HasKanji(text)) return text;
            if (_furiganaCache.TryGetValue(text, out var cached)) return cached;

            string result = text;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                result = await QueryAiConversionAsync(text, "Furigana", apiKey);
            }

            if (string.IsNullOrWhiteSpace(result)) result = text;
            _furiganaCache[text] = result;
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

        public static bool HasHangul(string text)
        {
            foreach (char c in text)
            {
                if ((c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x3131 && c <= 0x318E))
                    return true;
            }
            return false;
        }

        public static bool HasCyrillic(string text)
        {
            foreach (char c in text)
            {
                if (c >= 0x0400 && c <= 0x04FF)
                    return true;
            }
            return false;
        }

        // Korean Revised Romanization (Official National Institute of Korean Language standard)
        private static readonly string[] KrInitials =
        {
            "g", "kk", "n", "d", "tt", "r", "m", "b", "pp", "s", "ss", "", "j", "jj", "ch", "k", "t", "p", "h"
        };

        private static readonly string[] KrMedials =
        {
            "a", "ae", "ya", "yae", "eo", "e", "yeo", "ye", "o", "wa", "wae", "oe", "yo", "u", "wo", "we", "wi", "yu", "eu", "ui", "i"
        };

        private static readonly string[] KrFinals =
        {
            "", "k", "k", "k", "n", "n", "n", "t", "l", "k", "m", "p", "l", "l", "p", "l", "m", "p", "p", "t", "t", "ng", "t", "t", "k", "t", "p", "t"
        };

        public static string HangulToRomaja(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var sb = new StringBuilder(text.Length * 2);
            foreach (char c in text)
            {
                if (c >= 0xAC00 && c <= 0xD7A3)
                {
                    int code = c - 0xAC00;
                    int initial = code / (21 * 28);
                    int medial = (code % (21 * 28)) / 28;
                    int final = code % 28;
                    sb.Append(KrInitials[initial]).Append(KrMedials[medial]).Append(KrFinals[final]);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return FormatRomaji(sb.ToString());
        }

        public static string HangulWithRomaja(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !HasHangul(text)) return text;
            var sb = new StringBuilder();
            var hangulWord = new StringBuilder();

            foreach (char c in text)
            {
                if (c >= 0xAC00 && c <= 0xD7A3)
                {
                    hangulWord.Append(c);
                }
                else
                {
                    if (hangulWord.Length > 0)
                    {
                        string w = hangulWord.ToString();
                        sb.Append(w).Append('(').Append(HangulToRomaja(w).ToLowerInvariant()).Append(')');
                        hangulWord.Clear();
                    }
                    sb.Append(c);
                }
            }
            if (hangulWord.Length > 0)
            {
                string w = hangulWord.ToString();
                sb.Append(w).Append('(').Append(HangulToRomaja(w).ToLowerInvariant()).Append(')');
            }
            return sb.ToString();
        }

        private static readonly Dictionary<char, string> CyrillicToLatinMap = new()
        {
            // Serbian / Bosnian / Macedonian / General Cyrillic
            {'А', "A"}, {'а', "a"}, {'Б', "B"}, {'б', "b"}, {'В', "V"}, {'в', "v"},
            {'Г', "G"}, {'г', "g"}, {'Д', "D"}, {'д', "d"}, {'Ђ', "Đ"}, {'ђ', "đ"},
            {'Ѓ', "Gj"}, {'ѓ', "gj"}, {'Е', "E"}, {'е', "e"}, {'Ё', "Yo"}, {'ё', "yo"},
            {'Ж', "Ž"}, {'ж', "ž"}, {'З', "Z"}, {'з', "z"}, {'Ѕ', "Dz"}, {'ѕ', "dz"},
            {'И', "I"}, {'и', "i"}, {'Ј', "J"}, {'ј', "j"}, {'Й', "Y"}, {'й', "y"},
            {'К', "K"}, {'к', "k"}, {'Л', "L"}, {'л', "l"}, {'Љ', "Lj"}, {'љ', "lj"},
            {'М', "M"}, {'м', "m"}, {'Н', "N"}, {'н', "n"}, {'Њ', "Nj"}, {'њ', "nj"},
            {'О', "O"}, {'о', "o"}, {'П', "P"}, {'п', "p"}, {'Р', "R"}, {'р', "r"},
            {'С', "S"}, {'с', "s"}, {'Т', "T"}, {'т', "t"}, {'Ћ', "Ć"}, {'ћ', "ć"},
            {'Ќ', "Kj"}, {'ќ', "kj"}, {'У', "U"}, {'у', "u"}, {'Ф', "F"}, {'ф', "f"},
            {'Х', "H"}, {'х', "h"}, {'Ц', "C"}, {'ц', "c"}, {'Ч', "Č"}, {'ч', "č"},
            {'Џ', "Dž"}, {'џ', "dž"}, {'Ш', "Š"}, {'ш', "š"}, {'Щ', "Shch"}, {'щ', "shch"},
            {'Ъ', ""}, {'ъ', ""}, {'Ы', "Y"}, {'ы', "y"}, {'Ь', ""}, {'ь', ""},
            {'Э', "E"}, {'э', "e"}, {'Ю', "Yu"}, {'ю', "yu"}, {'Я', "Ya"}, {'я', "ya"},
            // Uzbek / Azerbaijani / Mongolian extended Cyrillic
            {'Ў', "Oʻ"}, {'ў', "oʻ"}, {'Қ', "Q"}, {'қ', "q"}, {'Ғ', "Gʻ"}, {'ғ', "gʻ"},
            {'Ҳ', "H"}, {'ҳ', "h"}, {'Ә', "Ə"}, {'ә', "ə"}, {'Ө', "Ö"}, {'ө', "ö"},
            {'Ү', "Ü"}, {'ү', "ü"}, {'Ҹ', "C"}, {'ҹ', "c"}, {'Һ', "H"}, {'һ', "h"}
        };

        private static readonly (string Latin, string Cyrillic)[] SerbianLatinToCyrillicDigraphs =
        {
            ("Dž", "Џ"), ("DŽ", "Џ"), ("dž", "џ"),
            ("Lj", "Љ"), ("LJ", "Љ"), ("lj", "љ"),
            ("Nj", "Њ"), ("NJ", "Њ"), ("nj", "њ")
        };

        private static readonly Dictionary<char, char> SerbianLatinToCyrillicSingle = new()
        {
            {'A', 'А'}, {'a', 'а'}, {'B', 'Б'}, {'b', 'б'}, {'C', 'Ц'}, {'c', 'ц'},
            {'Č', 'Ч'}, {'č', 'ч'}, {'Ć', 'Ћ'}, {'ć', 'ћ'}, {'D', 'Д'}, {'d', 'д'},
            {'Đ', 'Ђ'}, {'đ', 'ђ'}, {'E', 'Е'}, {'e', 'е'}, {'F', 'Ф'}, {'f', 'ф'},
            {'G', 'Г'}, {'g', 'г'}, {'H', 'Х'}, {'h', 'х'}, {'I', 'И'}, {'i', 'и'},
            {'J', 'Ј'}, {'j', 'ј'}, {'K', 'К'}, {'k', 'к'}, {'L', 'Л'}, {'l', 'л'},
            {'M', 'М'}, {'m', 'м'}, {'N', 'Н'}, {'n', 'н'}, {'O', 'О'}, {'o', 'о'},
            {'P', 'П'}, {'p', 'п'}, {'R', 'Р'}, {'r', 'р'}, {'S', 'С'}, {'s', 'с'},
            {'Š', 'Ш'}, {'š', 'ш'}, {'T', 'Т'}, {'t', 'т'}, {'U', 'У'}, {'u', 'у'},
            {'V', 'В'}, {'v', 'в'}, {'Z', 'З'}, {'z', 'з'}, {'Ž', 'Ж'}, {'ž', 'ж'}
        };

        public static string ConvertCyrillicToLatin(string text, string langCode)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            string lang = langCode.ToLowerInvariant();
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (lang == "uz")
                {
                    switch (c)
                    {
                        case 'Ж': sb.Append("J"); continue;
                        case 'ж': sb.Append("j"); continue;
                        case 'Ч': sb.Append("Ch"); continue;
                        case 'ч': sb.Append("ch"); continue;
                        case 'Ш': sb.Append("Sh"); continue;
                        case 'ш': sb.Append("sh"); continue;
                        case 'Ц': sb.Append("Ts"); continue;
                        case 'ц': sb.Append("ts"); continue;
                        case 'Х': sb.Append("X"); continue;
                        case 'х': sb.Append("x"); continue;
                    }
                }
                else if (lang == "az")
                {
                    switch (c)
                    {
                        case 'Ч': sb.Append("Ç"); continue;
                        case 'ч': sb.Append("ç"); continue;
                        case 'Ш': sb.Append("Ş"); continue;
                        case 'ш': sb.Append("ş"); continue;
                        case 'Ж': sb.Append("J"); continue;
                        case 'ж': sb.Append("j"); continue;
                        case 'Х': sb.Append("X"); continue;
                        case 'х': sb.Append("x"); continue;
                        case 'Ғ': sb.Append("Ğ"); continue;
                        case 'ғ': sb.Append("ğ"); continue;
                        case 'Ы': sb.Append("I"); continue;
                        case 'ы': sb.Append("ı"); continue;
                    }
                }

                if (CyrillicToLatinMap.TryGetValue(c, out var mapped))
                    sb.Append(mapped);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        public static string ConvertLatinToCyrillic(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            string working = text;
            foreach (var (lat, cyr) in SerbianLatinToCyrillicDigraphs)
            {
                working = working.Replace(lat, cyr);
            }
            var sb = new StringBuilder(working.Length);
            foreach (char c in working)
            {
                if (SerbianLatinToCyrillicSingle.TryGetValue(c, out char cyr))
                    sb.Append(cyr);
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Converts text for any multi-script language (Japanese, Korean, Chinese, Serbian, Bosnian, Uzbek, Azerbaijani, Mongolian).
        /// </summary>
        public static async Task<string> ConvertForLanguageAsync(string text, string langCode, JapaneseScriptStyle style, string? apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(text) || style == JapaneseScriptStyle.Standard)
                return text;

            string category = GetScriptCategory(langCode);
            if (category == "ja" || (category == "none" && (ContainsKana(text) || HasKanji(text))))
            {
                return await ConvertAsync(text, style, apiKey);
            }

            string cacheKey = $"{langCode}:{style}:{text}";
            if (_multiScriptCache.TryGetValue(cacheKey, out var cached))
                return cached;

            string result = text;
            if (category == "ko")
            {
                result = style switch
                {
                    JapaneseScriptStyle.Furigana => HangulWithRomaja(text),
                    JapaneseScriptStyle.Romaji => HangulToRomaja(text),
                    JapaneseScriptStyle.Katakana => !string.IsNullOrWhiteSpace(apiKey)
                        ? await QueryAiMultiScriptAsync(text, "Korean Hangul with Hanja in parentheses for Sino-Korean words, e.g. 한국(韓國)", apiKey)
                        : text,
                    _ => text
                };
            }
            else if (category == "zh")
            {
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    string instruction = style switch
                    {
                        JapaneseScriptStyle.Hiragana => "Traditional Chinese characters (繁體中文)",
                        JapaneseScriptStyle.Furigana => "Chinese characters with Hanyu Pinyin in parentheses after each word, e.g. 你好(nǐ hǎo)",
                        JapaneseScriptStyle.Romaji => "Hanyu Pinyin with tone marks only (Latin alphabet)",
                        _ => ""
                    };
                    if (!string.IsNullOrEmpty(instruction))
                        result = await QueryAiMultiScriptAsync(text, instruction, apiKey);
                }
            }
            else if (category == "dual")
            {
                if (style == JapaneseScriptStyle.Romaji) // Latin script
                {
                    result = HasCyrillic(text) ? ConvertCyrillicToLatin(text, langCode) : text;
                }
                else if (style == JapaneseScriptStyle.Hiragana) // Cyrillic script
                {
                    if (!HasCyrillic(text))
                    {
                        if (langCode is "sr" or "bs")
                            result = ConvertLatinToCyrillic(text);
                        else if (!string.IsNullOrWhiteSpace(apiKey))
                            result = await QueryAiMultiScriptAsync(text, $"Cyrillic script for {langCode}", apiKey);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(result))
                _multiScriptCache[cacheKey] = result;

            return string.IsNullOrWhiteSpace(result) ? text : result;
        }

        private static bool ContainsKana(string text)
        {
            foreach (char c in text)
            {
                if ((c >= 0x3040 && c <= 0x309F) || (c >= 0x30A0 && c <= 0x30FF))
                    return true;
            }
            return false;
        }

        private static async Task<string> QueryAiMultiScriptAsync(string text, string targetFormatDescription, string apiKey)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string prompt = $"Convert the following text into {targetFormatDescription}. Output ONLY the converted text, nothing else.";
                string[] modelsToTry = { "llama-3.3-70b-versatile", "llama-3.1-8b-instant" };

                foreach (var modelName in modelsToTry)
                {
                    try
                    {
                        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                        var payload = new
                        {
                            model = modelName,
                            messages = new[]
                            {
                                new { role = "system", content = prompt },
                                new { role = "user", content = text }
                            },
                            temperature = 0.0,
                            max_tokens = 256
                        };

                        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                        using var response = await client.SendAsync(req);
                        if (!response.IsSuccessStatusCode) continue;

                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var msg = choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(msg))
                                return msg;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return text;
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
                    "Romaji" => "Convert the following Japanese text into standard Hepburn Romaji Latin alphabet. Output ONLY the Romaji text, nothing else.",
                    "Hiragana" => "Convert the following Japanese text into pure Hiragana reading (replace all Kanji with their correct Hiragana reading, but keep Katakana loanwords as Katakana). Output ONLY the converted text, nothing else.",
                    "Katakana" => "Convert the following Japanese text into pure Katakana reading. Output ONLY the Katakana text, nothing else.",
                    "Furigana" => "Add Hiragana furigana in parentheses immediately after each Kanji word (e.g. 漢字(かんじ)). Keep original Kana and Katakana as is. Output ONLY the converted text, nothing else.",
                    _ => "Convert the text to " + targetStyle
                };

                string[] modelsToTry = { "llama-3.3-70b-versatile", "llama-3.1-8b-instant" };

                foreach (var modelName in modelsToTry)
                {
                    try
                    {
                        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                        var payload = new
                        {
                            model = modelName,
                            messages = new[]
                            {
                                new { role = "system", content = prompt },
                                new { role = "user", content = text }
                            },
                            temperature = 0.0,
                            max_tokens = 256
                        };

                        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                        using var response = await client.SendAsync(req);
                        if (!response.IsSuccessStatusCode) continue;

                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var msg = choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(msg))
                                return msg;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return text;
        }
    }
}
