using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LiveTranslatorOverlay.Core
{
    public class AppSettings
    {
        private static readonly object SettingsLock = new();
        private static readonly JsonSerializerOptions SettingsJson = new(JsonSerializerOptions.Default) { WriteIndented = true };

        [JsonIgnore] public string GroqApiKey { get; set; } = "";
        [JsonIgnore] public string DeepgramApiKey { get; set; } = "";
        public int SourceLangIndex { get; set; } = 0;
        public int TargetLangIndex { get; set; } = 0;
        public string SourceLangTag { get; set; } = "auto";
        public string TargetLangTag { get; set; } = "auto";
        public int EngineIndex { get; set; } = 0;
        public int SttModeIndex { get; set; } = 2;
        public int JapaneseStyleIndex { get; set; } = 0;
        public string AudioDeviceName { get; set; } = "";
        public double CaptionLeft { get; set; } = -1;
        public double CaptionTop { get; set; } = -1;
        public int FontSizeIndex { get; set; } = 1; // Default to Medium
        public string UiLanguage { get; set; } = "en"; // "en", "id", "auto"
        public int UiLanguageIndex { get; set; } = 0; // 0=en, 1=id, 2=auto
        public bool HideFromCapture { get; set; } = false;
        public string DeviceId { get; set; } = "";

        public void UseAvailableEngines(bool preparedTranslation)
        {
            if (SttModeIndex is < 0 or > 3 ||
                (SttModeIndex == 0 && string.IsNullOrWhiteSpace(DeepgramApiKey)) ||
                (SttModeIndex == 1 && string.IsNullOrWhiteSpace(GroqApiKey)))
                SttModeIndex = 2;
            if (EngineIndex is < 0 or > 2 ||
                (EngineIndex == 1 && string.IsNullOrWhiteSpace(GroqApiKey)) ||
                (EngineIndex == 2 && !preparedTranslation))
                EngineIndex = 0;
        }

        public static string GetOrCreateDeviceId()
        {
            try
            {
                string appDataFolder = DataDirectory;
                string idFile = Path.Combine(appDataFolder, "device_id.txt");
                if (File.Exists(idFile))
                {
                    string existingId = File.ReadAllText(idFile).Trim();
                    if (!string.IsNullOrWhiteSpace(existingId)) return existingId;
                }

                var settings = Load();
                if (!string.IsNullOrWhiteSpace(settings.DeviceId))
                {
                    Directory.CreateDirectory(appDataFolder);
                    File.WriteAllText(idFile, settings.DeviceId);
                    return settings.DeviceId;
                }

                string raw = Guid.NewGuid().ToString("N").ToUpperInvariant();
                string newId = $"ID-{raw.Substring(0, 4)}-{raw.Substring(4, 4)}-{raw.Substring(8, 4)}";

                settings.DeviceId = newId;
                settings.Save();

                try
                {
                    Directory.CreateDirectory(appDataFolder);
                    File.WriteAllText(idFile, newId);
                }
                catch { }

                return newId;
            }
            catch
            {
                return "ID-" + Environment.MachineName.GetHashCode().ToString("X8");
            }
        }
        
        public static string DataDirectory => AppContext.GetData("LiveTranslatorOverlay.DataDirectory") as string
            ?? (OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "LiveTranslatorOverlay")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveTranslatorOverlay"));

        public static string GetSettingsPath() => Path.Combine(DataDirectory, "appsettings.json");

        private static string FindSettingsToRead(string path)
        {
            if (!OperatingSystem.IsWindows() || File.Exists(path) ||
                AppContext.GetData("LiveTranslatorOverlay.DataDirectory") != null)
                return path;

            string legacy = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            return File.Exists(legacy) ? legacy
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "appsettings.json"));
        }

        public static AppSettings Load()
        {
            lock (SettingsLock) return LoadCore();
        }

        private static AppSettings LoadCore()
        {
            try
            {
                string path = GetSettingsPath();
                string source = FindSettingsToRead(path);
                if (!File.Exists(source)) return new AppSettings();
                if (JsonNode.Parse(File.ReadAllText(source)) is not JsonObject json) return new AppSettings();
                var settings = json.Deserialize<AppSettings>() ?? new AppSettings();
                if (OperatingSystem.IsWindows())
                {
                    settings.GroqApiKey = ReadKey(json, nameof(GroqApiKey));
                    settings.DeepgramApiKey = ReadKey(json, nameof(DeepgramApiKey));
                    if (source != path || !string.IsNullOrEmpty(ReadString(json, nameof(GroqApiKey))) ||
                        !string.IsNullOrEmpty(ReadString(json, nameof(DeepgramApiKey))))
                    {
                        try { settings.Save(); }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
                        {
                            // Keep existing settings and allow this session to use the original keys if migration cannot write.
                            System.Diagnostics.Trace.TraceWarning("Could not migrate app settings to protected key storage.");
                        }
                    }
                }
                return settings;
            }
            catch { }
            return new AppSettings();
        }

        private static string ReadString(JsonObject json, string name) =>
            json[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? "" : "";

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static string ReadKey(JsonObject json, string name)
        {
            string protectedKey = ReadString(json, name + "Protected");
            if (protectedKey.Length == 0) return ReadString(json, name);
            try
            {
                byte[] plaintext = ProtectedData.Unprotect(Convert.FromBase64String(protectedKey), null, DataProtectionScope.CurrentUser);
                try { return Encoding.UTF8.GetString(plaintext); }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            catch (Exception error) when (error is CryptographicException or FormatException)
            {
                // A copied or damaged credential should not reset unrelated settings.
                return "";
            }
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static void WriteKey(JsonObject json, string name, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            byte[] plaintext = Encoding.UTF8.GetBytes(key);
            try
            {
                json[name + "Protected"] = Convert.ToBase64String(ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser));
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }

        public void Save()
        {
            lock (SettingsLock) SaveCore();
        }

        private void SaveCore()
        {
            var json = (JsonObject)JsonSerializer.SerializeToNode(this)!;
            if (OperatingSystem.IsWindows())
            {
                WriteKey(json, nameof(GroqApiKey), GroqApiKey);
                WriteKey(json, nameof(DeepgramApiKey), DeepgramApiKey);
            }
            string path = GetSettingsPath();
            Directory.CreateDirectory(DataDirectory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json.ToJsonString(SettingsJson));
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
