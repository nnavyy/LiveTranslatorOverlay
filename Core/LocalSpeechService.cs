using System.Text;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LiveTranslatorOverlay.Core;

/// <summary>Multilingual recognition on the CPU, with an automatically installed model.</summary>
public sealed class LocalSpeechService(bool small = false) : IAsyncDisposable
{
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private WhisperVadFactory? _vadFactory;
    private WhisperVadProcessor? _vad;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    public string Language { get; private set; } = "";

    public async Task InitializeAsync(string language, CancellationToken cancellationToken, Action<string>? progress = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var token = linked.Token;
        await _serial.WaitAsync(token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_processor != null) throw new InvalidOperationException("Speech is already initialized.");
            // CPU-only, including a fallback for older processors without AVX instructions.
            RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
            string normalized = language == "auto" ? "auto" : language.Split('-')[0].ToLowerInvariant();
            if (normalized != "auto" && !WhisperFactory.GetSupportedLanguages().Contains(normalized))
                throw new InvalidOperationException("This speech language is not supported. Choose a supported language or Auto-detect.");
            string path = await SpeechModelDownload.EnsureAsync(small, progress, token);
            string vadPath = await SpeechModelDownload.EnsureVadAsync(progress, token);
            progress?.Invoke("Loading local speech on CPU...");
            await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                _factory = WhisperFactory.FromPath(path, new WhisperFactoryOptions { UseGpu = false });
                var builder = _factory.CreateBuilder().WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 6))
                    .WithNoContext().WithTemperature(0).WithNoSpeechThreshold(.6f);
                if (normalized == "auto") builder.WithLanguageDetection();
                else builder.WithLanguage(normalized);
                _processor = builder.Build();
                _vadFactory = WhisperVadFactory.FromPath(vadPath, new WhisperFactoryOptions { UseGpu = false });
                _vad = _vadFactory.CreateBuilder().WithUseGpu(false).WithThreads(2).WithThreshold(.5f).Build();
            }, token);
            token.ThrowIfCancellationRequested();
            Language = language;
        }
        finally { _serial.Release(); }
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken)
    {
        if (samples.Length is <= 0 or > 320000 || Array.Exists(samples, value => !float.IsFinite(value)))
            throw new ArgumentException("Expected finite mono 16 kHz audio up to 20 seconds.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await _serial.WaitAsync(timeout.Token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_processor == null) throw new InvalidOperationException("Local speech is not initialized.");
            if (!WhisperSttService.HasSustainedVoiceActivity(samples)) return "";
            if ((await _vad!.DetectSpeechAsync(samples, timeout.Token)).Count == 0) return "";
            var text = new StringBuilder();
            await foreach (var segment in _processor.ProcessAsync(samples, timeout.Token))
            {
                if (text.Length > 0) text.Append(' ');
                text.Append(segment.Text.Trim());
            }
            string result = text.ToString();
            return HallucinationDetector.IsHallucination(result) ? "" : result;
        }
        finally { _serial.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _serial.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_processor != null) await _processor.DisposeAsync();
            _factory?.Dispose();
            _vad?.Dispose();
            _vadFactory?.Dispose();
        }
        finally { _serial.Release(); }
    }
}
