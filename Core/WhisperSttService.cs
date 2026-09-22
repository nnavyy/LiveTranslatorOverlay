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
        private DateTime _rateLimitCooldownUntil = DateTime.MinValue;
        private DateTime _lastRequestTime = DateTime.MinValue;

        public event EventHandler<int>? RateLimitCooldownStarted;

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

            // Jika sedang dalam masa cooldown rate limit Groq (429), jangan kirim request dulu
            if (DateTime.UtcNow < _rateLimitCooldownUntil)
            {
                return string.Empty;
            }

            // Non-blocking lock check: jika sedang memproses chunk sebelumnya, lewati chunk ini
            // agar tidak terjadi antrian request yang menumpuk.
            if (!await _processingLock.WaitAsync(0))
                return string.Empty;

            try
            {
                // Pacing: Groq Free Tier Whisper dibatasi max 20 Request Per Menit (RPM).
                // Jaga jarak minimal 3.2 detik antar request agar aman dari batas 20 RPM.
                double msSinceLast = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
                if (msSinceLast < 3200)
                {
                    int waitMs = (int)(3200 - msSinceLast);
                    if (waitMs > 0 && waitMs <= 3200)
                        await Task.Delay(waitMs);
                }

                _lastRequestTime = DateTime.UtcNow;
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
                QuotaTracker.Instance.UpdateFromHeaders(response.Headers, "Groq Whisper (STT)");

                if (!response.IsSuccessStatusCode)
                {
                    string errJson = await response.Content.ReadAsStringAsync();
                    try { System.IO.File.AppendAllText("groq_debug.txt", $"[HTTP ERROR] {response.StatusCode} - {errJson}\n"); } catch {}
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        throw new Exception("API Key Groq tidak valid (Invalid API Key)!");
                    else if (response.StatusCode == (System.Net.HttpStatusCode)429)
                    {
                        // Groq Free Tier limit 20 RPM tercapai. Ambil waktu retry atau cooldown 8 detik.
                        int cooldownSecs = 8;
                        if (response.Headers.RetryAfter?.Delta.HasValue == true)
                        {
                            cooldownSecs = Math.Max(5, (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds + 1);
                        }
                        _rateLimitCooldownUntil = DateTime.UtcNow.AddSeconds(cooldownSecs);
                        RateLimitCooldownStarted?.Invoke(this, cooldownSecs);
                        QuotaTracker.Instance.SetCooldown("Groq Whisper", cooldownSecs);
                        return string.Empty; // Jangan lempar exception agar subtitle overlay tidak crash/rusak
                    }
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
                if (ex.Message.Contains("TooManyRequests") || ex.Message.Contains("429"))
                {
                    _rateLimitCooldownUntil = DateTime.UtcNow.AddSeconds(8);
                    RateLimitCooldownStarted?.Invoke(this, 8);
                    return string.Empty;
                }
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
