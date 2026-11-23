using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LiveTranslatorOverlay.Core.Security;

namespace LiveTranslatorOverlay.Core;

public sealed record SupportLedgerEntry(
    string Id,
    string OfferId,
    string OfferTitle,
    long Cents,
    string Mode,
    string Status,
    DateTimeOffset CreatedUtc)
{
    public string AmountLabel => AccountSessionStore.FormatUsd(Cents);
}

public sealed record AccountSessionState(
    string Email,
    string DisplayName,
    string AccessToken,
    string ActiveTier,
    long TotalDonatedCents,
    bool BetaAccessUnlocked,
    bool IsLocalStaging,
    DateTimeOffset SignedInUtc,
    SupportLedgerEntry[] History,
    string HardwareFingerprintHash = "",
    SignedEntitlement? Entitlement = null)
{
    public string TierDisplayName => ActiveTier switch
    {
        "insider" => "Lifetime Insider (US$100 · Beta unlocked)",
        "supporter" => "Lifetime Supporter (US$19.99)",
        _ => "Free (MIT Open Source)"
    };
}

public sealed record GuestDonationLedgerState(
    string GuestTag,
    string HardwareFingerprintHash,
    long TotalDonatedCents,
    SupportLedgerEntry[] History);

public sealed record UserProfileState(
    string Nickname,
    bool SyncUsage,
    bool PublicUsage,
    bool PublicSupport,
    bool ShowMedals,
    DateTimeOffset UpdatedUtc);

public sealed record OwnerAdminConfig(
    string SupabaseUrl,
    string ServiceRoleKey);

/// <summary>Persists signed-in account state and tracked licenses/donations locally under DPAPI (Windows) or 0600 permissions.</summary>
public static class AccountSessionStore
{
    private const string ProtectedPrefix = "DPAPI1:";
    private static readonly object StoreLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string FormatUsd(long cents) =>
        "US$" + (cents / 100m).ToString(cents % 100 == 0 ? "0.00" : "N2", CultureInfo.InvariantCulture);

    private static string GetSessionPath(string dataDirectory) =>
        Path.Combine(dataDirectory, "account-session.dat");

    public static AccountSessionState? Load(string dataDirectory) =>
        LoadProtectedJson<AccountSessionState>(GetSessionPath(dataDirectory));

    public static void Save(string dataDirectory, AccountSessionState state) =>
        SaveProtectedJson(GetSessionPath(dataDirectory), state);

    public static void Clear(string dataDirectory)
    {
        lock (StoreLock)
        {
            string path = GetSessionPath(dataDirectory);
            if (File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    public static AccountSessionState RecordSupport(
        string dataDirectory,
        AccountSessionState current,
        string entryId,
        string offerId,
        string offerTitle,
        long cents,
        string mode,
        string status,
        bool countsTowardTier)
    {
        var updatedHistory = new List<SupportLedgerEntry>(current.History ?? [])
        {
            new(entryId, offerId, offerTitle, cents, mode, status, DateTimeOffset.UtcNow)
        };

        long nextTotal = Math.Max(0, current.TotalDonatedCents) + Math.Max(0, cents);
        string nextTier = current.ActiveTier;
        bool beta = current.BetaAccessUnlocked;

        if (countsTowardTier)
        {
            if (string.Equals(offerId, "insider", StringComparison.OrdinalIgnoreCase))
            {
                nextTier = "insider";
                beta = true;
            }
            else if (string.Equals(offerId, "supporter", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(nextTier, "insider", StringComparison.OrdinalIgnoreCase))
            {
                nextTier = "supporter";
            }
        }

        var updated = current with
        {
            ActiveTier = nextTier,
            TotalDonatedCents = nextTotal,
            BetaAccessUnlocked = beta || nextTotal >= 10000,
            History = updatedHistory.ToArray()
        };
        Save(dataDirectory, updated);
        return updated;
    }

    internal static T? LoadProtectedJson<T>(string path) where T : class
    {
        lock (StoreLock)
        {
            if (!File.Exists(path)) return null;
            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                string json = raw.StartsWith(ProtectedPrefix, StringComparison.Ordinal)
                    ? Unprotect(raw[ProtectedPrefix.Length..])
                    : raw;
                return string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException)
            {
                return null;
            }
        }
    }

    internal static void SaveProtectedJson<T>(string path, T value)
    {
        lock (StoreLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(value, JsonOptions);
            string payload = OperatingSystem.IsWindows()
                ? ProtectedPrefix + Protect(json)
                : json;

            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, payload, Encoding.UTF8);
                if (!OperatingSystem.IsWindows())
                {
                    try { File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                    catch { }
                }
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); } catch { }
                }
            }
        }
    }

    private static string Protect(string plaintext)
    {
        if (!OperatingSystem.IsWindows()) return plaintext;
        byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            byte[] encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string Unprotect(string base64)
    {
        if (!OperatingSystem.IsWindows()) return "";
        byte[] encrypted = Convert.FromBase64String(base64);
        byte[] plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

/// <summary>Stores login-optional guest donations keyed to this device's anonymous User#XXXXX handle.</summary>
public static class GuestDonationLedgerStore
{
    private static string GetLedgerPath(string dataDirectory) =>
        Path.Combine(dataDirectory, "guest-donations.dat");

    public static GuestDonationLedgerState? Load(string dataDirectory) =>
        AccountSessionStore.LoadProtectedJson<GuestDonationLedgerState>(GetLedgerPath(dataDirectory));

    public static GuestDonationLedgerState RecordDonation(
        string dataDirectory,
        HardwareIdentity identity,
        string entryId,
        long cents,
        string mode,
        string status)
    {
        var existing = Load(dataDirectory);
        var entries = new List<SupportLedgerEntry>(existing?.History ?? [])
        {
            new(entryId, "donation", $"Donate ({identity.GuestDisplayTag})", cents, mode, status, DateTimeOffset.UtcNow)
        };
        long nextTotal = Math.Max(0, existing?.TotalDonatedCents ?? 0) + Math.Max(0, cents);
        var updated = new GuestDonationLedgerState(identity.GuestDisplayTag, identity.FingerprintHash, nextTotal, entries.ToArray());
        AccountSessionStore.SaveProtectedJson(GetLedgerPath(dataDirectory), updated);
        return updated;
    }
}

/// <summary>Persists optional community ranking profile preferences locally under DPAPI or 0600 permissions.</summary>
public static class UserProfileStore
{
    private static string GetProfilePath(string dataDirectory) =>
        Path.Combine(dataDirectory, "user-profile.dat");

    public static UserProfileState? Load(string dataDirectory) =>
        AccountSessionStore.LoadProtectedJson<UserProfileState>(GetProfilePath(dataDirectory));

    public static void Save(string dataDirectory, UserProfileState profile) =>
        AccountSessionStore.SaveProtectedJson(GetProfilePath(dataDirectory), profile);

    public static void Clear(string dataDirectory)
    {
        string path = GetProfilePath(dataDirectory);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }
}

/// <summary>Stores owner-only Supabase Service Role connection locally (DPAPI / 0600, gitignored).</summary>
public static class OwnerAdminConfigStore
{
    private static string GetConfigPath(string dataDirectory) =>
        Path.Combine(dataDirectory, "owner-admin.dat");

    public static OwnerAdminConfig? Load(string dataDirectory) =>
        AccountSessionStore.LoadProtectedJson<OwnerAdminConfig>(GetConfigPath(dataDirectory));

    public static void Save(string dataDirectory, OwnerAdminConfig config) =>
        AccountSessionStore.SaveProtectedJson(GetConfigPath(dataDirectory), config);

    public static void Clear(string dataDirectory)
    {
        string path = GetConfigPath(dataDirectory);
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { }
        }
    }
}
