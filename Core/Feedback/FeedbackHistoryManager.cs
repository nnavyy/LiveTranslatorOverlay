using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiveTranslatorOverlay.Core.Feedback;

public static class FeedbackHistoryManager
{
    private static readonly string HistoryFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveTranslatorOverlay");

    private static readonly string HistoryFile = Path.Combine(HistoryFolder, "feedback_history.json");

    private static readonly HttpClient HttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    static FeedbackHistoryManager()
    {
        if (!HttpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "LiveTranslatorOverlay");
        }
    }

    public static List<FeedbackRecord> Load()
    {
        try
        {
            if (File.Exists(HistoryFile))
            {
                string json = File.ReadAllText(HistoryFile);
                var items = JsonSerializer.Deserialize<List<FeedbackRecord>>(json);
                if (items != null)
                {
                    items.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
                    return items;
                }
            }
        }
        catch { }

        return new List<FeedbackRecord>();
    }

    public static void Save(List<FeedbackRecord> items)
    {
        try
        {
            Directory.CreateDirectory(HistoryFolder);
            string json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(HistoryFile, json);
        }
        catch { }
    }

    public static FeedbackRecord AddRecord(string type, string subject, string description, string contactEmail, string appVersion = "v1.1.0")
    {
        string raw = Guid.NewGuid().ToString("N").ToUpperInvariant();
        string id = $"REP-{raw.Substring(0, 6)}";

        var record = new FeedbackRecord
        {
            Id = id,
            DeviceId = AppSettings.GetOrCreateDeviceId(),
            Timestamp = DateTime.UtcNow,
            Type = type,
            Subject = subject,
            Description = description,
            ContactEmail = contactEmail,
            AppVersion = appVersion,
            Status = "Pending",
            ResolutionNote = ""
        };

        var list = Load();
        list.Insert(0, record);
        Save(list);
        return record;
    }

    public static async Task<bool> RefreshStatusesAsync(string currentAppVersion = "v1.1.0")
    {
        var records = Load();
        if (records.Count == 0) return false;

        bool modified = false;

        // 1. Check reports_status.json from GitHub repo (developer remote override)
        try
        {
            string statusFileUrl = "https://raw.githubusercontent.com/nnavyy/LiveTranslatorOverlay/main/reports_status.json";
            var response = await HttpClient.GetAsync(statusFileUrl);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                foreach (var record in records)
                {
                    if (doc.RootElement.TryGetProperty(record.Id, out var prop))
                    {
                        if (prop.TryGetProperty("status", out var sProp))
                        {
                            string newStatus = sProp.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(newStatus) && record.Status != newStatus)
                            {
                                record.Status = newStatus;
                                modified = true;
                            }
                        }
                        if (prop.TryGetProperty("note", out var nProp))
                        {
                            string note = nProp.GetString() ?? "";
                            if (record.ResolutionNote != note)
                            {
                                record.ResolutionNote = note;
                                modified = true;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 2. Check latest GitHub Release version
        try
        {
            string releaseUrl = "https://api.github.com/repos/nnavyy/LiveTranslatorOverlay/releases/latest";
            var response = await HttpClient.GetAsync(releaseUrl);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tag_name", out var tagProp))
                {
                    string latestTag = tagProp.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(latestTag))
                    {
                        foreach (var record in records)
                        {
                            // If reported in an older version and a newer version has been released
                            if (IsVersionNewer(latestTag, record.AppVersion))
                            {
                                if (record.Status == "Pending")
                                {
                                    record.Status = "Fixed";
                                    record.ResolutionNote = $"Resolved in {latestTag}";
                                    modified = true;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }

        // 3. Fallback: Check if current installed app is newer than report version
        foreach (var record in records)
        {
            if (IsVersionNewer(currentAppVersion, record.AppVersion))
            {
                if (record.Status == "Pending")
                {
                    record.Status = "Fixed";
                    record.ResolutionNote = $"Resolved in {currentAppVersion}";
                    modified = true;
                }
            }
        }

        if (modified)
        {
            Save(records);
        }

        return modified;
    }

    private static bool IsVersionNewer(string vCandidate, string vBase)
    {
        try
        {
            string cleanCandidate = vCandidate.TrimStart('v', 'V');
            string cleanBase = vBase.TrimStart('v', 'V');

            if (Version.TryParse(cleanCandidate, out var parsedCandidate) &&
                Version.TryParse(cleanBase, out var parsedBase))
            {
                return parsedCandidate > parsedBase;
            }
        }
        catch { }

        return false;
    }
}
