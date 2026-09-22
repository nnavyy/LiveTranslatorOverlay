using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiveTranslatorOverlay.Core
{
    public class SpeakerTranscriptEventArgs : EventArgs
    {
        public int SpeakerIndex { get; set; }
        public string Text { get; set; } = "";
        /// <summary>True = interim (still speaking), False = final (sentence complete)</summary>
        public bool IsInterim { get; set; }
    }

    public class DiarizedSttService : IAsyncDisposable
    {
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;
        private readonly string _apiKey;
        private string _language = "auto";
        private Task? _receiveTask;

        /// <summary>
        /// Fired for every transcript update.
        /// IsInterim=true  → words are still coming in, display as "live preview" 
        /// IsInterim=false → sentence is finalized, trigger translation
        /// </summary>
        public event EventHandler<SpeakerTranscriptEventArgs>? TranscriptReady;

        public DiarizedSttService(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<bool> ConnectAsync(Action<string>? onProgress = null)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                onProgress?.Invoke("Deepgram API Key is empty! Fill in the Deepgram Key field.");
                return false;
            }

            try
            {
                _cts = new CancellationTokenSource();
                _webSocket = new ClientWebSocket();
                _webSocket.Options.SetRequestHeader("Authorization", $"Token {_apiKey}");

                // Build query string
                // When no language param is sent, Deepgram nova-2 defaults to English
                // but can still pick up other languages. When a specific language is set,
                // we send it explicitly for best accuracy.
                string langParam = string.IsNullOrEmpty(_language) || _language == "auto" || _language == "multi"
                    ? ""
                    : $"&language={_language}";

                var uri = new Uri(
                    $"wss://api.deepgram.com/v1/listen" +
                    $"?model=nova-2" +
                    $"&diarize=true" +
                    $"&punctuate=true" +
                    $"{langParam}" +
                    $"&encoding=linear16" +
                    $"&sample_rate=16000" +
                    $"&channels=1" +
                    $"&interim_results=true" +         // ← KEY CHANGE: real-time word-by-word updates
                    $"&vad_events=true" +              // detect speech start/end for cleaner segments
                    $"&endpointing=300"                // finalize after 300ms of silence (was waiting forever)
                );

                await _webSocket.ConnectAsync(uri, _cts.Token);
                onProgress?.Invoke("🔴 Deepgram Streaming active (Speaker Detection ON)");

                _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
                return true;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"Failed to connect to Deepgram: {ex.Message}");
                return false;
            }
        }

        public async Task SendAudioAsync(byte[] pcm16Bytes)
        {
            if (_webSocket?.State != WebSocketState.Open) return;
            try
            {
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(pcm16Bytes),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    _cts?.Token ?? CancellationToken.None);
            }
            catch { /* Ignore send errors; ReceiveLoop will detect disconnect */ }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            var buffer = new byte[32768]; // larger buffer for interim payloads
            var sb = new StringBuilder();

            Console.WriteLine($"[Deepgram] ReceiveLoop started. WebSocket state: {_webSocket?.State}");

            while (!token.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    sb.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Text)
                            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine($"[Deepgram] WebSocket closed by server: {result.CloseStatusDescription}");
                        break;
                    }
                    if (sb.Length > 0)
                    {
                        string msg = sb.ToString();
                        // Log ALL messages for debugging - NOT just first 200 chars
                        Console.WriteLine($"[Deepgram] Received ({msg.Length} chars): {msg[..Math.Min(msg.Length, 500)]}");
                        // Also write to file for debugging
                        try { System.IO.File.AppendAllText("deepgram_debug.txt",
                            $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
                        ParseAndEmit(msg);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Deepgram] ReceiveLoop error: {ex.Message}");
                    await Task.Delay(100, token);
                }
            }
            Console.WriteLine($"[Deepgram] ReceiveLoop ended. WebSocket state: {_webSocket?.State}");
        }

        private void ParseAndEmit(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Ignore non-Results messages (VAD events, metadata, etc.)
                if (!root.TryGetProperty("type", out var typeEl)) return;
                string msgType = typeEl.GetString() ?? "";
                if (msgType != "Results") return;

                // Determine if this is interim or final
                bool isFinal = root.TryGetProperty("is_final", out var finalEl) && finalEl.GetBoolean();

                if (!root.TryGetProperty("channel", out var channel)) return;
                if (!channel.TryGetProperty("alternatives", out var alternatives)) return;
                if (alternatives.GetArrayLength() == 0) return;

                var alt = alternatives[0];
                string transcript = alt.TryGetProperty("transcript", out var tEl) ? tEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(transcript)) return;

                // ── HALLUCINATION FILTER ──────────────────────────────────────────────
                // These phrases are commonly generated by models when hearing ambient
                // noise or silence. They come from training data artifacts.
                if (IsHallucination(transcript)) return;

                // If no words/diarization info, emit as speaker 0 (no confidence check)
                if (!alt.TryGetProperty("words", out var wordsEl) || wordsEl.GetArrayLength() == 0)
                {
                    TranscriptReady?.Invoke(this, new SpeakerTranscriptEventArgs 
                    { 
                        SpeakerIndex = 0, 
                        Text = transcript,
                        IsInterim = !isFinal
                    });
                    return;
                }

                // ── CONFIDENCE FILTER ─────────────────────────────────────────────────
                // Calculate average confidence and total speech duration across all words.
                // Low confidence = model guessing from noise. Short duration = noise spike.
                double totalConf = 0;
                int wordCount = 0;
                double speechStart = double.MaxValue;
                double speechEnd = 0;
                foreach (var word in wordsEl.EnumerateArray())
                {
                    if (word.TryGetProperty("confidence", out var confEl))
                    {
                        totalConf += confEl.GetDouble();
                        wordCount++;
                    }
                    // Track speech duration from word timestamps
                    if (word.TryGetProperty("start", out var startEl))
                        speechStart = Math.Min(speechStart, startEl.GetDouble());
                    if (word.TryGetProperty("end", out var endEl))
                        speechEnd = Math.Max(speechEnd, endEl.GetDouble());
                }

                double avgConf = wordCount > 0 ? totalConf / wordCount : 0;
                double speechDuration = speechEnd > speechStart ? speechEnd - speechStart : 0;

                // Deepgram doesn't hallucinate as much as Whisper, but confidence can be lower for non-English
                if (wordCount > 0 && avgConf < 0.20) return;

                // For final results: require minimal meaningful speech
                if (isFinal)
                {
                    // Block very short utterances (< 0.2s) with only 1 word and low confidence
                    if (speechDuration < 0.2 && wordsEl.GetArrayLength() <= 1 && avgConf < 0.50) return;
                }

                // ── GROUP BY SPEAKER ──────────────────────────────────────────────────
                var segments = new List<(int speaker, StringBuilder text)>();

                foreach (var word in wordsEl.EnumerateArray())
                {
                    int speaker = word.TryGetProperty("speaker", out var spEl) ? spEl.GetInt32() : 0;
                    string wordText = word.TryGetProperty("punctuated_word", out var pw) ? pw.GetString() ?? ""
                                    : word.TryGetProperty("word", out var wEl) ? wEl.GetString() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(wordText)) continue;

                    if (segments.Count > 0 && segments[^1].speaker == speaker)
                    {
                        segments[^1].text.Append(' ').Append(wordText);
                    }
                    else
                    {
                        segments.Add((speaker, new StringBuilder(wordText)));
                    }
                }

                foreach (var (spk, textSb) in segments)
                {
                    string segText = textSb.ToString().Trim();
                    if (!string.IsNullOrWhiteSpace(segText) && !IsHallucination(segText))
                    {
                        TranscriptReady?.Invoke(this, new SpeakerTranscriptEventArgs 
                        { 
                            SpeakerIndex = spk, 
                            Text = segText,
                            IsInterim = !isFinal
                        });
                    }
                }
            }
            catch { /* Ignore malformed JSON */ }
        }

        private static bool IsHallucination(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            string stripped = new string(text.Trim().ToLowerInvariant().Where(c => !char.IsPunctuation(c)).ToArray()).Trim();
            // Abaikan jika hanya 1 huruf atau simbol aneh (noise)
            if (stripped.Length <= 1) return true;

            return HallucinationDetector.IsHallucination(text);
        }

        public void SetLanguage(string lang) => _language = lang;

        public async ValueTask DisposeAsync()
        {
            _cts?.Cancel();
            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                }
                catch { }
            }
            _webSocket?.Dispose();
            _cts?.Dispose();
            if (_receiveTask != null)
            {
                try { await _receiveTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            }
        }
    }
}

