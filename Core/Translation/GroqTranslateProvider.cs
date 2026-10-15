using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiveTranslatorOverlay.Core.Translation
{
    public class GroqTranslateProvider : ITranslationProvider
    {
        private readonly HttpClient _httpClient;
        private string _apiKey;
        private string _model = ""; // Will be auto-set after fetching available models

        // Preferred models in order of priority (fastest/most accurate multilingual first)
        private static readonly string[] PreferredModels =
        {
            "qwen/qwen3.8-27b",         // Outstanding accuracy for Japanese, Russian, Asian & European languages
            "openai/gpt-oss-120b",      // High accuracy large language model
            "openai/gpt-oss-20b",       // Fast, high throughput text model
            "llama-3.3-70b-versatile",  // Standard Groq models (if available in user tier)
            "llama-3.1-8b-instant",
            "allam-2-7b"
        };

        private string _cachedInput = "";
        private string _cachedTarget = "";
        private string _cachedSource = "";
        private string _cachedResult = "";

        // Menyimpan history 3 kalimat terakhir untuk konteks terjemahan
        private readonly Queue<string> _history = new Queue<string>();
        private const int MaxHistory = 3;

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
                    "ja" => "Japanese",
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

                string systemPrompt = $"You are a highly accurate professional translator. Translate the text to {targetLangFull}.{sourceLangHint} If the text is already in {targetLangFull}, just return the exact text without any changes. Output ONLY the direct translation, nothing else. Do not add any notes, explanations, or hallucinate extra sentences.{contextStr}";

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
                    "ja" => "Japanese",
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

                string systemPrompt = $"You are a highly accurate professional translator. Translate the text to {targetLangFull}.{sourceLangHint} If the text is already in {targetLangFull}, just return the exact text without any changes. Output ONLY the direct translation, nothing else. Do not add any notes, explanations, or hallucinate extra sentences.{contextStr}";

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
    }
}
