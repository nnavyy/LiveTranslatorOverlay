using System;
using System.IO;
using System.Text.Json;

namespace LiveTranslatorOverlay.Core
{
    public class AppSettings
    {
        public string GroqApiKey { get; set; } = "";
        public string DeepgramApiKey { get; set; } = "";
        public int SourceLangIndex { get; set; } = 0;
        public int TargetLangIndex { get; set; } = 0;
        public int EngineIndex { get; set; } = 0;
        public int SttModeIndex { get; set; } = 0;
        public string AudioDeviceName { get; set; } = "";
        public double CaptionLeft { get; set; } = -1;
        public double CaptionTop { get; set; } = -1;
        public int FontSizeIndex { get; set; } = 1; // Default to Medium
        public string UiLanguage { get; set; } = "en"; // "en", "id", "auto"
        public int UiLanguageIndex { get; set; } = 0; // 0=en, 1=id, 2=auto
        public bool HideFromCapture { get; set; } = false;
        public string DeviceId { get; set; } = "";

        public static string GetOrCreateDeviceId()
        {
            try
            {
                string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiveTranslatorOverlay");
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
        
        public static string GetSettingsPath()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string settingsPath = Path.Combine(exeDir, "appsettings.json");
            if (!File.Exists(settingsPath))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
                settingsPath = Path.Combine(projectRoot, "appsettings.json");
            }
            return settingsPath;
        }

        public static AppSettings Load()
        {
            try
            {
                string path = GetSettingsPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string path = GetSettingsPath();
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}


