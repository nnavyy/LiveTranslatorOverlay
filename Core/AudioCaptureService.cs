using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace LiveTranslatorOverlay.Core
{
    public class AudioCaptureService : IDisposable
    {
        private IWaveIn? _capture;
        private ConcurrentQueue<float> _audioBuffer = new ConcurrentQueue<float>();
        private WaveFormat? _captureFormat;

        public event EventHandler<float[]>? AudioChunkReady;
        private bool _isCapturing;

        // Queue dengan batas 2 chunk agar tidak numpuk tapi tidak buang semua
        private readonly SemaphoreSlim _chunkQueue = new SemaphoreSlim(2, 2);

        // VAD settings
        private const float SilenceThreshold = 0.003f;  // Level suara dianggap hening
        private const int SilenceWindowMs = 800;         // Jeda diam (ms) buat potong kalimat (Ditingkatkan agar tidak mudah terpotong)
        private const int MinChunkMs = 3000;             // Minimum chunk 3 detik
        private const int MaxChunkMs = 12000;            // Maximum chunk 12 detik (paksa potong jika terlalu lama)

        public void Start()
        {
            if (_isCapturing) return;

#pragma warning disable CS0618
            var capture = new WasapiLoopbackCapture();
#pragma warning restore CS0618
            _capture = capture;
            _captureFormat = capture.WaveFormat;

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
            _isCapturing = true;

            Task.Run(ChunkEmitterLoop);
        }

        public void Stop()
        {
            _isCapturing = false;
            try { _capture?.StopRecording(); } catch { }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_captureFormat == null || e.BytesRecorded == 0) return;

            int bytesPerSample = _captureFormat.BitsPerSample / 8;
            int channels = _captureFormat.Channels;
            int sampleRate = _captureFormat.SampleRate;
            int totalSamples = e.BytesRecorded / bytesPerSample;

            // Step 1: Read raw samples as float
            float[] rawSamples = new float[totalSamples];
            if (_captureFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                for (int i = 0; i < totalSamples; i++)
                    rawSamples[i] = BitConverter.ToSingle(e.Buffer, i * 4);
            }
            else
            {
                for (int i = 0; i < totalSamples; i++)
                    rawSamples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
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
        }

        private async Task ChunkEmitterLoop()
        {
            const int sampleRate = 16000;
            int minChunkSamples = sampleRate * MinChunkMs / 1000;
            int maxChunkSamples = sampleRate * MaxChunkMs / 1000;
            int silenceWindowSamples = sampleRate * SilenceWindowMs / 1000;

            var currentChunk = new List<float>(maxChunkSamples);

            while (_isCapturing)
            {
                // Drain buffer ke currentChunk
                while (_audioBuffer.TryDequeue(out float sample))
                    currentChunk.Add(sample);

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
                        shouldEmit = true;
                }

                if (shouldEmit && currentChunk.Count > 0)
                {
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

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
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
