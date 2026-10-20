using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace LiveTranslatorOverlay.Core.Update
{
    public class UpdateStatusEventArgs : EventArgs
    {
        public string StatusMessage { get; }
        public int ProgressPercent { get; }
        public bool IsError { get; }
        public bool IsReadyToInstall { get; }
        public string NewVersion { get; }

        public UpdateStatusEventArgs(string message, int progress = 0, bool isError = false, bool isReady = false, string newVersion = "")
        {
            StatusMessage = message;
            ProgressPercent = progress;
            IsError = isError;
            IsReadyToInstall = isReady;
            NewVersion = newVersion;
        }
    }

    public class AutoUpdaterService
    {
        private static readonly Lazy<AutoUpdaterService> _instance = new(() => new AutoUpdaterService());
        public static AutoUpdaterService Instance => _instance.Value;

        private const string GitHubOwner = "nnavyy";
        private const string GitHubRepo = "LiveTranslatorOverlay";
        private const string GitHubApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _updateLock = new(1, 1);

        public event EventHandler<UpdateStatusEventArgs>? StatusChanged;
        public event EventHandler<string>? UpdateReadyToInstall;

        public bool IsUpdateReady { get; private set; } = false;
        public string PendingVersion { get; private set; } = "";
        public string StagedDirectory { get; private set; } = "";

        public AutoUpdaterService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LiveTranslatorOverlay-Updater", AppVersion.DisplayString));
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        }

        private CancellationTokenSource? _periodicCheckCts;

        public async Task StartBackgroundCheckAsync(bool silent = true)
        {
            if (!silent)
            {
                await CheckAndDownloadAsync(silent: false);
                return;
            }

            _periodicCheckCts?.Cancel();
            _periodicCheckCts = new CancellationTokenSource();
            var token = _periodicCheckCts.Token;

            _ = Task.Run(async () =>
            {
                // Small initial delay on app start so UI loads instantly
                await Task.Delay(3000, token).ConfigureAwait(false);
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (IsUpdateReady) break;
                        await CheckAndDownloadAsync(silent: true).ConfigureAwait(false);
                    }
                    catch { }

                    // Periodically poll GitHub releases every 15 minutes (protects against GitHub API 60 req/hr rate limit)
                    await Task.Delay(TimeSpan.FromMinutes(15), token).ConfigureAwait(false);
                }
            }, token);
        }

        public async Task<bool> CheckAndDownloadAsync(bool silent = true)
        {
            if (!await _updateLock.WaitAsync(100))
                return false;

            try
            {
                StatusChanged?.Invoke(this, new UpdateStatusEventArgs("Checking for updates...", 0));

                using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    string msg = $"Server check returned {response.StatusCode}";
                    StatusChanged?.Invoke(this, new UpdateStatusEventArgs(msg, 0, isError: true));
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(tagName) || !AppVersion.IsNewerThanCurrent(tagName))
                {
                    StatusChanged?.Invoke(this, new UpdateStatusEventArgs($"Up to date ({AppVersion.DisplayString})", 100));
                    return false;
                }

                // Locate zip asset
                string downloadUrl = "";
                long expectedSize = 0;
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
                            expectedSize = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
                            break;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    StatusChanged?.Invoke(this, new UpdateStatusEventArgs($"Update {tagName} found, but zip asset is not ready yet.", 0, isError: true));
                    return false;
                }

                // Pre-clean: terminate any lingering robocopy from older update attempts
                try
                {
                    foreach (var proc in Process.GetProcessesByName("robocopy"))
                    {
                        try { proc.Kill(); } catch { }
                    }
                }
                catch { }

                // Setup version-isolated local temp directories to prevent file locking collisions
                string cleanTag = tagName.Replace("/", "_").Replace("\\", "_");
                string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveTranslatorOverlay");
                string updateTempDir = Path.Combine(localAppData, "UpdateTemp");
                string updateStagedDir = Path.Combine(localAppData, $"Staged_{cleanTag}");

                Directory.CreateDirectory(updateTempDir);
                if (Directory.Exists(updateStagedDir))
                {
                    try { Directory.Delete(updateStagedDir, true); } catch { }
                }
                Directory.CreateDirectory(updateStagedDir);

                string zipFilePath = Path.Combine(updateTempDir, $"update_{cleanTag}.zip");
                if (File.Exists(zipFilePath))
                {
                    try { File.Delete(zipFilePath); } catch { }
                }

                // Download ZIP with progress
                StatusChanged?.Invoke(this, new UpdateStatusEventArgs($"Downloading update {tagName}...", 5, newVersion: tagName));

                using (var downloadResponse = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    downloadResponse.EnsureSuccessStatusCode();
                    long totalBytes = downloadResponse.Content.Headers.ContentLength ?? expectedSize;

                    using (var contentStream = await downloadResponse.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 81920, true))
                    {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;
                        int lastReportedPercent = 5;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                int percent = (int)((totalRead * 85) / totalBytes) + 5; // Scale 5% - 90%
                                if (percent >= lastReportedPercent + 5)
                                {
                                    lastReportedPercent = percent;
                                    StatusChanged?.Invoke(this, new UpdateStatusEventArgs($"Downloading update {tagName} ({percent}%)...", percent, newVersion: tagName));
                                }
                            }
                        }
                    }
                }

                // Extract Zip
                StatusChanged?.Invoke(this, new UpdateStatusEventArgs("Extracting update files...", 92, newVersion: tagName));
                ZipFile.ExtractToDirectory(zipFilePath, updateStagedDir, overwriteFiles: true);

                // CRITICAL SAFETY: Delete appsettings.json from staged directory to preserve user settings!
                string stagedAppSettings = Path.Combine(updateStagedDir, "appsettings.json");
                if (File.Exists(stagedAppSettings))
                {
                    try { File.Delete(stagedAppSettings); } catch { }
                }

                // Clean up zip
                try { File.Delete(zipFilePath); } catch { }

                // Create helper script
                CreateUpdateScript(localAppData);

                IsUpdateReady = true;
                PendingVersion = tagName;
                StagedDirectory = updateStagedDir;

                StatusChanged?.Invoke(this, new UpdateStatusEventArgs($"Update {tagName} ready! Restart to apply.", 100, isReady: true, newVersion: tagName));
                UpdateReadyToInstall?.Invoke(this, tagName);

                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, new UpdateStatusEventArgs($"Update failed: {ex.Message}", 0, isError: true));
                return false;
            }
            finally
            {
                _updateLock.Release();
            }
        }

        private void CreateUpdateScript(string localAppDataDir)
        {
            Directory.CreateDirectory(localAppDataDir);
            string scriptPath = Path.Combine(localAppDataDir, "apply_update.cmd");
            string scriptContent = @"@echo off
chcp 65001 >nul
set ""TARGET_DIR=%~1""
set ""SOURCE_DIR=%~2""
set ""TARGET_EXE=%~3""
set ""OLD_PID=%~4""

echo [%date% %time%] Updater started. Target=%TARGET_DIR% OldPID=%OLD_PID% > ""%~dp0updater.log""

:: Kill any hanging robocopy processes
taskkill /f /im robocopy.exe >nul 2>&1

:: Kill old process by PID if still alive
if not ""%OLD_PID%""=="""" (
    taskkill /f /pid %OLD_PID% >nul 2>&1
)

:: Terminate any duplicate instances to guarantee clean single-instance restart
taskkill /f /im ""%TARGET_EXE%"" >nul 2>&1
timeout /t 2 /nobreak >nul

echo [%date% %time%] Starting file copy from %SOURCE_DIR% to %TARGET_DIR%... >> ""%~dp0updater.log""

:: Robocopy everything except user config with fast retry flags
robocopy ""%SOURCE_DIR%"" ""%TARGET_DIR%"" /E /IS /IT /XF appsettings.json /R:2 /W:1 >> ""%~dp0updater.log"" 2>&1
set ROBO_ERR=%ERRORLEVEL%
echo [%date% %time%] Robocopy finished with exit code %ROBO_ERR% >> ""%~dp0updater.log""

:: Clean up staging folder
timeout /t 1 /nobreak >nul
rmdir /s /q ""%SOURCE_DIR%"" >nul 2>&1

:: Relaunch updated application exactly once
echo [%date% %time%] Launching updated %TARGET_DIR%\%TARGET_EXE% >> ""%~dp0updater.log""
cd /d ""%TARGET_DIR%""
start """" ""%TARGET_DIR%\%TARGET_EXE%""
exit /b 0
";
            File.WriteAllText(scriptPath, scriptContent);
        }

        private bool _isApplyingUpdate = false;

        public void ApplyUpdateAndRestart()
        {
            if (_isApplyingUpdate) return;
            _isApplyingUpdate = true;

            if (!IsUpdateReady || string.IsNullOrWhiteSpace(StagedDirectory) || !Directory.Exists(StagedDirectory))
            {
                _isApplyingUpdate = false;
                return;
            }

            IsUpdateReady = false;

            try
            {
                string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveTranslatorOverlay");
                CreateUpdateScript(localAppData); // Always write fresh update script
                string scriptPath = Path.Combine(localAppData, "apply_update.cmd");

                string currentDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string exeName = Process.GetCurrentProcess().MainModule?.ModuleName ?? "LiveTranslatorOverlay.exe";
                int currentPid = Process.GetCurrentProcess().Id;

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{scriptPath}\" \"{currentDir}\" \"{StagedDirectory}\" \"{exeName}\" {currentPid}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(psi);

                // Instantly kill process at kernel level to immediately unlock all files
                try
                {
                    Process.GetCurrentProcess().Kill();
                }
                catch
                {
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                _isApplyingUpdate = false;
                MessageBox.Show($"Failed to apply update: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
