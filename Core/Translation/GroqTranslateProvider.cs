using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using LiveTranslatorOverlay.Core.Japanese;

namespace LiveTranslatorOverlay.Core.Translation
{
    public class GroqTranslateProvider : ITranslationProvider
    {
        private readonly HttpClient _httpClient;
        private static readonly GoogleFreeTranslateProvider SharedGoogleFallback = new();
        private string _apiKey;
        private string _model = ""; // Will be auto-set after fetching available models

        public JapaneseScriptStyle JapaneseStyle { get; set; } = JapaneseScriptStyle.Standard;

        // Preferred models in order of priority (most accurate multilingual first)
        private static readonly string[] PreferredModels =
        {
            "llama-3.3-70b-versatile",  // Best accuracy, tone nuance & idiom handling for Slavic, Turkic, Caucasian, Balkan & Asian languages
            "meta-llama/llama-4-maverick-17b-128e-instruct",
            "meta-llama/llama-4-scout-17b-16e-instruct",
            "llama-3.1-8b-instant"       // High speed fallback
        };

        private string _cachedInput = "";
        private string _cachedTarget = "";
        private string _cachedSource = "";
        private JapaneseScriptStyle _cachedStyle = JapaneseScriptStyle.Standard;
        private string _cachedResult = "";

        // Keep last 4 dialogue turns for pronoun/topic continuity without polluting short translations
        private readonly Queue<string> _history = new Queue<string>();
        private const int MaxHistory = 4;

        // Deduplication: track the last translated input to avoid redundant API calls
        private string _lastTranslatedInput = "";

        public GroqTranslateProvider(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(20);
        }

        public void UpdateApiKey(string newKey)
        {
            _apiKey = newKey;
            _model = ""; // Reset model so it refetches on next call
        }

        /// <summary>
        /// Validates that a model ID is a legitimate text generation model,
        /// strictly excluding safeguard, moderation, audio, TTS, and whisper models.
        /// </summary>
        public static bool IsValidTextModel(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            string lower = id.ToLowerInvariant();
            if (lower.Contains("guard")) return false;
            if (lower.Contains("whisper")) return false;
            if (lower.Contains("vision")) return false;
            if (lower.Contains("embed")) return false;
            if (lower.Contains("orpheus")) return false;
            if (lower.Contains("audio")) return false;
            if (lower.Contains("moderation")) return false;
            if (lower.Contains("tts")) return false;
            return true;
        }

        /// <summary>
        /// Fetch available model list from Groq API and pick the best one
        /// </summary>
        public async Task<string> GetBestAvailableModelAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/models");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return PreferredModels[0];

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var availableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var validModels = new List<string>();

                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var model in data.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var idEl))
                        {
                            string? id = idEl.GetString();
                            if (IsValidTextModel(id))
                            {
                                availableIds.Add(id!);
                                validModels.Add(id!);
                            }
                        }
                    }
                }

                foreach (var preferred in PreferredModels)
                {
                    if (availableIds.Contains(preferred))
                        return preferred;
                }

                if (validModels.Count > 0)
                {
                    return validModels[0];
                }
            }
            catch { }

            return PreferredModels[0];
        }

        public async Task<List<string>> GetAvailableModelsAsync()
        {
            var models = new List<string>();
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/models");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return models;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var model in data.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var idEl))
                        {
                            string? id = idEl.GetString();
                            if (IsValidTextModel(id))
                                models.Add(id!);
                        }
                    }
                }
            }
            catch { }

            return models;
        }

        public static async Task<(bool IsValid, string Message, List<string> Models)> ValidateKeyAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return (false, "API Key is empty", new List<string>());

            string cleanKey = apiKey.Trim();
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/models");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cleanKey);

                using var response = await client.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return (false, "Invalid API Key (HTTP 401 Unauthorized)", new List<string>());
                }

                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"Groq error: {response.StatusCode}", new List<string>());
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var models = new List<string>();

                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var model in data.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var idEl))
                        {
                            string? id = idEl.GetString();
                            if (IsValidTextModel(id))
                                models.Add(id!);
                        }
                    }
                }

                return (true, "Key is valid & connected to Groq", models);
            }
            catch (Exception ex)
            {
                return (false, $"Connection error: {ex.Message}", new List<string>());
            }
        }

        private async Task<string> GetFallbackModelAsync(string currentModel)
        {
            try
            {
                foreach (var pref in PreferredModels)
                {
                    if (!string.Equals(pref, currentModel, StringComparison.OrdinalIgnoreCase))
                        return pref;
                }

                var models = await GetAvailableModelsAsync();
                foreach (var m in models)
                {
                    if (!string.Equals(m, currentModel, StringComparison.OrdinalIgnoreCase) && IsValidTextModel(m))
                        return m;
                }
            }
            catch { }

            return "llama-3.1-8b-instant";
        }

        public static string ResolveTargetLanguageDescription(string targetLanguage, JapaneseScriptStyle style)
        {
            return targetLanguage switch
            {
                "en" => "English",
                "id" => "Indonesian",
                "ja" => style switch
                {
                    JapaneseScriptStyle.Hiragana => "Japanese (written in pure Hiragana and Katakana without Kanji)",
                    JapaneseScriptStyle.Furigana => "Japanese (with Hiragana furigana in parentheses immediately following each Kanji word, e.g. 漢字(かんじ))",
                    JapaneseScriptStyle.Romaji => "Japanese (written in standard Hepburn Romaji Latin alphabet)",
                    JapaneseScriptStyle.Katakana => "Japanese (written exclusively in Katakana)",
                    _ => "Japanese"
                },
                "ko" => style switch
                {
                    JapaneseScriptStyle.Furigana => "Korean (written in Hangul with Revised Romanization in parentheses after each word, e.g. 한국어(hangugeo))",
                    JapaneseScriptStyle.Romaji => "Korean (written in standard Revised Romanization of Korean Latin alphabet)",
                    JapaneseScriptStyle.Katakana => "Korean (written in Hangul with corresponding Hanja in parentheses for Sino-Korean words, e.g. 대한민국(大韓民國))",
                    _ => "Korean (Hangul)"
                },
                "zh" => style switch
                {
                    JapaneseScriptStyle.Hiragana => "Traditional Chinese (繁體中文)",
                    JapaneseScriptStyle.Furigana => "Simplified Chinese (with Hanyu Pinyin in parentheses after each word, e.g. 你好(nǐ hǎo))",
                    JapaneseScriptStyle.Romaji => "Chinese (written in Hanyu Pinyin with tone marks)",
                    _ => "Simplified Chinese (简体中文)"
                },
                "zh-TW" => style switch
                {
                    JapaneseScriptStyle.Furigana => "Traditional Chinese (with Hanyu Pinyin in parentheses after each word, e.g. 你好(nǐ hǎo))",
                    JapaneseScriptStyle.Romaji => "Chinese (written in Hanyu Pinyin with tone marks)",
                    _ => "Traditional Chinese (繁體中文)"
                },
                "uk" => "Ukrainian",
                "az" => style switch
                {
                    JapaneseScriptStyle.Hiragana => "Azerbaijani (written in Cyrillic script: Азәрбајҹан Кирил)",
                    _ => "Azerbaijani (written in standard Latin script: Azərbaycan dili)"
                },
                "uz" => style switch
                {
                    JapaneseScriptStyle.Hiragana => "Uzbek (written in Cyrillic script: Ўзбек Кирилл)",
                    _ => "Uzbek (written in standard Latin script: Oʻzbek lotin)"
                },
                "mn" => style switch
                {
                    JapaneseScriptStyle.Romaji => "Mongolian (written in Latin transliteration)",
                    _ => "Mongolian (written in standard Khalkha Cyrillic script)"
                },
                "ka" => "Georgian (ქართული)",
                "sr" => style switch
                {
                    JapaneseScriptStyle.Romaji => "Serbian (written in Latinica / Latin script)",
                    JapaneseScriptStyle.Hiragana => "Serbian (written in Ћирилица / Cyrillic script)",
                    _ => "Serbian"
                },
                "hr" => "Croatian",
                "bs" => style switch
                {
                    JapaneseScriptStyle.Hiragana => "Bosnian (written in Cyrillic script)",
                    _ => "Bosnian (written in Latinica / Latin script)"
                },
                "bg" => "Bulgarian",
                "ro" => "Romanian",
                "el" => "Greek",
                "sq" => "Albanian",
                "mk" => "Macedonian",
                "sl" => "Slovenian",
                "es" => "Spanish",
                "fr" => "French",
                "de" => "German",
                "ru" => "Russian",
                "ar" => "Arabic",
                _ => targetLanguage
            };
        }

        public static string ResolveSourceLanguageName(string sourceLanguage)
        {
            return sourceLanguage switch
            {
                "en" => "English",
                "id" => "Indonesian",
                "ja" => "Japanese",
                "ko" => "Korean",
                "zh" => "Chinese (Simplified)",
                "zh-TW" => "Chinese (Traditional)",
                "uk" => "Ukrainian",
                "az" => "Azerbaijani",
                "uz" => "Uzbek",
                "mn" => "Mongolian",
                "ka" => "Georgian",
                "sr" => "Serbian",
                "hr" => "Croatian",
                "bs" => "Bosnian",
                "bg" => "Bulgarian",
                "ro" => "Romanian",
                "el" => "Greek",
                "sq" => "Albanian",
                "mk" => "Macedonian",
                "sl" => "Slovenian",
                "es" => "Spanish",
                "fr" => "French",
                "de" => "German",
                "ru" => "Russian",
                "ar" => "Arabic",
                _ => sourceLanguage
            };
        }

        public async Task<string> TranslateAsync(string text, string targetLanguage = "id", string sourceLanguage = "auto")
        {
            return await TranslateInternalAsync(text, targetLanguage, sourceLanguage, isRetry: false);
        }

        private async Task<string> TranslateInternalAsync(string text, string targetLanguage, string sourceLanguage, bool isRetry)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            if (string.IsNullOrWhiteSpace(_apiKey)) return "[Error: Groq API Key kosong]";

            // Return cache if identical
            if (text == _cachedInput && targetLanguage == _cachedTarget && sourceLanguage == _cachedSource && JapaneseStyle == _cachedStyle && !string.IsNullOrEmpty(_cachedResult))
                return _cachedResult;

            // Deduplication: skip if new text is >88% similar to last translated text in same language pair
            if (!string.IsNullOrEmpty(_lastTranslatedInput) && targetLanguage == _cachedTarget && sourceLanguage == _cachedSource && JapaneseStyle == _cachedStyle && StringSimilarity(text, _lastTranslatedInput) > 0.88)
                return _cachedResult;

            if (string.IsNullOrEmpty(_model) || !IsValidTextModel(_model))
            {
                _model = await GetBestAvailableModelAsync();
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                string targetLangFull = ResolveTargetLanguageDescription(targetLanguage, JapaneseStyle);
                string sourceLangFull = ResolveSourceLanguageName(sourceLanguage);
                string sourceLangHint = sourceLanguage != "auto" ? $" Source language: {sourceLangFull}." : "";
                
                string contextStr = "";
                if (_history.Count > 0 && text.Length > 4)
                {
                    contextStr = "\n\nRecent dialogue context (for pronoun and topic continuity ONLY — DO NOT translate or repeat these earlier lines):\n" + string.Join("\n", _history);
                }

                string systemPrompt = BuildSystemPrompt(targetLangFull, sourceLangHint, contextStr);

                var payload = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = text }
                    },
                    temperature = 0.05,
                    top_p = 0.9,
                    max_tokens = 1024
                };

                request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                QuotaTracker.Instance.UpdateFromHeaders(response.Headers, "Groq AI (Text)");

                if (!response.IsSuccessStatusCode)
                {
                    if (!isRetry && (response.StatusCode == System.Net.HttpStatusCode.NotFound || 
                                     response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
                                     response.StatusCode == (System.Net.HttpStatusCode)429))
                    {
                        _model = await GetFallbackModelAsync(_model);
                        return await TranslateInternalAsync(text, targetLanguage, sourceLanguage, isRetry: true);
                    }
                    
                    return await SharedGoogleFallback.TranslateAsync(text, targetLanguage, sourceLanguage);
                }

                var responseString = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseString);
                var root = document.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                    {
                        string raw = content.GetString()?.Trim() ?? text;
                        string result = HallucinationDetector.CleanTranslationOutput(raw, text);
                        if (string.IsNullOrWhiteSpace(result)) result = text;

                        _cachedInput = text;
                        _cachedTarget = targetLanguage;
                        _cachedSource = sourceLanguage;
                        _cachedStyle = JapaneseStyle;
                        _cachedResult = result;
                        _lastTranslatedInput = text;

                        _history.Enqueue($"- Source: \"{text}\" | Translation: \"{result}\"");
                        if (_history.Count > MaxHistory) _history.Dequeue();

                        return result;
                    }
                }

                return text;
            }
            catch (Exception)
            {
                try
                {
                    return await SharedGoogleFallback.TranslateAsync(text, targetLanguage, sourceLanguage);
                }
                catch
                {
                    return text;
                }
            }
        }

        public async Task TranslateStreamAsync(string text, string targetLanguage, string sourceLanguage, Action<string> onTokenReceived)
        {
            await TranslateStreamInternalAsync(text, targetLanguage, sourceLanguage, onTokenReceived, isRetry: false);
        }

        private async Task TranslateStreamInternalAsync(string text, string targetLanguage, string sourceLanguage, Action<string> onTokenReceived, bool isRetry)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                onTokenReceived("[Error: Groq API Key kosong]");
                return;
            }

            // Return cached result immediately if identical or near-identical
            if (text == _cachedInput && targetLanguage == _cachedTarget && sourceLanguage == _cachedSource && JapaneseStyle == _cachedStyle && !string.IsNullOrEmpty(_cachedResult))
            {
                onTokenReceived(_cachedResult);
                return;
            }
            if (!string.IsNullOrEmpty(_lastTranslatedInput) && targetLanguage == _cachedTarget && sourceLanguage == _cachedSource && JapaneseStyle == _cachedStyle && StringSimilarity(text, _lastTranslatedInput) > 0.88 && !string.IsNullOrEmpty(_cachedResult))
            {
                onTokenReceived(_cachedResult);
                return;
            }

            if (string.IsNullOrEmpty(_model) || !IsValidTextModel(_model))
            {
                _model = await GetBestAvailableModelAsync();
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                string targetLangFull = ResolveTargetLanguageDescription(targetLanguage, JapaneseStyle);
                string sourceLangFull = ResolveSourceLanguageName(sourceLanguage);
                string sourceLangHint = sourceLanguage != "auto" ? $" Source language: {sourceLangFull}." : "";
                
                string contextStr = "";
                if (_history.Count > 0 && text.Length > 4)
                {
                    contextStr = "\n\nRecent dialogue context (for pronoun and topic continuity ONLY — DO NOT translate or repeat these earlier lines):\n" + string.Join("\n", _history);
                }

                string systemPrompt = BuildSystemPrompt(targetLangFull, sourceLangHint, contextStr);

                var payload = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = text }
                    },
                    temperature = 0.05,
                    top_p = 0.9,
                    max_tokens = 1024,
                    stream = true
                };

                request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                QuotaTracker.Instance.UpdateFromHeaders(response.Headers, "Groq AI (Streaming)");
                
                if (!response.IsSuccessStatusCode)
                {
                    if (!isRetry && (response.StatusCode == System.Net.HttpStatusCode.NotFound || 
                                     response.StatusCode == System.Net.HttpStatusCode.BadRequest || 
                                     response.StatusCode == (System.Net.HttpStatusCode)429))
                    {
                        _model = await GetFallbackModelAsync(_model);
                        await TranslateStreamInternalAsync(text, targetLanguage, sourceLanguage, onTokenReceived, isRetry: true);
                        return;
                    }

                    try
                    {
                        await SharedGoogleFallback.TranslateStreamAsync(text, targetLanguage, sourceLanguage, onTokenReceived);
                        return;
                    }
                    catch
                    {
                        onTokenReceived(text);
                        return;
                    }
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new System.IO.StreamReader(stream);

                string rawBuffer = "";
                string lastEmitted = "";

                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    if (line.StartsWith("data: "))
                    {
                        string data = line.Substring(6).Trim();
                        if (data == "[DONE]") break;

                        try
                        {
                            using var doc = JsonDocument.Parse(data);
                            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                            {
                                var delta = choices[0].GetProperty("delta");
                                if (delta.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
                                {
                                    string token = contentElement.GetString() ?? "";
                                    if (!string.IsNullOrEmpty(token))
                                    {
                                        rawBuffer += token;
                                        string cleaned = HallucinationDetector.CleanTranslationOutput(rawBuffer, text);
                                        if (!string.IsNullOrEmpty(cleaned) && cleaned != lastEmitted)
                                        {
                                            lastEmitted = cleaned;
                                            onTokenReceived(cleaned);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }

                string finalClean = HallucinationDetector.CleanTranslationOutput(rawBuffer, text);
                if (!string.IsNullOrWhiteSpace(finalClean))
                {
                    if (finalClean != lastEmitted)
                    {
                        onTokenReceived(finalClean);
                    }

                    _cachedInput = text;
                    _cachedTarget = targetLanguage;
                    _cachedSource = sourceLanguage;
                    _cachedStyle = JapaneseStyle;
                    _cachedResult = finalClean;
                    _lastTranslatedInput = text;
                    
                    _history.Enqueue($"- Source: \"{text}\" | Translation: \"{finalClean}\"");
                    if (_history.Count > MaxHistory) _history.Dequeue();
                }
            }
            catch (Exception)
            {
                try
                {
                    await SharedGoogleFallback.TranslateStreamAsync(text, targetLanguage, sourceLanguage, onTokenReceived);
                }
                catch
                {
                    onTokenReceived(text);
                }
            }
        }

        public string CurrentModel => _model;

        public void SetModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model) || !IsValidTextModel(model))
            {
                _model = "";
                return;
            }

            _model = model;
        }

        private static string BuildSystemPrompt(string targetLangFull, string sourceLangHint, string contextStr)
        {
            return $"You are a high-precision live subtitle and speech translator. Translate the user's input directly into {targetLangFull} with strict factual accuracy and natural spoken phrasing.\n\n" +
                   $"Strict Translation Rules (Anti-Hallucination):\n" +
                   $"1. Faithful Accuracy: Translate ONLY what is explicitly present in the user's input. NEVER invent extra clauses, NEVER complete unfinished thoughts, NEVER answer questions in the input, and NEVER add polite filler that was not spoken.\n" +
                   $"2. Natural Spoken Register: Preserve the speaker's exact tone (formal, polite, casual, slang, or urgent) idiomatic to {targetLangFull}.\n" +
                   $"3. Service & Courtesy Expressions:\n" +
                   $"   - Polite acknowledgements (e.g. わかりました, かしこまりました, 了解, 알겠습니다): translate as natural acknowledgements ('Understood' / 'Got it' / 'Хорошо' / 'Зрозуміло' / 'Baiklah' / 'Baş üstə' / 'Tushunarli').\n" +
                   $"   - Waiting phrases (e.g. 少々お待ちください): translate naturally ('Please wait a moment' / 'Зачекайте хвилинку' / 'Минуточку' / 'Mohon tunggu sebentar').\n" +
                   $"   - Reassurance (e.g. 大丈夫, 問題ない, 괜찮아요): translate as 'It's all right' / 'No problem' / 'Все гаразд' / 'Tidak apa-apa'.\n" +
                   $"4. Mixed Script & OCR Noise Resilience: If the input has minor OCR spacing/character noise or mixed scripts (Kanji/Kana/Romaji, Hangul, Hanzi, Cyrillic/Latin, Georgian Mkhedruli, Greek), infer the exact intended words without hallucinating unrelated sentences.\n" +
                   $"5. Same-Language Passthrough: If the input is ALREADY in {targetLangFull}, output it unchanged (adjusting only to the requested script style if specified).\n" +
                   $"6. Short Fragments: If the input is a single word or short fragment, output ONLY the direct translation of that fragment.\n" +
                   $"7. Pure Output Only: Output ONLY the final translation in {targetLangFull}. Never include quotes, brackets, arrows (->), translator notes, pronunciation guides (unless explicitly requested by the target script style), or explanations.{sourceLangHint}{contextStr}";
        }

        private static double StringSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

            int maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0) return 1.0;

            int dist = LevenshteinDistance(a, b);
            return 1.0 - ((double)dist / maxLen);
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length, m = t.Length;
            if (n == 0) return m;
            if (m == 0) return n;

            var prev = new int[m + 1];
            var curr = new int[m + 1];

            for (int j = 0; j <= m; j++) prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                (prev, curr) = (curr, prev);
            }

            return prev[m];
        }
    }
}
