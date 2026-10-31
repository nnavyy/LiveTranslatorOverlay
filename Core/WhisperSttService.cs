using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
        private string _currentLanguage = "auto"; // "id", "en", "ru", "uk", etc.
        private DateTime _rateLimitCooldownUntil = DateTime.MinValue;
        private DateTime _lastRequestTime = DateTime.MinValue;

        public event EventHandler<int>? RateLimitCooldownStarted;

        public WhisperSttService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(45);
        }

        public async Task<bool> InitializeAsync(Action<string>? onProgress)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                onProgress?.Invoke("API Key kosong! Harap isi API Key Groq.");
                return false;
            }
            
            onProgress?.Invoke("Sistem STT Cloud (Groq) Siap!");
            return await Task.FromResult(true);
        }

        public Task ChangeLanguageAsync(string langTag)
        {
            if (string.IsNullOrWhiteSpace(langTag))
            {
                _currentLanguage = "auto";
            }
            else
            {
                // Groq Whisper expects 2-letter ISO-639-1 code (e.g. "zh-TW" -> "zh")
                string normalized = langTag.Trim().ToLowerInvariant();
                int dash = normalized.IndexOf('-');
                if (dash > 0) normalized = normalized.Substring(0, dash);
                _currentLanguage = normalized;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Checks if a 16kHz PCM audio chunk contains real sustained human voice activity,
        /// filtering out silence, microphone hiss, and isolated single-sample clicks/pops.
        /// </summary>
        public static bool HasSustainedVoiceActivity(float[] pcmData)
        {
            if (pcmData == null || pcmData.Length < 1600) return false;

            float sum = 0;
            float maxAmp = 0;
            for (int i = 0; i < pcmData.Length; i++)
            {
                float abs = Math.Abs(pcmData[i]);
                sum += abs;
                if (abs > maxAmp) maxAmp = abs;
            }
            float avgAmp = sum / pcmData.Length;

            // Reject if EITHER peak amplitude is too quiet OR average energy is pure silence/hiss
            if (maxAmp < 0.012f || avgAmp < 0.0018f)
                return false;

            // Frame-based VAD: count 20ms frames (320 samples @ 16kHz) with speech-level energy
            const int frameSize = 320;
            int totalFrames = pcmData.Length / frameSize;
            if (totalFrames == 0) return false;

            int voicedFrames = 0;
            for (int f = 0; f < totalFrames; f++)
            {
                float frameSum = 0;
                int offset = f * frameSize;
                for (int i = 0; i < frameSize; i++)
                {
                    frameSum += Math.Abs(pcmData[offset + i]);
                }
                if (frameSum / frameSize >= 0.0045f)
                {
                    voicedFrames++;
                }
            }

            // Require at least 6% of frames (or at least 8 frames = 160ms) to contain voice energy
            double voicedRatio = (double)voicedFrames / totalFrames;
            return voicedFrames >= 6 && voicedRatio >= 0.05;
        }

        public async Task<string> ProcessAudioChunkAsync(float[] pcmData)
        {
            // Strict multi-metric VAD check before calling Whisper API
            if (!HasSustainedVoiceActivity(pcmData))
            {
                return string.Empty;
            }

            // Cooldown rate limit check
            if (DateTime.UtcNow < _rateLimitCooldownUntil)
            {
                return string.Empty;
            }

            // Non-blocking lock check
            if (!await _processingLock.WaitAsync(0))
                return string.Empty;

            try
            {
                // Pacing: Groq Free Tier Whisper max 20 RPM
                double msSinceLast = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
                if (msSinceLast < 3500)
                {
                    int waitMs = (int)(3500 - msSinceLast);
                    if (waitMs > 0 && waitMs <= 3500)
                        await Task.Delay(waitMs);
                }

                _lastRequestTime = DateTime.UtcNow;
                byte[] wavBytes = CreateWavBytes(pcmData, 16000);

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/audio/transcriptions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());

                var content = new MultipartFormDataContent();
                var fileContent = new ByteArrayContent(wavBytes);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
                content.Add(fileContent, "file", "chunk.wav");
                content.Add(new StringContent("whisper-large-v3"), "model");
                // Request verbose_json so we can inspect no_speech_prob, avg_logprob, and compression_ratio per segment
                content.Add(new StringContent("verbose_json"), "response_format");
                content.Add(new StringContent("0.0"), "temperature");
                
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
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        throw new Exception("API Key Groq tidak valid (Invalid API Key)!");
                    else if (response.StatusCode == (System.Net.HttpStatusCode)429)
                    {
                        int cooldownSecs = 8;
                        if (response.Headers.RetryAfter?.Delta.HasValue == true)
                        {
                            cooldownSecs = Math.Max(5, (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds + 1);
                        }
                        _rateLimitCooldownUntil = DateTime.UtcNow.AddSeconds(cooldownSecs);
                        RateLimitCooldownStarted?.Invoke(this, cooldownSecs);
                        QuotaTracker.Instance.SetCooldown("Groq Whisper", cooldownSecs);
                        return string.Empty;
                    }
                    else
                        throw new Exception($"HTTP {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                // 1. Inspect segment-level acoustic metrics (no_speech_prob, avg_logprob, compression_ratio)
                if (doc.RootElement.TryGetProperty("segments", out var segmentsEl) &&
                    segmentsEl.ValueKind == JsonValueKind.Array &&
                    segmentsEl.GetArrayLength() > 0)
                {
                    var validTextBuilder = new StringBuilder();
                    foreach (var seg in segmentsEl.EnumerateArray())
                    {
                        double noSpeechProb = seg.TryGetProperty("no_speech_prob", out var nsp) ? nsp.GetDouble() : 0.0;
                        double avgLogProb = seg.TryGetProperty("avg_logprob", out var alp) ? alp.GetDouble() : 0.0;
                        double compressionRatio = seg.TryGetProperty("compression_ratio", out var cr) ? cr.GetDouble() : 1.0;
                        string segText = seg.TryGetProperty("text", out var st) ? st.GetString()?.Trim() ?? "" : "";

                        if (string.IsNullOrWhiteSpace(segText))
                            continue;

                        // Filter acoustic hallucinations using OpenAI Whisper confidence thresholds:
                        // - High no_speech_prob (>0.65) or moderate (>0.48) combined with low token confidence (<-0.75)
                        // - Very low token confidence (avg_logprob < -1.15)
                        // - High compression ratio (>2.40) indicating repetitive loop hallucination
                        if (noSpeechProb > 0.65 ||
                            (noSpeechProb > 0.48 && avgLogProb < -0.75) ||
                            avgLogProb < -1.15 ||
                            compressionRatio > 2.40)
                        {
                            continue;
                        }

                        if (!HallucinationDetector.IsHallucination(segText))
                        {
                            if (validTextBuilder.Length > 0) validTextBuilder.Append(' ');
                            validTextBuilder.Append(segText);
                        }
                    }

                    string combined = validTextBuilder.ToString().Trim();
                    if (string.IsNullOrWhiteSpace(combined) || HallucinationDetector.IsHallucination(combined))
                        return string.Empty;
                    return combined;
                }

                // 2. Fallback to top-level text property
                if (doc.RootElement.TryGetProperty("text", out var textEl))
                {
                    string text = textEl.GetString()?.Trim() ?? "";
                    if (HallucinationDetector.IsHallucination(text))
                    {
                        return "";
                    }
                    return text;
                }

                return "";
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("TooManyRequests") || ex.Message.Contains("429"))
                {
                    _rateLimitCooldownUntil = DateTime.UtcNow.AddSeconds(8);
                    RateLimitCooldownStarted?.Invoke(this, 8);
                    return string.Empty;
                }
                if (ex is TaskCanceledException || ex is OperationCanceledException || ex.Message.Contains("Timeout") || ex.Message.Contains("canceled"))
                {
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
            
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + pcmData.Length * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // Channels
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
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
