using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace LiveTranslatorOverlay.Core.Translation;

/// <summary>Offline local M2M-100 translation using a warm persistent CTranslate2 worker.</summary>
public sealed class LocalTranslateProvider : ITranslationProvider, IDisposable
{
    private readonly SemaphoreSlim _serial = new(1, 1);
    private Process? _daemonProcess;
    private IntPtr _jobHandle;
    private string? _daemonConfigPath;
    private DateTime _daemonConfigStamp;
    private bool _disposed;
    private bool _exitHookRegistered;

    public async Task WarmUpAsync(CancellationToken token = default)
    {
        string configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiveTranslatorOverlay",
            "local-speech.json");
        if (!File.Exists(configPath)) return;

        await _serial.WaitAsync(token);
        try
        {
            if (_disposed) return;
            await EnsureDaemonStartedAsync(configPath, File.GetLastWriteTimeUtc(configPath), token);
        }
        catch { }
        finally { _serial.Release(); }
    }

    public Task<string> TranslateAsync(string text, string targetLanguage = "id", string sourceLanguage = "auto") =>
        TranslateAsync(text, targetLanguage, sourceLanguage, fastInterim: false);

    public async Task<string> TranslateAsync(string text, string targetLanguage, string sourceLanguage, bool fastInterim)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (sourceLanguage == "auto" || targetLanguage == "auto")
            throw new InvalidOperationException("Local translation needs explicit source and target languages.");
        if (sourceLanguage == targetLanguage) return text;
        if (text.Length > 4000) throw new ArgumentException("Split text into passages of at most 4000 characters.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _serial.WaitAsync(timeout.Token);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiveTranslatorOverlay",
                "local-speech.json");
            if (!File.Exists(configPath))
                throw new InvalidOperationException("Run enable-local-speech.ps1 from your prepared workspace first.");

            DateTime stamp = File.GetLastWriteTimeUtc(configPath);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await EnsureDaemonStartedAsync(configPath, stamp, timeout.Token);
                var process = _daemonProcess!;
                string payload = JsonSerializer.Serialize(new { text, source = sourceLanguage, target = targetLanguage, fast = fastInterim });
                await process.StandardInput.WriteLineAsync(payload.AsMemory(), timeout.Token);
                await process.StandardInput.FlushAsync(timeout.Token);

                string? responseLine = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (string.IsNullOrWhiteSpace(responseLine))
                {
                    StopDaemonLocked();
                    if (attempt == 0) continue;
                    throw new IOException("Local translation failed to start; check the configured environment.");
                }

                using var response = JsonDocument.Parse(responseLine);
                if (response.RootElement.TryGetProperty("error", out var error))
                    throw new InvalidOperationException(error.GetString());
                return response.RootElement.GetProperty("text").GetString() ?? "";
            }

            throw new IOException("Local translation worker exited unexpectedly.");
        }
        finally { _serial.Release(); }
    }

    public async Task TranslateStreamAsync(string text, string targetLanguage, string sourceLanguage, Action<string> onTokenReceived) =>
        onTokenReceived(await TranslateAsync(text, targetLanguage, sourceLanguage));

    private async Task EnsureDaemonStartedAsync(string configPath, DateTime stamp, CancellationToken token)
    {
        if (_daemonProcess is { HasExited: false } &&
            string.Equals(_daemonConfigPath, configPath, StringComparison.OrdinalIgnoreCase) &&
            _daemonConfigStamp == stamp)
        {
            return;
        }

        StopDaemonLocked();

        if (!_exitHookRegistered)
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            _exitHookRegistered = true;
        }

        using var configuration = JsonDocument.Parse(await File.ReadAllTextAsync(configPath, token));
        if (!configuration.RootElement.TryGetProperty("translationPython", out var python))
            throw new InvalidOperationException("Run the updated enable-local-speech.ps1 to configure local translation.");

        var start = new ProcessStartInfo(python.GetString()!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8
        };
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "runtime", "translation_worker.py"));
        start.ArgumentList.Add(configPath);
        start.ArgumentList.Add("--daemon");

        var process = Process.Start(start) ?? throw new IOException("Could not start local translation.");
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _jobHandle = AssignKillOnCloseJob(process);
            }

            string? banner = await process.StandardOutput.ReadLineAsync(token);
            if (string.IsNullOrWhiteSpace(banner))
            {
                string err = await process.StandardError.ReadToEndAsync(token);
                throw new IOException(string.IsNullOrWhiteSpace(err)
                    ? "Local translation failed to start; check the configured environment."
                    : err.Trim());
            }

            using var doc = JsonDocument.Parse(banner);
            if (doc.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.GetString());

            _daemonProcess = process;
            _daemonConfigPath = configPath;
            _daemonConfigStamp = stamp;
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            process.Dispose();
            CloseJobHandle();
            throw;
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => Dispose();

    private void StopDaemonLocked()
    {
        if (_daemonProcess != null)
        {
            try
            {
                if (!_daemonProcess.HasExited) _daemonProcess.Kill(entireProcessTree: true);
            }
            catch { }
            _daemonProcess.Dispose();
            _daemonProcess = null;
        }
        CloseJobHandle();
    }

    private void CloseJobHandle()
    {
        if (_jobHandle != IntPtr.Zero)
        {
            if (OperatingSystem.IsWindows()) CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_exitHookRegistered)
        {
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            _exitHookRegistered = false;
        }
        StopDaemonLocked();
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr AssignKillOnCloseJob(Process process)
    {
        IntPtr job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = 0x2000 /* JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE */ }
            };
            int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(job, 9 /* JobObjectExtendedLimitInformation */, ptr, (uint)size) ||
                    !AssignProcessToJobObject(job, process.Handle))
                {
                    CloseHandle(job);
                    return IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return job;
        }
        catch
        {
            CloseHandle(job);
            return IntPtr.Zero;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
