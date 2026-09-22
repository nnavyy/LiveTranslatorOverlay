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

        // Preferred models in order of priority (fastest/cheapest first)
        private static readonly string[] PreferredModels =
        {
            "openai/gpt-oss-20b",         // Model utama (sangat cepat, reasoning terpisah jadi tidak error <think>)
            "llama-3.1-8b-instant",       
            "gemma2-9b-it",               
            "llama-3.3-70b-versatile",    
            "mixtral-8x7b-32768",
            "qwen/qwen3.6-27b",           
            "groq/compound-mini",
            "openai/gpt-oss-120b"
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
                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var model in data.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var idEl))
                            availableIds.Add(idEl.GetString() ?? "");
                    }
                }

                // Pick first preferred model that is actually available
                foreach (var preferred in PreferredModels)
                {
                    if (availableIds.Contains(preferred))
                        return preferred;
                }

                // Fallback: return the first text model in the list
                if (doc.RootElement.TryGetProperty("data", out var dataFallback) && dataFallback.GetArrayLength() > 0)
                {
                    if (dataFallback[0].TryGetProperty("id", out var fallbackId))
                        return fallbackId.GetString() ?? PreferredModels[0];
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
                            // Only text models (skip whisper, guard etc.)
                            if (id != null && !id.Contains("whisper") && !id.Contains("guard") && !id.Contains("vision"))
                                models.Add(id);
                        }
                    }
                }
            }
            catch { }

            return models;
        }

        public async Task<string> TranslateAsync(string text, string targetLanguage = "id", string sourceLanguage = "auto")
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            if (string.IsNullOrWhiteSpace(_apiKey)) return "[Error: Groq API Key kosong]";

            // Return cache if same
            if (text == _cachedInput && targetLanguage == _cachedTarget && sourceLanguage == _cachedSource)
                return _cachedResult;

            // Auto-select model if not set
            if (string.IsNullOrEmpty(_model))
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
                    temperature = 0.1, // Kurangi halusinasi
                    max_tokens = 1024 // Ditingkatkan agar model reasoning (seperti Qwen/DeepSeek yang pakai <think>) tidak terpotong sebelum memberikan terjemahan
                };

                request.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                QuotaTracker.Instance.UpdateFromHeaders(response.Headers, "Groq AI (Text)");

                if (!response.IsSuccessStatusCode)
                {
                    string errBody = await response.Content.ReadAsStringAsync();
                    
                    // If model not found, reset and retry once with fresh model list
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound || 
                        response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        _model = await GetBestAvailableModelAsync();
                        return await TranslateAsync(text, targetLanguage, sourceLanguage); // Retry once
                    }
                    
                    return $"[Groq Error: {response.StatusCode}] {errBody}";
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

                        // DeepSeek models usually wrap their thinking process in <think> tags.
                        // We must strip this out so it doesn't show up in the live translation.
                        if (result.Contains("<think>"))
                        {
                            int endThinkIndex = result.IndexOf("</think>");
                            if (endThinkIndex != -1)
                            {
                                result = result.Substring(endThinkIndex + 8).Trim();
                            }
                            else
                            {
                                // If the token limit cut off the thinking process before it finished,
                                // we just return the original text because the translation never happened.
                                return text; 
                            }
                        }

                        _cachedInput = text;
                        _cachedTarget = targetLanguage;
                        _cachedSource = sourceLanguage;
                        _cachedResult = result;

                        // Tambahkan ke history
                        _history.Enqueue($"[{text}] -> [{result}]");
                        if (_history.Count > MaxHistory) _history.Dequeue();

                        return result;
                    }
                }

                return text;
            }
            catch (Exception ex)
            {
                return $"[Groq Error: {ex.Message}]";
            }
        }

        public async Task TranslateStreamAsync(string text, string targetLanguage, string sourceLanguage, System.Action<string> onTokenReceived)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                onTokenReceived("[Error: Groq API Key kosong]");
                return;
            }

            if (string.IsNullOrEmpty(_model))
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

                string sourceLangHint = sourceLanguage != "auto" ? $" Source language: {sourceLanguage}." : "";
                
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
                    string errBody = await response.Content.ReadAsStringAsync();
                    onTokenReceived($"[Groq Error: {response.StatusCode}] {errBody}");
                    return;
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new System.IO.StreamReader(stream);

                string fullResult = "";
                bool isThinking = false;

                while (!reader.EndOfStream)
                {
                    string? line = await reader.ReadLineAsync();
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
            catch (Exception ex)
            {
                onTokenReceived($"[Groq Streaming Error: {ex.Message}]");
            }
        }

        public string CurrentModel => _model;

        public void SetModel(string model)
        {
            _model = model;
        }
    }
}
