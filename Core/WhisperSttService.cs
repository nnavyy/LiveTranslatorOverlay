using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiveTranslatorOverlay.Core
{
    public class WhisperSttService : IAsyncDisposable
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _processingLock = new SemaphoreSlim(1, 1);
        private string _currentLanguage = "auto"; // "id", "en", "ru", etc.

        public WhisperSttService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
        }

        public async Task<bool> InitializeAsync(Action<string>? onProgress)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                onProgress?.Invoke("API Key kosong! Harap isi API Key Groq.");
                return false;
            }
            
            // Tidak perlu download model 480MB lagi karena kita pakai Groq Cloud Whisper API
            onProgress?.Invoke("Sistem STT Cloud (Groq) Siap!");
            return await Task.FromResult(true);
        }

        public Task ChangeLanguageAsync(string langTag)
        {
            // Groq Whisper expects ISO-639-1 format (e.g. "ru", "en", "ja")
            _currentLanguage = langTag;
            return Task.CompletedTask;
        }

        public async Task<string> ProcessAudioChunkAsync(float[] pcmData)
        {
            // Cek energi audio (VAD): Abaikan audio hening / desisan mikrofon
            // Suara manusia asli memiliki puncak (maxAmp) dan rata-rata (avgAmp) yang jelas.
            float sum = 0;
            float maxAmp = 0;
            for(int i = 0; i < pcmData.Length; i++)
            {
                float abs = Math.Abs(pcmData[i]);
                sum += abs;
                if (abs > maxAmp) maxAmp = abs;
            }
            float avgAmp = sum / pcmData.Length;

            // Jika suara terlalu kecil (hanya background hiss/silence), jangan kirim ke Groq.
            // Ini menghemat token API Groq dan mencegah halusinasi Whisper seperti "spasiba" / "subtitle by".
            if (maxAmp < 0.012f && avgAmp < 0.002f) {
                return string.Empty;
            }

            if (!await _processingLock.WaitAsync(0))
                return string.Empty; // Skip jika masih sibuk (biar tidak numpuk delay)

            try
            {
                byte[] wavBytes = CreateWavBytes(pcmData, 16000);

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey.Trim());

                var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(wavBytes);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
                content.Add(fileContent, "file", "chunk.wav");
                content.Add(new StringContent("whisper-large-v3"), "model");
                content.Add(new StringContent("json"), "response_format");
                content.Add(new StringContent("0.0"), "temperature"); // Temperature 0 mencegah halusinasi acak
                content.Add(new StringContent("Transcribe spoken conversation directly."), "prompt");
                
                // Kalau sourceLanguage bukan auto, paksakan bahasanya agar 100% akurat
                if (_currentLanguage != "auto")
                {
                    content.Add(new StringContent(_currentLanguage), "language");
                }

                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string errJson = await response.Content.ReadAsStringAsync();
                    try { System.IO.File.AppendAllText("groq_debug.txt", $"[HTTP ERROR] {response.StatusCode} - {errJson}\n"); } catch {}
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        throw new Exception("API Key Groq tidak valid (Invalid API Key)!");
                    else
                        throw new Exception($"HTTP {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("text", out var textEl))
                {
                    string text = textEl.GetString()?.Trim() ?? "";
                    
                    // Filter halusinasi umum dari Whisper (subtitle credits, spasiba, продолжение следует, dll)
                    if (HallucinationDetector.IsHallucination(text))
                    {
                        try { System.IO.File.AppendAllText("groq_debug.txt", $"[FILTERED] {text}\n"); } catch {}
                        return "";
                    }
                    
                    try { System.IO.File.AppendAllText("groq_debug.txt", $"[TRANSCRIPT] {text}\n"); } catch {}
                    return text;
                }

                return "";
            }
            catch (Exception ex)
            {
                try { System.IO.File.AppendAllText("groq_debug.txt", $"[EXCEPTION] {ex.Message}\n"); } catch {}
                throw new Exception($"Whisper API Error: {ex.Message}");
            }
            finally
            {
                _processingLock.Release();
            }
        }

        private byte[] CreateWavBytes(float[] pcmData, int sampleRate)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcmData.Length * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // Channels
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(pcmData.Length * 2);
            
            foreach (var sample in pcmData)
            {
                float s = sample * 32767f;
                if (s > 32767f) s = 32767f;
                if (s < -32768f) s = -32768f;
                writer.Write((short)s);
            }
            
            return ms.ToArray();
        }

        public ValueTask DisposeAsync()
        {
            _httpClient.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
