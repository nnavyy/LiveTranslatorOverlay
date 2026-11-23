using System.Text;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LiveTranslatorOverlay.Core;

/// <summary>Multilingual local recognition using Vulkan/Metal GPU when available, with automatic CPU fallback.</summary>
public sealed class LocalSpeechService : IAsyncDisposable
{
    private readonly LocalSpeechModelTier _requestedTier;
    private LocalSpeechModelTier _activeTier;
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private WhisperProcessor? _streamingShortProcessor;
    private WhisperProcessor? _streamingMediumProcessor;
    private WhisperVadFactory? _vadFactory;
    private WhisperVadProcessor? _vad;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    public string Language { get; private set; } = "";
    public bool UsingGpu { get; private set; }
    public string BackendLabel { get; private set; } = "CPU";
    public LocalSpeechModelTier ActiveTier => _activeTier;
    public string ActiveModelLabel => SpeechModelDownload.GetModelDisplayName(_activeTier);

    public LocalSpeechService(bool small = false)
        : this(small ? LocalSpeechModelTier.Small : LocalSpeechModelTier.Base)
    {
    }

    public LocalSpeechService(LocalSpeechModelTier tier)
    {
        _requestedTier = tier;
    }

    public async Task InitializeAsync(string language, CancellationToken cancellationToken, Action<string>? progress = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        await _serial.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_processor != null) throw new InvalidOperationException("Speech is already initialized.");

            bool gpuCandidate = _requestedTier is LocalSpeechModelTier.LargeTurbo or LocalSpeechModelTier.Auto
                && SpeechModelDownload.HasGpuAccelerationAvailable();

            RuntimeOptions.RuntimeLibraryOrder = BuildRuntimeOrder(gpuCandidate);

            string normalized = language == "auto" ? "auto" : language.Split('-')[0].ToLowerInvariant();
            if (normalized != "auto" && !WhisperFactory.GetSupportedLanguages().Contains(normalized))
                throw new InvalidOperationException("This speech language is not supported. Choose a supported language or Auto-detect.");

            _activeTier = SpeechModelDownload.ResolveEffectiveTier(_requestedTier, gpuCandidate);
            string path = await SpeechModelDownload.EnsureTierAsync(_activeTier, progress, token);
            string vadPath = await SpeechModelDownload.EnsureVadAsync(progress, token);

            progress?.Invoke($"Loading {ActiveModelLabel} on {(gpuCandidate ? (OperatingSystem.IsMacOS() ? "Metal GPU" : "Vulkan GPU") : "CPU")}...");
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();

                bool loadedWithGpu = false;
                if (gpuCandidate)
                {
                    foreach (bool flashAttn in new[] { true, false })
                    {
                        try
                        {
                            _factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions
                            {
                                UseGpu = true,
                                UseFlashAttention = flashAttn
                            });
                            loadedWithGpu = OperatingSystem.IsMacOS() ||
                                            RuntimeOptions.LoadedLibrary == RuntimeLibrary.Vulkan;
                            if (loadedWithGpu) break;
                        }
                        catch
                        {
                            _factory?.Dispose();
                            _factory = null;
                            loadedWithGpu = false;
                        }
                    }
                }

                if (_factory == null)
                {
                    RuntimeOptions.RuntimeLibraryOrder = BuildRuntimeOrder(allowGpu: false);
                    _factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = false });
                    loadedWithGpu = false;
                }

                UsingGpu = loadedWithGpu;
                BackendLabel = loadedWithGpu
                    ? (OperatingSystem.IsMacOS() ? "Metal GPU" : "Vulkan GPU")
                    : "CPU";

                _processor = BuildProcessor(_factory, normalized, singleSegment: false, audioContextSize: null);
                _streamingShortProcessor = BuildProcessor(_factory, normalized, singleSegment: true, audioContextSize: 256);
                _streamingMediumProcessor = BuildProcessor(_factory, normalized, singleSegment: true, audioContextSize: 448);

                _vadFactory = WhisperVadFactory.FromPath(vadPath, new WhisperFactoryOptions { UseGpu = false });
                _vad = _vadFactory.CreateBuilder()
                    .WithUseGpu(false)
                    .WithThreads(2)
                    .WithThreshold(0.40f)
                    .WithMinSpeechDuration(TimeSpan.FromMilliseconds(120))
                    .WithMinSilenceDuration(TimeSpan.FromMilliseconds(250))
                    .WithSpeechPadding(TimeSpan.FromMilliseconds(200))
                    .Build();
            }, token);

            if (UsingGpu)
            {
                progress?.Invoke("Warming up GPU shaders...");
                await Task.Run(async () =>
                {
                    var warmupAudio = new float[8000];
                    if (_streamingShortProcessor != null)
                        await foreach (var _ in _streamingShortProcessor.ProcessAsync(warmupAudio, token)) { }
                    if (_streamingMediumProcessor != null)
                        await foreach (var _ in _streamingMediumProcessor.ProcessAsync(warmupAudio, token)) { }
                }, token);
            }

            token.ThrowIfCancellationRequested();
            Language = language;
        }
        finally { _serial.Release(); }
    }

    public static float[] TrimToSpeechSpan(float[] samples, IReadOnlyList<VadSegmentData> segments)
    {
        if (samples.Length == 0 || segments.Count == 0)
            return [];

        const int sampleRate = 16000;
        const int padSamples = 250 * sampleRate / 1000;      // 4000 samples (250 ms) to keep soft speech onsets intact
        const int mergeGapSamples = 700 * sampleRate / 1000; // 11200 samples (700 ms)
        const int minSpanSamples = 100 * sampleRate / 1000;  // 1600 samples (100 ms)

        var intervals = new List<(int Start, int End)>(segments.Count);
        foreach (var seg in segments)
        {
            int s = Math.Clamp((int)Math.Floor(seg.Start.TotalSeconds * sampleRate) - padSamples, 0, samples.Length);
            int e = Math.Clamp((int)Math.Ceiling(seg.End.TotalSeconds * sampleRate) + padSamples, 0, samples.Length);
            if (e > s)
                intervals.Add((s, e));
        }

        if (intervals.Count == 0)
            return [];

        intervals.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        var merged = new List<(int Start, int End)>(intervals.Count);
        var current = intervals[0];
        for (int i = 1; i < intervals.Count; i++)
        {
            var next = intervals[i];
            if (next.Start - current.End <= mergeGapSamples)
            {
                current = (current.Start, Math.Max(current.End, next.End));
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);

        int totalLength = 0;
        foreach (var range in merged)
            totalLength += range.End - range.Start;

        if (totalLength < minSpanSamples)
            return [];

        var trimmed = new float[totalLength];
        int offset = 0;
        foreach (var range in merged)
        {
            int len = range.End - range.Start;
            Array.Copy(samples, range.Start, trimmed, offset, len);
            offset += len;
        }
        return trimmed;
    }

    public Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken) =>
        TranscribeCoreAsync(samples, streamingWindow: false, cancellationToken);

    public Task<string> TranscribeStreamingStepAsync(float[] samples, CancellationToken cancellationToken) =>
        TranscribeCoreAsync(samples, streamingWindow: true, cancellationToken);

    private async Task<string> TranscribeCoreAsync(float[] samples, bool streamingWindow, CancellationToken cancellationToken)
    {
        if (samples.Length is <= 0 or > 320000 || Array.Exists(samples, value => !float.IsFinite(value)))
            throw new ArgumentException("Expected finite mono 16 kHz audio up to 20 seconds.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await _serial.WaitAsync(timeout.Token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_processor == null || _vad == null) throw new InvalidOperationException("Local speech is not initialized.");

            if (!HasNonZeroSignal(samples)) return "";
            var vadSegments = await _vad.DetectSpeechAsync(samples, timeout.Token);
            if (vadSegments.Count == 0) return "";

            float[] speechAudio = TrimToSpeechSpan(samples, vadSegments);
            if (speechAudio.Length == 0) return "";

            var processor = streamingWindow
                ? (speechAudio.Length <= 72_000
                    ? _streamingShortProcessor
                    : speechAudio.Length <= 136_000
                        ? _streamingMediumProcessor
                        : _processor)
                : _processor;

            if (processor == null) throw new InvalidOperationException("Local speech is not initialized.");

            var text = new StringBuilder();
            string previousSegment = "";
            await foreach (var segment in processor.ProcessAsync(speechAudio, timeout.Token))
            {
                if (segment.NoSpeechProbability >= 0.78f ||
                    (segment.NoSpeechProbability >= 0.60f && segment.Probability > 0f && segment.Probability < 0.45f))
                {
                    continue;
                }

                string line = HallucinationDetector.StripWhisperArtifacts(segment.Text);
                if (string.IsNullOrWhiteSpace(line) || HallucinationDetector.IsWhisperArtifact(line))
                    continue;

                if (string.Equals(line, previousSegment, StringComparison.OrdinalIgnoreCase))
                    continue;

                previousSegment = line;
                if (text.Length > 0) text.Append(' ');
                text.Append(line);
            }

            string result = text.ToString();
            return HallucinationDetector.IsHallucination(result) ? "" : result;
        }
        finally { _serial.Release(); }
    }

    private static List<RuntimeLibrary> BuildRuntimeOrder(bool allowGpu)
    {
        if (OperatingSystem.IsMacOS())
            return [RuntimeLibrary.Cpu];

        if (OperatingSystem.IsWindows())
        {
            return allowGpu
                ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx]
                : [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
        }

        return allowGpu
            ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu]
            : [RuntimeLibrary.Cpu];
    }

    private static bool HasNonZeroSignal(float[] samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) >= 1e-4f)
                return true;
        }
        return false;
    }

    private static WhisperProcessor BuildProcessor(WhisperFactory factory, string normalizedLanguage, bool singleSegment, int? audioContextSize)
    {
        var builder = factory.CreateBuilder()
            .WithThreads(Math.Clamp(Environment.ProcessorCount - 2, 1, 8))
            .WithNoContext()
            .WithTemperature(0f)
            .WithTemperatureInc(0f)
            .WithEntropyThreshold(2.4f)
            .WithLogProbThreshold(-1.0f)
            .WithNoSpeechThreshold(0.60f)
            .WithProbabilities();

        if (singleSegment)
        {
            builder.WithSingleSegment();
            builder.WithMaxTokensPerSegment(96);
        }

        if (audioContextSize is int ctx)
            builder.WithAudioContextSize(ctx);

        if (normalizedLanguage == "auto")
            builder.WithLanguageDetection();
        else
            builder.WithLanguage(normalizedLanguage);

        return builder.Build();
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _serial.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_streamingShortProcessor != null) await _streamingShortProcessor.DisposeAsync();
            if (_streamingMediumProcessor != null) await _streamingMediumProcessor.DisposeAsync();
            if (_processor != null) await _processor.DisposeAsync();
            _factory?.Dispose();
            _vad?.Dispose();
            _vadFactory?.Dispose();
        }
        finally { _serial.Release(); }
    }
}
