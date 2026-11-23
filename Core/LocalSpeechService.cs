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
    private WhisperProcessor? _streamingProcessor;
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
                    try
                    {
                        _factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = true });
                        loadedWithGpu = OperatingSystem.IsMacOS() ||
                                        RuntimeOptions.LoadedLibrary == RuntimeLibrary.Vulkan;
                    }
                    catch
                    {
                        _factory?.Dispose();
                        _factory = null;
                        loadedWithGpu = false;
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
                _streamingProcessor = BuildProcessor(_factory, normalized, singleSegment: true, audioContextSize: 512);

                _vadFactory = WhisperVadFactory.FromPath(vadPath, new WhisperFactoryOptions { UseGpu = false });
                _vad = _vadFactory.CreateBuilder().WithUseGpu(false).WithThreads(2).WithThreshold(.5f).Build();
            }, token);

            if (UsingGpu && _streamingProcessor != null)
            {
                progress?.Invoke("Warming up GPU shaders...");
                await Task.Run(async () =>
                {
                    await foreach (var _ in _streamingProcessor.ProcessAsync(new float[8000], token)) { }
                }, token);
            }

            token.ThrowIfCancellationRequested();
            Language = language;
        }
        finally { _serial.Release(); }
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
            var processor = streamingWindow && samples.Length <= 160000 ? _streamingProcessor : _processor;
            if (processor == null) throw new InvalidOperationException("Local speech is not initialized.");

            if (!HasNonZeroSignal(samples)) return "";
            if ((await _vad!.DetectSpeechAsync(samples, timeout.Token)).Count == 0) return "";

            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(samples, timeout.Token))
            {
                string line = segment.Text.Trim();
                if (HallucinationDetector.IsWhisperArtifact(line)) continue;
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
            .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 6))
            .WithNoContext()
            .WithTemperature(0)
            .WithNoSpeechThreshold(.6f);

        if (singleSegment)
            builder.WithSingleSegment();

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
            if (_streamingProcessor != null) await _streamingProcessor.DisposeAsync();
            if (_processor != null) await _processor.DisposeAsync();
            _factory?.Dispose();
            _vad?.Dispose();
            _vadFactory?.Dispose();
        }
        finally { _serial.Release(); }
    }
}
