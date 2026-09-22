using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using NAudio.Wave;

using NAudio.CoreAudioApi;

namespace LiveTranslatorOverlay.Core
{
    public class AudioCaptureService : IDisposable
    {
        private NAudio.Wave.WasapiRecorder? _capture;
        private ConcurrentQueue<float> _audioBuffer = new ConcurrentQueue<float>();
        private WaveFormat? _captureFormat;

        public event EventHandler<float[]>? AudioChunkReady;
        // Raw PCM16 bytes streamed continuously for Deepgram WebSocket mode
        public event EventHandler<byte[]>? RawPcmBytesAvailable;
        private bool _isCapturing;
        private int _debugCallCount = 0;

        // Queue dengan batas 2 chunk agar tidak numpuk tapi tidak buang semua
        private readonly SemaphoreSlim _chunkQueue = new SemaphoreSlim(2, 2);

        // VAD settings
        private const float SilenceThreshold = 0.003f;  // Level suara dianggap hening
        private const int SilenceWindowMs = 500;         // Jeda hening (ms) untuk memotong kalimat lebih cepat
        private const int MinChunkMs = 1500;             // Minimum chunk 1.5 detik (jauh lebih responsif dari 3 detik)
        private const int MaxChunkMs = 7000;             // Maximum chunk 7 detik (potong jika terlalu panjang)

        public async Task StartAsync(string? deviceId = null, int? processId = null)
        {
            if (_isCapturing) return;

            var builder = new NAudio.Wave.WasapiRecorderBuilder();
            string captureInfo = "default-loopback";

            if (processId.HasValue && processId.Value > 0)
            {
                builder.WithProcessLoopback((uint)processId.Value, NAudio.CoreAudioApi.ProcessLoopbackMode.IncludeTargetProcessTree);
                captureInfo = $"process-loopback-pid:{processId.Value}";
            }
            else if (!string.IsNullOrEmpty(deviceId) && deviceId != "default")
            {
                var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var device = enumerator.GetDevice(deviceId);
                captureInfo = $"device:{device.FriendlyName} ({device.DataFlow})";
                builder.WithDevice(device);
                
                // If it's a render device (speaker/headphone), MUST use loopback to capture output
                if (device.DataFlow == NAudio.CoreAudioApi.DataFlow.Render)
                {
                    builder.WithLoopbackCapture();
                    captureInfo += " [loopback]";
                }
            }
            else
            {
                // Default: use the standard loopback behavior which automatically picks the default endpoint
                var defaultEnum = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var defaultDevice = defaultEnum.GetDefaultAudioEndpoint(
                    NAudio.CoreAudioApi.DataFlow.Render,
                    NAudio.CoreAudioApi.Role.Multimedia);
                captureInfo = $"default-render-loopback:{defaultDevice.FriendlyName}";
                builder.WithLoopbackCapture();
            }

            _capture = await builder.BuildAsync();
            _captureFormat = _capture.WaveFormat;

            // Log startup info
            try {
                System.IO.File.WriteAllText("audio_debug.txt",
                    $"[STARTED] CaptureMode={captureInfo}\n" +
                    $"[FORMAT] {_captureFormat.SampleRate}Hz, {_captureFormat.BitsPerSample}bit, {_captureFormat.Channels}ch, Enc={_captureFormat.Encoding}\n");
            } catch { }

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _isCapturing = true;

            _ = Task.Run(ChunkEmitterLoop);
        }

        public void Stop()
        {
            _isCapturing = false;
            try { _capture?.StopRecording(); } catch { }
        }

        private void OnDataAvailable(ReadOnlySpan<byte> buffer, NAudio.CoreAudioApi.AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
        {
            // DEBUG LOGGING - log first 10 calls unconditionally, then every 100 calls
            _debugCallCount++;
            if (_debugCallCount <= 10 || _debugCallCount % 100 == 1)
            {
                float maxAmp = 0f;
                bool isFloatFmt = _captureFormat != null && (_captureFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                    (_captureFormat.Encoding == WaveFormatEncoding.Extensible && _captureFormat.BitsPerSample == 32));
                
                if (isFloatFmt && buffer.Length > 0)
                {
                    var checkSpan = MemoryMarshal.Cast<byte, float>(buffer);
                    for (int ci = 0; ci < Math.Min(checkSpan.Length, 256); ci++)
                        if (Math.Abs(checkSpan[ci]) > maxAmp) maxAmp = Math.Abs(checkSpan[ci]);
                }
                try {
                    System.IO.File.AppendAllText("audio_debug.txt",
                        $"[{DateTime.Now:HH:mm:ss.fff}] call#{_debugCallCount} bytes={buffer.Length} flags={flags} maxAmp={maxAmp:F4}\n");
                } catch { }
            }

            if (_captureFormat == null || buffer.Length == 0) return;

            int bytesPerSample = _captureFormat.BitsPerSample / 8;
            int channels = _captureFormat.Channels;
            int sampleRate = _captureFormat.SampleRate;
            int totalSamples = buffer.Length / bytesPerSample;

            // Step 1: Read raw samples as float
            float[] rawSamples = new float[totalSamples];
            bool isFloat = _captureFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                           (_captureFormat.Encoding == WaveFormatEncoding.Extensible && _captureFormat.BitsPerSample == 32);

            if (isFloat)
            {
                var floatSpan = MemoryMarshal.Cast<byte, float>(buffer);
                floatSpan.CopyTo(rawSamples);
            }
            else
            {
                var shortSpan = MemoryMarshal.Cast<byte, short>(buffer);
                for (int i = 0; i < totalSamples; i++)
                    rawSamples[i] = shortSpan[i] / 32768f;
            }

            // Step 2: Mix down to mono
            int monoSamples = totalSamples / channels;
            float[] mono = new float[monoSamples];
            for (int i = 0; i < monoSamples; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += rawSamples[i * channels + c];
                mono[i] = sum / channels;
            }

            // Step 3: Downsample to 16kHz
            double ratio = (double)sampleRate / 16000.0;
            int targetSamples = (int)(monoSamples / ratio);
            for (int i = 0; i < targetSamples; i++)
            {
                int srcIndex = (int)(i * ratio);
                if (srcIndex < mono.Length)
                    _audioBuffer.Enqueue(mono[srcIndex]);
            }

            // Also stream raw PCM16 bytes directly for Deepgram WebSocket mode
            if (RawPcmBytesAvailable != null)
            {
                byte[] pcm16 = new byte[targetSamples * 2];
                for (int i = 0; i < targetSamples; i++)
                {
                    int srcIndex = (int)(i * ratio);
                    float s = srcIndex < mono.Length ? mono[srcIndex] * 32767f : 0f;
                    if (s > 32767f) s = 32767f;
                    if (s < -32768f) s = -32768f;
                    short sample = (short)s;
                    pcm16[i * 2]     = (byte)(sample & 0xFF);
                    pcm16[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                }
                RawPcmBytesAvailable.Invoke(this, pcm16);
            }
        }

        private async Task ChunkEmitterLoop()
        {
            const int sampleRate = 16000;
            int minChunkSamples = sampleRate * MinChunkMs / 1000;
            int maxChunkSamples = sampleRate * MaxChunkMs / 1000;
            int silenceWindowSamples = sampleRate * SilenceWindowMs / 1000;

            var currentChunk = new List<float>(maxChunkSamples);

            int stagnantLoops = 0;

            while (_isCapturing)
            {
                bool gotData = false;
                // Drain buffer ke currentChunk
                while (_audioBuffer.TryDequeue(out float sample))
                {
                    currentChunk.Add(sample);
                    gotData = true;
                }

                if (gotData) stagnantLoops = 0;
                else stagnantLoops++;

                bool shouldEmit = false;

                if (currentChunk.Count >= maxChunkSamples)
                {
                    // Paksa potong kalau sudah terlalu panjang
                    shouldEmit = true;
                }
                else if (currentChunk.Count >= minChunkSamples)
                {
                    // Cek apakah ada jeda diam di akhir chunk (VAD)
                    int checkFrom = Math.Max(0, currentChunk.Count - silenceWindowSamples);
                    float energy = 0;
                    for (int i = checkFrom; i < currentChunk.Count; i++)
                        energy += Math.Abs(currentChunk[i]);
                    float avgEnergy = energy / (currentChunk.Count - checkFrom);

                    if (avgEnergy < SilenceThreshold)
                    {
                        shouldEmit = true;
                    }
                    else if (stagnantLoops > 10) // ~500ms without new data
                    {
                        // WASAPI Loopback stops sending data when audio stops.
                        // If we haven't received data for 500ms, force emit what we have.
                        shouldEmit = true;
                    }
                }

                if (shouldEmit && currentChunk.Count > 0)
                {
                    // Pastikan chunk memiliki energi suara nyata (bukan hening total)
                    float totalEnergy = 0;
                    float maxAmp = 0;
                    for (int i = 0; i < currentChunk.Count; i++)
                    {
                        float abs = Math.Abs(currentChunk[i]);
                        totalEnergy += abs;
                        if (abs > maxAmp) maxAmp = abs;
                    }
                    float avgChunkEnergy = totalEnergy / currentChunk.Count;

                    if (maxAmp < 0.012f && avgChunkEnergy < 0.002f)
                    {
                        // Hening total, kosongkan buffer tanpa mengirim ke STT
                        currentChunk.Clear();
                        await Task.Delay(50);
                        continue;
                    }

                    float[] chunkToEmit = currentChunk.ToArray();
                    currentChunk.Clear();

                    // Coba ambil slot queue (non-blocking), kalau penuh skip chunk ini
                    // tapi jangan buang lebih dari 1 slot — tunggu sebentar dulu
                    if (await _chunkQueue.WaitAsync(200))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                try { System.IO.File.AppendAllText("chunk_debug.txt", $"[{DateTime.Now:HH:mm:ss.fff}] Emitting chunk with {chunkToEmit.Length} samples\n"); } catch {}
                                AudioChunkReady?.Invoke(this, chunkToEmit);
                            }
                            finally
                            {
                                _chunkQueue.Release();
                            }
                        });
                    }
                    // Kalau queue penuh setelah nunggu 200ms, chunk ini di-skip
                }

                await Task.Delay(50);
            }
        }

        private void OnRecordingStopped(object? sender, EventArgs e)
        {
            _capture?.Dispose();
            _capture = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
