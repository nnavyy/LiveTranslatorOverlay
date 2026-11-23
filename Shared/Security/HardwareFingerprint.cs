using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace LiveTranslatorOverlay.Core.Security;

public sealed record HardwareIdentity(string FingerprintHash, string GuestDisplayTag, string Platform);

/// <summary>
/// Derives a one-way salted SHA-256 device fingerprint and anonymous User#XXXXX leaderboard handle
/// without exposing raw hardware serial numbers.
/// </summary>
public static class HardwareFingerprint
{
    private const string DomainSalt = "livetranslator.app:hwid:v1:";
    private static readonly object CacheLock = new();
    private static string? _cachedEnvOverride;
    private static HardwareIdentity? _cachedIdentity;

    public static HardwareIdentity GetCurrent(string? fallbackDataDirectory = null)
    {
        if (AppContext.GetData("LiveTranslatorOverlay.HardwareSeed") is string seed &&
            !string.IsNullOrWhiteSpace(seed))
        {
            return BuildFromRawMaterial($"seed|{seed.Trim()}", CurrentPlatformLabel());
        }

        string? envOverride = Environment.GetEnvironmentVariable("LIVETRANSLATOR_HARDWARE_SEED");
        lock (CacheLock)
        {
            if (_cachedIdentity != null && string.Equals(_cachedEnvOverride, envOverride, StringComparison.Ordinal))
                return _cachedIdentity;

            string raw = !string.IsNullOrWhiteSpace(envOverride)
                ? $"env|{envOverride.Trim()}"
                : CollectPlatformMaterial(fallbackDataDirectory);

            var identity = BuildFromRawMaterial(raw, CurrentPlatformLabel());
            if (string.IsNullOrWhiteSpace(envOverride))
            {
                _cachedEnvOverride = envOverride;
                _cachedIdentity = identity;
            }
            return identity;
        }
    }

    public static HardwareIdentity BuildFromRawMaterial(string rawMaterial, string? platformLabel = null)
    {
        if (string.IsNullOrWhiteSpace(rawMaterial))
            throw new ArgumentException("Hardware material must not be empty.", nameof(rawMaterial));

        byte[] bytes = Encoding.UTF8.GetBytes(DomainSalt + rawMaterial.Trim());
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string tag = DeriveGuestDisplayTag(hash);
        return new HardwareIdentity(hash, tag, platformLabel ?? CurrentPlatformLabel());
    }

    /// <summary>
    /// Computes User#10000..User#99999 from the first 32 bits of the lowercase SHA-256 HWID hash.
    /// Matches PostgreSQL guest_tag_from_hwid() bit-for-bit.
    /// </summary>
    public static string DeriveGuestDisplayTag(string fingerprintHash)
    {
        if (string.IsNullOrWhiteSpace(fingerprintHash) || fingerprintHash.Length < 8)
            throw new ArgumentException("Expected 64-character hex fingerprint hash.", nameof(fingerprintHash));

        uint prefix = uint.Parse(fingerprintHash.AsSpan(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        uint number = (prefix % 90000u) + 10000u;
        return $"User#{number:D5}";
    }

    public static string FormatShortHash(string fingerprintHash) =>
        fingerprintHash.Length >= 16
            ? $"{fingerprintHash[..8]}…{fingerprintHash[^6..]}"
            : fingerprintHash;

    private static string CurrentPlatformLabel() =>
        OperatingSystem.IsWindows() ? $"windows-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
        : OperatingSystem.IsMacOS() ? $"macos-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}"
        : $"other-{RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";

    private static string CollectPlatformMaterial(string? fallbackDataDirectory)
    {
        var parts = new List<string>
        {
            $"os={CurrentPlatformLabel()}",
            $"cores={Environment.ProcessorCount}"
        };

        if (OperatingSystem.IsWindows())
        {
            string? machineGuid = ReadWindowsMachineGuid();
            if (!string.IsNullOrWhiteSpace(machineGuid))
                parts.Add($"mguid={machineGuid}");

            uint serial = ReadWindowsSystemVolumeSerial();
            if (serial != 0)
                parts.Add($"vol={serial:x8}");
        }
        else if (OperatingSystem.IsMacOS())
        {
            string? platformUuid = ReadMacPlatformUuid();
            if (!string.IsNullOrWhiteSpace(platformUuid))
                parts.Add($"macuuid={platformUuid}");
        }

        // If OS hardware queries returned no unique identifier (restricted sandbox/container),
        // bind to a persistent random local installation salt rather than failing.
        if (parts.Count <= 2)
        {
            string fallbackDir = string.IsNullOrWhiteSpace(fallbackDataDirectory)
                ? AppSettings.DataDirectory
                : fallbackDataDirectory;
            parts.Add($"fallback={GetOrCreateLocalFallbackSalt(fallbackDir)}");
        }

        return string.Join("|", parts);
    }

    private static string GetOrCreateLocalFallbackSalt(string directory)
    {
        try
        {
            string file = Path.Combine(directory, "device_hwid_salt.txt");
            if (File.Exists(file))
            {
                string existing = File.ReadAllText(file).Trim();
                if (!string.IsNullOrWhiteSpace(existing)) return existing;
            }
            Directory.CreateDirectory(directory);
            string created = Guid.NewGuid().ToString("N");
            File.WriteAllText(file, created);
            return created;
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsMachineGuid()
    {
        nint hKey = 0;
        const int KeyReadWow64 = 0x20119; // KEY_READ | KEY_WOW64_64KEY
        if (RegOpenKeyExW(unchecked((nint)0x80000002), @"SOFTWARE\Microsoft\Cryptography", 0, KeyReadWow64, out hKey) != 0 || hKey == 0)
            return null;
        try
        {
            uint type = 0;
            uint sizeBytes = 256;
            byte[] buffer = new byte[sizeBytes];
            if (RegQueryValueExW(hKey, "MachineGuid", 0, ref type, buffer, ref sizeBytes) == 0 && type == 1 && sizeBytes >= 2)
            {
                return Encoding.Unicode.GetString(buffer, 0, (int)sizeBytes).TrimEnd('\0').Trim().ToLowerInvariant();
            }
        }
        finally
        {
            _ = RegCloseKey(hKey);
        }
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static uint ReadWindowsSystemVolumeSerial()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        if (GetVolumeInformationW(root, null, 0, out uint serial, out _, out _, null, 0))
            return serial;
        return 0;
    }

    private static string? ReadMacPlatformUuid()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("/usr/sbin/ioreg", "-rd1 -c IOPlatformExpertDevice")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            });
            if (proc == null) return null;
            string output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(2000)) { try { proc.Kill(); } catch { } return null; }

            foreach (string line in output.Split('\n'))
            {
                int idx = line.IndexOf("IOPlatformUUID", StringComparison.Ordinal);
                if (idx < 0) continue;
                int eq = line.IndexOf('=', idx);
                if (eq < 0) continue;
                string cleaned = line[(eq + 1)..].Trim().Trim('"').Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(cleaned)) return cleaned;
            }
        }
        catch { }
        return null;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyExW(nint hKey, string lpSubKey, uint ulOptions, int samDesired, out nint phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueExW(nint hKey, string lpValueName, nint lpReserved, ref uint lpType, byte[] lpData, ref uint lpcbData);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(nint hKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder? lpFileSystemNameBuffer,
        int nFileSystemNameSize);
}
