using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Diagnostics;

namespace LiveTranslatorOverlay.Core;

public static class SpeechModelDownload
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly SemaphoreSlim DownloadLock = new(1, 1);

    public static Task<string> EnsureAsync(bool small, Action<string>? progress, CancellationToken token) =>
        EnsureFileAsync(small ? "ggml-small.bin" : "ggml-base.bin",
            small ? "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b"
                : "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe",
            small ? 487601967 : 147951465,
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/", progress, token);

    public static Task<string> EnsureVadAsync(Action<string>? progress, CancellationToken token) =>
        EnsureFileAsync("ggml-silero-v6.2.0.bin",
            "2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987", 885098,
            "https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/", progress, token);

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
