using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace LiveTranslatorOverlay.Core;

/// <summary>Session-bound local recognizer with the benchmark's fixed Silero gate.</summary>
public sealed class PreparedSpeechService : IAsyncDisposable
{
    private Process? _worker;
    private WhisperVadFactory? _factory;
    private WhisperVadProcessor? _vad;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private bool _disposed;
    private readonly CancellationTokenSource _lifetime = new();
    public string Language { get; private set; } = "";

    public async Task InitializeAsync(string language, CancellationToken cancellationToken)
    {
        await _serial.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await InitializeCoreAsync(language, cancellationToken);
        }
        finally { _serial.Release(); }
    }

    private async Task InitializeCoreAsync(string language, CancellationToken cancellationToken)
    {
        if (language is not ("en" or "ru"))
            throw new InvalidOperationException("Local speech: select English or Russian as source, then restart audio.");
        string configuration = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveTranslatorOverlay", "local-speech.json");
        if (!File.Exists(configuration))
            throw new InvalidOperationException("Local speech is not configured. Run research/speech/enable-local-speech.ps1 from your prepared workspace first.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(configuration, cancellationToken));
        var config = document.RootElement;
        string vadPath = config.GetProperty("vadModel").GetString()!;
        await using (var file = File.OpenRead(vadPath))
            if (Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant() !=
                "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987")
                throw new InvalidDataException("Silero model checksum mismatch.");
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Cpu];
        _factory = WhisperVadFactory.FromPath(vadPath, new WhisperFactoryOptions { UseGpu = false });
        _vad = _factory.CreateBuilder().WithUseGpu(false).WithThreads(2).WithThreshold(.5f).Build();
        var start = new ProcessStartInfo(config.GetProperty("python").GetString()!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false)
        };
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "runtime", "speech_worker.py"));
        start.ArgumentList.Add(configuration);
        start.ArgumentList.Add(language);
        _worker = Process.Start(start) ?? throw new InvalidOperationException("Could not start local speech.");
        // Drain diagnostics without retaining audio or an unbounded log.
        _worker.ErrorDataReceived += (_, _) => { };
        _worker.BeginErrorReadLine();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var response = JsonDocument.Parse(await ReadResponseAsync(timeout.Token));
        if (!response.RootElement.TryGetProperty("ready", out var ready) || !ready.GetBoolean())
            throw new InvalidOperationException(response.RootElement.GetProperty("error").GetString());
        Language = language;
    }

    private async Task<string> ReadResponseAsync(CancellationToken token) =>
        await _worker!.StandardOutput.ReadLineAsync(token) ?? throw new IOException("Local speech worker exited. Stop and restart audio.");

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken)
    {
        if (samples.Length is <= 0 or > 320000 || Array.Exists(samples, v => !float.IsFinite(v)))
            throw new ArgumentException("Expected finite mono 16 kHz audio up to 20 seconds.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await _serial.WaitAsync(timeout.Token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_vad == null || _worker == null) throw new InvalidOperationException("Local speech is not initialized.");
            if ((await _vad.DetectSpeechAsync(samples, timeout.Token)).Count == 0) return "";
            await _worker.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { samples }).AsMemory(), timeout.Token);
            using var response = JsonDocument.Parse(await ReadResponseAsync(timeout.Token));
            if (response.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.GetString());
            return response.RootElement.GetProperty("text").GetString() ?? "";
        }
        catch
        {
            // A canceled request cannot leave a stale reply for the next clip.
            _lifetime.Cancel();
            KillWorker();
            throw;
        }
        finally { _serial.Release(); }
    }

    private void KillWorker()
    {
        try { if (_worker is { HasExited: false }) _worker.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        KillWorker();
        await _serial.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            KillWorker();
            _worker?.Dispose();
            _vad?.Dispose();
            _factory?.Dispose();
        }
        finally { _serial.Release(); }
    }
}
