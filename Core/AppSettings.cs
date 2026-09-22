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


