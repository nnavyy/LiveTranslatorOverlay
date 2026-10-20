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
        private string _apiKey;
        private string _model = ""; // Will be auto-set after fetching available models

        public JapaneseScriptStyle JapaneseStyle { get; set; } = JapaneseScriptStyle.Standard;

        // Preferred models in order of priority (fastest/most accurate multilingual first)
        private static readonly string[] PreferredModels =
        {
            "llama-3.3-70b-versatile",  // Best accuracy, tone nuance & idiom handling for Japanese, Russian, Asian & European languages
            "llama-3.1-8b-instant"       // High speed fallback
        };

        private string _cachedInput = "";
        private string _cachedTarget = "";
        private string _cachedSource = "";
        private string _cachedResult = "";

        // Menyimpan history 5 kalimat terakhir untuk konteks terjemahan (deeper context improves coherence)
        private readonly Queue<string> _history = new Queue<string>();
        private const int MaxHistory = 5;

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
        /// strictly excluding safeguard, moderation, audio, TTS, and whisper models
        /// which have severe rate limits (e.g. 3 RPM) and are unsuitable for translation.
        /// </summary>
        public static bool IsValidTextModel(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            string lower = id.ToLowerInvariant();
            if (lower.Contains("guard")) return false; // Excludes prompt-guard, safeguard
            if (lower.Contains("whisper")) return false; // Audio STT
            if (lower.Contains("vision")) return false;
            if (lower.Contains("embed")) return false;
            if (lower.Contains("orpheus")) return false; // Audio TTS
            if (lower.Contains("audio")) return false;
            if (lower.Contains("moderation")) return false;
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

                // 1. Pick first preferred model that is actually available
                foreach (var preferred in PreferredModels)
                {
                    if (availableIds.Contains(preferred))
                        return preferred;
                }

                // 2. Fallback: return the first VALID text model (NEVER safeguard/guard/whisper)
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
                if (currentModel.Contains("qwen", StringComparison.OrdinalIgnoreCase))
                    return "openai/gpt-oss-20b";
                if (currentModel.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase))
                    return "qwen/qwen3.8-27b";

                var models = await GetAvailableModelsAsync();
                foreach (var m in models)
                {
                    if (!string.Equals(m, currentModel, StringComparison.OrdinalIgnoreCase) && IsValidTextModel(m))
                        return m;
                }
            }
            catch { }

            return "openai/gpt-oss-20b";
        }

        public async Task<string> TranslateAsync(string text, string targetLanguage = "id", string sourceLanguage = "auto")
        {
            return await TranslateInternalAsync(text, targetLanguage, sourceLanguage, isRetry: false);
        }

        private async Task<string> TranslateInternalAsync(string text, string targetLanguage, string sourceLanguage, bool isRetry)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            if (string.IsNullOrWhiteSpace(_apiKey)) return "[Error: Groq API Key kosong]";

            // Return cache if same
            if (text == _cachedInput && targetLanguage == _cachedTarget && sourceLanguage == _cachedSource)
                return _cachedResult;

            // Deduplication: skip if new text is >85% similar to last translated text
            if (!string.IsNullOrEmpty(_lastTranslatedInput) && StringSimilarity(text, _lastTranslatedInput) > 0.85)
                return _cachedResult;

            // Auto-select model if not set or invalid
            if (string.IsNullOrEmpty(_model) || !IsValidTextModel(_model))
            {
                _model = await GetBestAvailableModelAsync();
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                string targetLangFull = targetLanguage switch
                {
                    "en" => "English",
                    "id" => "Indonesian",
                    "ja" => JapaneseStyle switch
                    {
                        JapaneseScriptStyle.Hiragana => "Japanese (written in pure Hiragana and Katakana without Kanji)",
                        JapaneseScriptStyle.Furigana => "Japanese (with Hiragana furigana in parentheses immediately following each Kanji, e.g. 漢字(かんじ))",
                        JapaneseScriptStyle.Romaji => "Japanese (written in standard Hepburn Romaji Latin alphabet)",
                        JapaneseScriptStyle.Katakana => "Japanese (written exclusively in Katakana)",
                        _ => "Japanese"
                    },
                    "ko" => "Korean",
                    "zh" => "Chinese",
                    "es" => "Spanish",
                    "fr" => "French",
                    "de" => "German",
                    "ru" => "Russian",
                    "ar" => "Arabic",
                    _ => targetLanguage
                };

                string sourceLangFull = sourceLanguage switch
                {
                    "en" => "English",
                    "id" => "Indonesian",
                    "ja" => "Japanese",
                    "ko" => "Korean",
                    "zh" => "Chinese",
                    "es" => "Spanish",
                    "fr" => "French",
                    "de" => "German",
                    "ru" => "Russian",
                    "ar" => "Arabic",
                    _ => sourceLanguage
                };

                string sourceLangHint = sourceLanguage != "auto" ? $" Source language: {sourceLangFull}." : "";
                
                string contextStr = "";
                if (_history.Count > 0)
                {
                    contextStr = "\n\nPrevious conversation context (use this ONLY to understand the flow, DO NOT translate this again):\n" + string.Join("\n", _history);
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
                    temperature = 0.1,
                    max_tokens = 1024
                };

                request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                QuotaTracker.Instance.UpdateFromHeaders(response.Headers, "Groq AI (Text)");

                if (!response.IsSuccessStatusCode)
                {
                    // Retry once with alternative model on 404, 400, or 429
                    if (!isRetry && (response.StatusCode == System.Net.HttpStatusCode.NotFound || 
                                     response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
                                     response.StatusCode == (System.Net.HttpStatusCode)429))
                    {
                        _model = await GetFallbackModelAsync(_model);
                        return await TranslateInternalAsync(text, targetLanguage, sourceLanguage, isRetry: true);
                    }
                    
                    // Seamless fallback to Google Free Translate so the live translation never breaks
                    var googleFallback = new GoogleFreeTranslateProvider();
                    return await googleFallback.TranslateAsync(text, targetLanguage, sourceLanguage);
                }

                var responseString = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseString);
                var root = document.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                    {
                        string result = content.GetString()?.Trim() ?? text;

                        if (result.Contains("<think>"))
                        {
                            int endThinkIndex = result.IndexOf("</think>");
                            if (endThinkIndex != -1)
                            {
                                result = result.Substring(endThinkIndex + 8).Trim();
                            }
                            else
                            {
                                return text; 
                            }
                        }

                        _cachedInput = text;
                        _cachedTarget = targetLanguage;
                        _cachedSource = sourceLanguage;
                        _cachedResult = result;
                        _lastTranslatedInput = text;

                        _history.Enqueue($"[{text}] -> [{result}]");
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
                    var googleFallback = new GoogleFreeTranslateProvider();
                    return await googleFallback.TranslateAsync(text, targetLanguage, sourceLanguage);
                }
                catch
                {
                    return text;
                }
            }
        }

        public async Task TranslateStreamAsync(string text, string targetLanguage, string sourceLanguage, System.Action<string> onTokenReceived)
        {
            await TranslateStreamInternalAsync(text, targetLanguage, sourceLanguage, onTokenReceived, isRetry: false);
        }

        private async Task TranslateStreamInternalAsync(string text, string targetLanguage, string sourceLanguage, System.Action<string> onTokenReceived, bool isRetry)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                onTokenReceived("[Error: Groq API Key kosong]");
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

                string targetLangFull = targetLanguage switch
                {
                    "en" => "English",
                    "id" => "Indonesian",
                    "ja" => JapaneseStyle switch
                    {
                        JapaneseScriptStyle.Hiragana => "Japanese (written in pure Hiragana and Katakana without Kanji)",
                        JapaneseScriptStyle.Furigana => "Japanese (with Hiragana furigana in parentheses immediately following each Kanji, e.g. 漢字(かんじ))",
                        JapaneseScriptStyle.Romaji => "Japanese (written in standard Hepburn Romaji Latin alphabet)",
                        JapaneseScriptStyle.Katakana => "Japanese (written exclusively in Katakana)",
                        _ => "Japanese"
                    },
                    "ko" => "Korean",
                    "zh" => "Chinese",
                    "es" => "Spanish",
                    "fr" => "French",
                    "de" => "German",
                    "ru" => "Russian",
                    "ar" => "Arabic",
                    _ => targetLanguage
                };

                string sourceLangFull = sourceLanguage switch
                {
                    "en" => "English",
                    "id" => "Indonesian",
                    "ja" => "Japanese",
                    "ko" => "Korean",
                    "zh" => "Chinese",
                    "es" => "Spanish",
                    "fr" => "French",
                    "de" => "German",
                    "ru" => "Russian",
                    "ar" => "Arabic",
                    _ => sourceLanguage
                };

                string sourceLangHint = sourceLanguage != "auto" ? $" Source language: {sourceLangFull}." : "";
                
                string contextStr = "";
                if (_history.Count > 0)
                {
                    contextStr = "\n\nPrevious conversation context (use this ONLY to understand the flow, DO NOT translate this again):\n" + string.Join("\n", _history);
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
                    temperature = 0.1,
                    max_tokens = 1024,
                    stream = true
                };

                request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                QuotaTracker.Instance.UpdateFromHeaders(response.Headers, "Groq AI (Streaming)");
                
                if (!response.IsSuccessStatusCode)
                {
                    // If model not found or rate limited, retry once with fallback model
                    if (!isRetry && (response.StatusCode == System.Net.HttpStatusCode.NotFound || 
                                     response.StatusCode == System.Net.HttpStatusCode.BadRequest || 
                                     response.StatusCode == (System.Net.HttpStatusCode)429))
                    {
                        _model = await GetFallbackModelAsync(_model);
                        await TranslateStreamInternalAsync(text, targetLanguage, sourceLanguage, onTokenReceived, isRetry: true);
                        return;
                    }

                    // Seamless fallback to Google Free Translate so the live subtitle never displays raw JSON errors
                    try
                    {
                        var googleFallback = new GoogleFreeTranslateProvider();
                        await googleFallback.TranslateStreamAsync(text, targetLanguage, sourceLanguage, onTokenReceived);
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

                string fullResult = "";
                bool isThinking = false;

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
                                    string content = contentElement.GetString() ?? "";
                                    
                                    // Handle reasoning models <think> tags
                                    if (content.Contains("<think>"))
                                    {
                                        isThinking = true;
                                        content = content.Replace("<think>", "");
                                    }
                                    if (content.Contains("</think>"))
                                    {
                                        isThinking = false;
                                        content = content.Substring(content.IndexOf("</think>") + 8);
                                    }

                                    if (!isThinking && !string.IsNullOrEmpty(content))
                                    {
                                        fullResult += content;
                                        onTokenReceived(fullResult);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Update history
                if (!string.IsNullOrWhiteSpace(fullResult))
                {
                    _cachedInput = text;
                    _cachedTarget = targetLanguage;
                    _cachedSource = sourceLanguage;
                    _cachedResult = fullResult.Trim();
                    
                    _history.Enqueue($"[{text}] -> [{_cachedResult}]");
                    if (_history.Count > MaxHistory) _history.Dequeue();
                }
            }
            catch (Exception)
            {
                try
                {
                    var googleFallback = new GoogleFreeTranslateProvider();
                    await googleFallback.TranslateStreamAsync(text, targetLanguage, sourceLanguage, onTokenReceived);
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
            return $"You are an expert subtitle, dialogue, and live speech translator. Translate spoken conversation into {targetLangFull} with high accuracy, natural phrasing, and authentic conversational flow.\n\n" +
                   $"Key translation guidelines:\n" +
                   $"1. Natural Tone & Idioms: Avoid stiff, word-for-word machine translation. Convey politeness, casualness, slang, emotion, and speaker intent naturally in {targetLangFull}.\n" +
                   $"2. Service & Courtesy Phrases:\n" +
                   $"   - Polite acknowledgements (e.g. わかりました, かしこまりました, 了解): translate as natural conversational acknowledgements (e.g. into Russian: 'Хорошо' / 'Понятно' / 'Договорились'; into Indonesian: 'Baiklah' / 'Dimengerti'; into English: 'Understood' / 'Got it').\n" +
                   $"   - Service/waiting phrases (e.g. 少々お待ちください, お待ちください): translate naturally (e.g. into Russian: 'Минуточку, пожалуйста' / 'Подождите секунду'; into Indonesian: 'Mohon tunggu sebentar'; into English: 'Please wait a moment').\n" +
                   $"   - Reassurance phrases (e.g. 大丈夫, 問題ない): translate as 'Всё в порядке' / 'Tidak apa-apa' / 'It's all right'.\n" +
                   $"3. Mixed Script & OCR Robustness: If the input contains mixed Kanji, Hiragana, Katakana, Romaji, or minor OCR noise/spacing artifacts, correctly understand the intended sentence and translate accurately without error.\n" +
                   $"4. Same-language passthrough: If the input text is ALREADY in {targetLangFull}, return it EXACTLY as-is without any modifications, rephrasing, or corrections. Do NOT paraphrase same-language text.\n" +
                   $"5. Incomplete utterances: If the input is a short, incomplete fragment (e.g. 'Yeah', 'Hmm', 'OK'), translate it directly without inventing additional context or expanding it into a full sentence.\n" +
                   $"6. Format: Output ONLY the final direct translation in {targetLangFull}. Never output quotes, notes, pronunciation guides, asterisks, brackets, markdown, or explanations. Never add translator's notes, footnotes, or commentary.{sourceLangHint}{contextStr}";
        }

        /// <summary>
        /// Simple Levenshtein-based string similarity ratio (0.0 = completely different, 1.0 = identical).
        /// Used for deduplication to avoid re-translating near-identical STT outputs.
        /// </summary>
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

            // Use single-row optimization for memory efficiency
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
