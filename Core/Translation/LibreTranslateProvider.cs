using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiveTranslatorOverlay.Core.Translation
{
    public class LibreTranslateProvider : ITranslationProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public LibreTranslateProvider(string baseUrl = "https://translate.terraprint.co/translate")
        {
            _httpClient = new HttpClient();
            _baseUrl = baseUrl;
        }

        public async Task<string> TranslateAsync(string text, string targetLanguage = "id", string sourceLanguage = "auto")
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            try
            {
                var content = new StringContent(JsonSerializer.Serialize(new
                {
                    q = text,
                    source = sourceLanguage,
                    target = targetLanguage,
                    format = "text"
                }), System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_baseUrl, content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                using var document = JsonDocument.Parse(responseString);
                
                if (document.RootElement.TryGetProperty("translatedText", out var translatedText))
                {
                    return translatedText.GetString() ?? text;
                }

                return text;
            }
            catch (Exception)
            {
                // Fallback jika API gagal (misal rate limit)
                return "[Error Translate] " + text;
            }
        }
        
        public async Task TranslateStreamAsync(string text, string targetLanguage, string sourceLanguage, System.Action<string> onTokenReceived)
        {
            // LibreTranslate API does not support streaming, so buffer it
            string result = await TranslateAsync(text, targetLanguage, sourceLanguage);
            onTokenReceived(result);
        }
    }
}
