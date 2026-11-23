using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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

                    // Recheck the stable feed every 15 minutes.
                    await Task.Delay(TimeSpan.FromMinutes(15), token).ConfigureAwait(false);
                }
            }, token);
        }

        public async Task<bool> CheckAndDownloadAsync(bool silent = true)
        {
            if (!await _updateLock.WaitAsync(100))
                return false;

            string? zipFilePath = null;
            try
            {
                if (IsUpdateReady) return true;
                StatusChanged?.Invoke(this, new UpdateStatusEventArgs("Checking for updates...", 0));

                var (channel, manifestUri) = ResolveChannelAndManifest();
                string manifestJson;
                if (channel == "beta" && manifestUri == null)
                {
                    var communityClient = new CommunityClient(AppSettings.DataDirectory, _httpClient);
                    var (allowed, gatedJson, message) = await communityClient.FetchGatedBetaManifestAsync("win-x64", CancellationToken.None).ConfigureAwait(false);
                    if (!allowed || string.IsNullOrWhiteSpace(gatedJson))
                    {
                        bool isErr = message.Contains("HTTP", StringComparison.Ordinal) || message.Contains("reach", StringComparison.OrdinalIgnoreCase);
                        StatusChanged?.Invoke(this, new UpdateStatusEventArgs(message, isErr ? 0 : 100, isError: isErr));
                        return false;
                    }
                    manifestJson = gatedJson;
                }
                else if (manifestUri == null)
                {
                    StatusChanged?.Invoke(this, new UpdateStatusEventArgs($"No {channel} feed configured.", 100));
                    return false;
                }
                else if (manifestUri.IsFile)
                {
                    if (!File.Exists(manifestUri.LocalPath))
                    {
                        StatusChanged?.Invoke(this, new UpdateStatusEventArgs($"No local {channel} feed manifest found yet.", 100));
                        return false;
                    }
                    manifestJson = await File.ReadAllTextAsync(manifestUri.LocalPath);
                }
                else
                {
                    using var response = await _httpClient.GetAsync(manifestUri);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        string notFoundMsg = channel == "stable" ? "No public 1.x release yet." : $"No {channel} 1.x release yet.";
                        StatusChanged?.Invoke(this, new UpdateStatusEventArgs(notFoundMsg, 100));
                        return false;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        string msg = $"Server check returned {response.StatusCode}";
                        StatusChanged?.Invoke(this, new UpdateStatusEventArgs(msg, 0, isError: true));
                        return false;
                    }

                    manifestJson = await response.Content.ReadAsStringAsync();
                }

                var release = StableRelease.Read(manifestJson, "win-x64", channel);
                if (release == null || release.Version <= AppVersion.Current)
                {
                    StatusChanged?.Invoke(this, new UpdateStatusEventArgs($"Up to date ({AppVersion.DisplayString} · {channel})", 100));
                    return false;
                }

                // Setup version-isolated local temp directories to prevent file locking collisions
                string tagName = $"v{release.Version}";
                string localAppData = AppSettings.DataDirectory;
                string updateTempDir = Path.Combine(localAppData, "UpdateTemp");
                string updateStagedDir = Path.Combine(localAppData, $"Staged_ml_{channel}_{tagName}");

                Directory.CreateDirectory(updateTempDir);
                if (Directory.Exists(updateStagedDir))
                {
                    try { Directory.Delete(updateStagedDir, true); } catch { }
                }
                Directory.CreateDirectory(updateStagedDir);

                zipFilePath = Path.Combine(updateTempDir, $"update_ml_{channel}_{tagName}.zip");

                // Download ZIP with progress (or copy from local dev feed)
                StatusChanged?.Invoke(this, new UpdateStatusEventArgs($"Downloading {channel} update {tagName}...", 5, newVersion: tagName));

                using var downloadTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                var token = downloadTimeout.Token;
                if (release.DownloadUri.IsFile)
                {
                    await using var sourceStream = File.OpenRead(release.DownloadUri.LocalPath);
                    await using var destStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 81920, true);
                    await sourceStream.CopyToAsync(destStream, token);
                }
                else
                {
                    using (var downloadResponse = await _httpClient.GetAsync(release.DownloadUri, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        downloadResponse.EnsureSuccessStatusCode();
                        long totalBytes = release.Size;
                        if (downloadResponse.Content.Headers.ContentLength is long size && size != totalBytes)
                            throw new InvalidDataException("The update response does not match the published size.");

                        using (var contentStream = await downloadResponse.Content.ReadAsStreamAsync(token))
                        using (var fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 81920, true))
                        {
                            var buffer = new byte[81920];
                            long totalRead = 0;
                            int bytesRead;
                            int lastReportedPercent = 5;

                            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(), token)) > 0)
                            {
                                totalRead += bytesRead;
                                if (totalRead > totalBytes) throw new InvalidDataException("The update download exceeded the published size.");
                                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token);

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
                }

                await release.VerifyAsync(zipFilePath, token);
                // Extract Zip
                StatusChanged?.Invoke(this, new UpdateStatusEventArgs("Extracting update files...", 92, newVersion: tagName));
                ZipFile.ExtractToDirectory(zipFilePath, updateStagedDir, overwriteFiles: true);

                if (!File.Exists(Path.Combine(updateStagedDir, "LiveTranslatorOverlay.exe")))
                    throw new InvalidDataException("Update package does not contain the application.");

                // CRITICAL SAFETY: Delete appsettings.json from staged directory to preserve user settings!
                string stagedAppSettings = Path.Combine(updateStagedDir, "appsettings.json");
                if (File.Exists(stagedAppSettings))
                {
                    try { File.Delete(stagedAppSettings); } catch { }
                }

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
                if (zipFilePath != null)
                {
                    try { File.Delete(zipFilePath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
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
set ""DEVELOPER_ARG=""
if ""%~5""==""1"" set ""DEVELOPER_ARG=--developer""

echo [%date% %time%] Updater started. Target=%TARGET_DIR% OldPID=%OLD_PID% > ""%~dp0updater.log""

:: Wait for this app instance to exit; never terminate unrelated processes.
set WAIT_COUNT=0
:wait_for_exit
tasklist /FI ""PID eq %OLD_PID%"" /NH | findstr /R /C:"" %OLD_PID% "" >nul
if errorlevel 1 goto copy_files
set /a WAIT_COUNT+=1
if %WAIT_COUNT% GEQ 30 exit /b 1
timeout /t 1 /nobreak >nul
goto wait_for_exit
:copy_files

echo [%date% %time%] Starting file copy from %SOURCE_DIR% to %TARGET_DIR%... >> ""%~dp0updater.log""

:: Robocopy everything except user config with fast retry flags
robocopy ""%SOURCE_DIR%"" ""%TARGET_DIR%"" /E /IS /IT /XF appsettings.json /R:2 /W:1 >> ""%~dp0updater.log"" 2>&1
set ROBO_ERR=%ERRORLEVEL%
if %ROBO_ERR% GEQ 8 exit /b %ROBO_ERR%
echo [%date% %time%] Robocopy finished with exit code %ROBO_ERR% >> ""%~dp0updater.log""

:: Clean up staging folder
timeout /t 1 /nobreak >nul
:: Keep staged files for recovery; clean them on the next download.

:: Relaunch updated application exactly once
echo [%date% %time%] Launching updated %TARGET_DIR%\%TARGET_EXE% >> ""%~dp0updater.log""
cd /d ""%TARGET_DIR%""
start """" ""%TARGET_DIR%\%TARGET_EXE%"" %DEVELOPER_ARG%
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
                string localAppData = AppSettings.DataDirectory;
                CreateUpdateScript(localAppData); // Always write fresh update script
                string scriptPath = Path.Combine(localAppData, "apply_update.cmd");

                string currentDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string exeName = Process.GetCurrentProcess().MainModule?.ModuleName ?? "LiveTranslatorOverlay.exe";
                int currentPid = Process.GetCurrentProcess().Id;
                int developer = App.IsDeveloperMode ? 1 : 0;

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"\"{scriptPath}\" \"{currentDir}\" \"{StagedDirectory}\" \"{exeName}\" {currentPid} {developer}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                Process.Start(psi);

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _isApplyingUpdate = false;
                MessageBox.Show($"Failed to apply update: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static (string Channel, Uri? ManifestUri) ResolveChannelAndManifest()
        {
            string localAppData = AppSettings.DataDirectory;
            string overridePath = Path.Combine(localAppData, "channel.json");
            if (File.Exists(overridePath))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(overridePath));
                    string channel = doc.RootElement.TryGetProperty("channel", out var chEl)
                        ? (chEl.GetString() ?? "stable").Trim().ToLowerInvariant()
                        : "stable";
                    if (doc.RootElement.TryGetProperty("manifestUrl", out var urlEl) &&
                        Uri.TryCreate(urlEl.GetString(), UriKind.Absolute, out var customUri) &&
                        (customUri.IsFile || customUri.Scheme == Uri.UriSchemeHttps || (customUri.Scheme == Uri.UriSchemeHttp && customUri.IsLoopback)))
                    {
                        return (channel, customUri);
                    }
                    string fallbackUrl = StableRelease.GetManifestUrl(channel);
                    return (channel, string.IsNullOrWhiteSpace(fallbackUrl) ? null : new Uri(fallbackUrl));
                }
                catch { }
            }

            string settingsChannel = (AppSettings.Load().UpdateChannel ?? "stable").Trim().ToLowerInvariant();
            if (settingsChannel == "dev")
            {
                string defaultDevManifest = Path.Combine(localAppData, "DevFeed", "ml-dev.json");
                return ("dev", new Uri(defaultDevManifest));
            }
            if (settingsChannel == "beta")
                return ("beta", null);
            return ("stable", new Uri(StableRelease.ManifestUrl));
        }
    }
}
