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
        public Func<float[], Task>? AudioChunkHandler { get; set; }
        // Raw PCM16 bytes streamed continuously for Deepgram WebSocket mode
        public event EventHandler<byte[]>? RawPcmBytesAvailable;
        private bool _isCapturing;

        // Queue dengan batas 2 chunk agar tidak numpuk tapi tidak buang semua
        private readonly SemaphoreSlim _chunkQueue = new SemaphoreSlim(2, 2);

        // VAD settings
        private const float SilenceThreshold = 0.0025f;  // Level suara dianggap hening (lowered to catch quiet speech)
        private const int SilenceWindowMs = 800;         // Jeda hening (ms) — 800ms gives more natural sentence boundaries
        private const int MinChunkMs = 4000;             // Minimum chunk 4 detik (Whisper works best with 5-15s context)
        private const int MaxChunkMs = 12000;            // Maximum chunk 12 detik (Whisper sweet spot, better accuracy)

        // Overlap buffer: carry last 0.5s of previous chunk into next chunk to prevent mid-word cuts
        private const int OverlapMs = 500;
        private float[]? _overlapBuffer = null;

        public async Task StartAsync(string? deviceId = null, int? processId = null, CancellationToken cancellationToken = default)
        {
            if (_isCapturing) return;

            var builder = new NAudio.Wave.WasapiRecorderBuilder();

            if (processId.HasValue && processId.Value > 0)
            {
                builder.WithProcessLoopback((uint)processId.Value, NAudio.CoreAudioApi.ProcessLoopbackMode.IncludeTargetProcessTree);
            }
            else if (!string.IsNullOrEmpty(deviceId) && deviceId != "default")
            {
                var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
                var device = enumerator.GetDevice(deviceId);
                builder.WithDevice(device);
                
                // If it's a render device (speaker/headphone), MUST use loopback to capture output
                if (device.DataFlow == NAudio.CoreAudioApi.DataFlow.Render)
                {
                    builder.WithLoopbackCapture();
                }
            }
            else
            {
                // Default: use the standard loopback behavior which automatically picks the default endpoint
                builder.WithLoopbackCapture();
            }

            var capture = await builder.BuildAsync();
            if (cancellationToken.IsCancellationRequested)
            {
                capture.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }
            _capture = capture;
            _captureFormat = capture.WaveFormat;

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
            if (_captureFormat == null || buffer.Length == 0) return;

            int bytesPerSample = _captureFormat.BitsPerSample / 8;
            int channels = _captureFormat.Channels;
            int sampleRate = _captureFormat.SampleRate;
            int totalSamples = buffer.Length / bytesPerSample;

            // Decode the negotiated PCM subtype rather than treating all integers as Int16.
            float[] rawSamples = new float[totalSamples];
            bool isFloat = _captureFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                (_captureFormat is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
            for (int i = 0; i < totalSamples; i++)
            {
                var bytes = buffer.Slice(i * bytesPerSample, bytesPerSample);
                rawSamples[i] = (isFloat, _captureFormat.BitsPerSample) switch
                {
                    (true, 32) => BitConverter.ToSingle(bytes),
                    (true, 64) => (float)BitConverter.ToDouble(bytes),
                    (false, 8) => (bytes[0] - 128) / 128f,
                    (false, 16) => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
                    (false, 24) => ((bytes[0] | bytes[1] << 8 | bytes[2] << 16) << 8 >> 8) / 8388608f,
                    (false, 32) => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648f,
                    _ => throw new NotSupportedException("Unsupported audio sample format.")
                };
                if (!float.IsFinite(rawSamples[i])) rawSamples[i] = 0;
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

            // Step 3: Downsample to 16kHz using linear interpolation (prevents aliasing artifacts)
            double ratio = (double)sampleRate / 16000.0;
            int targetSamples = (int)(monoSamples / ratio);
            for (int i = 0; i < targetSamples; i++)
            {
                double srcPos = i * ratio;
                int srcIndex = (int)srcPos;
                double frac = srcPos - srcIndex;
                
                float sample;
                if (srcIndex + 1 < mono.Length)
                    sample = (float)(mono[srcIndex] * (1.0 - frac) + mono[srcIndex + 1] * frac);
                else if (srcIndex < mono.Length)
                    sample = mono[srcIndex];
                else
                    continue;
                
                _audioBuffer.Enqueue(sample);
            }

            // Also stream raw PCM16 bytes directly for Deepgram WebSocket mode (with linear interpolation)
            if (RawPcmBytesAvailable != null)
            {
                byte[] pcm16 = new byte[targetSamples * 2];
                for (int i = 0; i < targetSamples; i++)
                {
                    double srcPos = i * ratio;
                    int srcIdx = (int)srcPos;
                    double frac = srcPos - srcIdx;
                    
                    float s;
                    if (srcIdx + 1 < mono.Length)
                        s = (float)(mono[srcIdx] * (1.0 - frac) + mono[srcIdx + 1] * frac) * 32767f;
                    else if (srcIdx < mono.Length)
                        s = mono[srcIdx] * 32767f;
                    else
                        s = 0f;
                    
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
            int overlapSamples = sampleRate * OverlapMs / 1000;

            var currentChunk = new List<float>(maxChunkSamples);

            // Prepend overlap from previous chunk if available
            if (_overlapBuffer != null && _overlapBuffer.Length > 0)
            {
                currentChunk.AddRange(_overlapBuffer);
                _overlapBuffer = null;
            }

            int stagnantLoops = 0;
            int freshSamples = 0;

            while (_isCapturing)
            {
                bool gotData = false;
                // Drain buffer ke currentChunk
                while (_audioBuffer.TryDequeue(out float sample))
                {
                    currentChunk.Add(sample);
                    freshSamples++;
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
                else if (currentChunk.Count >= minChunkSamples || (freshSamples > 0 && currentChunk.Count >= sampleRate / 5 && stagnantLoops > 16))
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
                    else if (stagnantLoops > 16) // ~800ms without new data
                    {
                        // WASAPI Loopback stops sending data when audio stops.
                        // If we haven't received data for 800ms, force emit what we have.
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

                    if (maxAmp < 0.012f || avgChunkEnergy < 0.0018f)
                    {
                        // Hening total atau hanya klik sesaat, kosongkan buffer tanpa mengirim ke STT
                        currentChunk.Clear();
                        freshSamples = 0;
                        _overlapBuffer = null;
                        await Task.Delay(50);
                        continue;
                    }

                    // Save overlap buffer: last 0.5s of this chunk for continuity with next chunk
                    if (currentChunk.Count > overlapSamples)
                    {
                        _overlapBuffer = currentChunk.GetRange(currentChunk.Count - overlapSamples, overlapSamples).ToArray();
                    }
                    else
                    {
                        _overlapBuffer = null;
                    }

                    float[] chunkToEmit = currentChunk.ToArray();
                    currentChunk.Clear();
                    freshSamples = 0;

                    // Prepend overlap from previous chunk into next chunk
                    if (_overlapBuffer != null)
                    {
                        currentChunk.AddRange(_overlapBuffer);
                    }

                    // Coba ambil slot queue (non-blocking), kalau penuh skip chunk ini
                    // tapi jangan buang lebih dari 1 slot — tunggu sebentar dulu
                    if (await _chunkQueue.WaitAsync(200))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                AudioChunkReady?.Invoke(this, chunkToEmit);
                                var handler = AudioChunkHandler;
                                if (handler != null) await handler(chunkToEmit);
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
            _isCapturing = false;
            _capture?.Dispose();
            _capture = null;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
