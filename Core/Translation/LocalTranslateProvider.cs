using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace LiveTranslatorOverlay.Core.Translation;

/// <summary>Offline CPU translation; releases model RAM after each request.</summary>
public sealed class LocalTranslateProvider : ITranslationProvider
{
    private static readonly SemaphoreSlim Serial = new(1, 1);

    public async Task<string> TranslateAsync(string text, string targetLanguage = "id", string sourceLanguage = "auto")
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (sourceLanguage == "auto" || targetLanguage == "auto")
            throw new InvalidOperationException("Local translation needs explicit source and target languages.");
        if (sourceLanguage == targetLanguage) return text;
        if (text.Length > 4000) throw new ArgumentException("Split text into passages of at most 4000 characters.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await Serial.WaitAsync(timeout.Token);
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveTranslatorOverlay", "local-speech.json");
            if (!File.Exists(path)) throw new InvalidOperationException("Run enable-local-speech.ps1 from your prepared workspace first.");
            using var configuration = JsonDocument.Parse(await File.ReadAllTextAsync(path, timeout.Token));
            if (!configuration.RootElement.TryGetProperty("translationPython", out var python))
                throw new InvalidOperationException("Run the updated enable-local-speech.ps1 to configure local translation.");
            var start = new ProcessStartInfo(python.GetString()!)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            start.Environment["PYTHONIOENCODING"] = "utf-8";
            start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "runtime", "translation_worker.py"));
            start.ArgumentList.Add(path);
            using var process = Process.Start(start) ?? throw new IOException("Could not start local translation.");
            try
            {
                var diagnostics = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { text, source = sourceLanguage, target = targetLanguage }).AsMemory(), timeout.Token);
                process.StandardInput.Close();
                string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                await diagnostics;
                if (string.IsNullOrWhiteSpace(output)) throw new IOException("Local translation failed to start; check the configured environment.");
                using var response = JsonDocument.Parse(output);
                if (response.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
                if (process.ExitCode != 0) throw new IOException("Local translation failed.");
                return response.RootElement.GetProperty("text").GetString() ?? "";
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
        finally { Serial.Release(); }
    }

    public async Task TranslateStreamAsync(string text, string targetLanguage, string sourceLanguage, Action<string> onTokenReceived) =>
        onTokenReceived(await TranslateAsync(text, targetLanguage, sourceLanguage));
}
