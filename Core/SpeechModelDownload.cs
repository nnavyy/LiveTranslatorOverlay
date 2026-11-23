using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace LiveTranslatorOverlay.Core;

public enum LocalSpeechModelTier
{
    Base,
    Small,
    LargeTurbo,
    Auto
}

public static class SpeechModelDownload
{
    private const string WhisperDownloadRoot = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    private const string VadDownloadRoot = "https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/";

    private const string BaseName = "ggml-base.bin";
    private const string BaseHash = "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe";
    private const long BaseSize = 147951465;

    private const string SmallName = "ggml-small.bin";
    private const string SmallHash = "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b";
    private const long SmallSize = 487601967;

    private const string LargeTurboName = "ggml-large-v3-turbo-q5_0.bin";
    private const string LargeTurboHash = "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2";
    private const long LargeTurboSize = 574041195;

    private const string VadName = "ggml-silero-v6.2.0.bin";
    private const string VadHash = "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987";
    private const long VadSize = 885098;

    private static readonly SemaphoreSlim DownloadLock = new(1, 1);
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static bool HasGpuAccelerationAvailable()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("LIVETRANSLATOR_DISABLE_GPU"), "1", StringComparison.Ordinal))
            return false;

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 &&
                   File.Exists(Path.Combine(AppContext.BaseDirectory, "ggml-metal.metal"));
        }

        if (!OperatingSystem.IsWindows())
            return false;

        string vulkanDll = Path.Combine(AppContext.BaseDirectory, "runtimes", "vulkan", "win-x64", "ggml-vulkan-whisper.dll");
        if (!File.Exists(vulkanDll) && !File.Exists(Path.Combine(AppContext.BaseDirectory, "ggml-vulkan-whisper.dll")))
            return false;

        if (!NativeLibrary.TryLoad("vulkan-1.dll", out nint handle))
            return false;

        NativeLibrary.Free(handle);
        return true;
    }

    public static LocalSpeechModelTier ResolveEffectiveTier(LocalSpeechModelTier requested, bool gpuAvailable) =>
        requested == LocalSpeechModelTier.Auto
            ? (gpuAvailable ? LocalSpeechModelTier.LargeTurbo : LocalSpeechModelTier.Base)
            : requested;

    public static string GetModelDisplayName(LocalSpeechModelTier tier) => tier switch
    {
        LocalSpeechModelTier.LargeTurbo => "Whisper large-v3-turbo (Q5_0)",
        LocalSpeechModelTier.Small => "Whisper small",
        LocalSpeechModelTier.Base => "Whisper base",
        _ => "Whisper auto"
    };

    public static Task<string> EnsureAsync(bool small, Action<string>? progress, CancellationToken token) =>
        EnsureTierAsync(small ? LocalSpeechModelTier.Small : LocalSpeechModelTier.Base, progress, token);

    public static Task<string> EnsureTierAsync(LocalSpeechModelTier tier, Action<string>? progress, CancellationToken token)
    {
        var effective = ResolveEffectiveTier(tier, HasGpuAccelerationAvailable());
        return effective switch
        {
            LocalSpeechModelTier.LargeTurbo => EnsureFileAsync(LargeTurboName, LargeTurboHash, LargeTurboSize, WhisperDownloadRoot, progress, token),
            LocalSpeechModelTier.Small => EnsureFileAsync(SmallName, SmallHash, SmallSize, WhisperDownloadRoot, progress, token),
            _ => EnsureFileAsync(BaseName, BaseHash, BaseSize, WhisperDownloadRoot, progress, token)
        };
    }

    public static Task<string> EnsureVadAsync(Action<string>? progress, CancellationToken token) =>
        EnsureFileAsync(VadName, VadHash, VadSize, VadDownloadRoot, progress, token);

    public static async Task<bool> HasDefaultModelsAsync(CancellationToken token)
    {
        string directory = Path.Combine(AppSettings.DataDirectory, "models");
        string speech = Path.Combine(directory, BaseName);
        string detector = Path.Combine(directory, VadName);
        if (!File.Exists(speech) || !File.Exists(detector)) return false;
        try
        {
            return await IsValidAsync(detector, VadSize, VadHash, token)
                && await IsValidAsync(speech, BaseSize, BaseHash, token);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static async Task<string> EnsureFileAsync(string name, string hash, long size, string baseUrl,
        Action<string>? progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        token = timeout.Token;
        await DownloadLock.WaitAsync(token);
        string? temporary = null;
        try
        {
            string directory = Path.Combine(AppSettings.DataDirectory, "models");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name);
            progress?.Invoke("Checking speech model...");
            if (File.Exists(path) && await IsValidAsync(path, size, hash, token)) return path;

            progress?.Invoke($"Downloading speech model (0 / {Math.Ceiling(size / 1_000_000d)} MB). First use only; Stop cancels.");
            using var response = await Client.GetAsync(baseUrl + name,
                HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                var report = Stopwatch.StartNew();
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    total += count;
                    if (total > size) throw new InvalidDataException("Speech model download has an unexpected size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    if (report.ElapsedMilliseconds >= 500)
                    {
                        progress?.Invoke($"Downloading speech model ({total / 1_000_000} / {Math.Ceiling(size / 1_000_000d)} MB). Stop cancels.");
                        report.Restart();
                    }
                }
            }
            progress?.Invoke("Verifying speech model...");
            if (!await IsValidAsync(temporary, size, hash, token))
                throw new InvalidDataException("Speech model download is incomplete or damaged. Start audio again to retry.");
            File.Move(temporary, path, overwrite: true);
            temporary = null;
            return path;
        }
        finally
        {
            try { if (temporary != null) File.Delete(temporary); }
            finally { DownloadLock.Release(); }
        }
    }

    private static async Task<bool> IsValidAsync(string path, long size, string hash, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return file.Length == size && Convert.ToHexString(await SHA256.HashDataAsync(file, token))
            .Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}
