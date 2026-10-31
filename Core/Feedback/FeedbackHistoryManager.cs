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

    public static FeedbackRecord AddRecord(string type, string subject, string description, string contactEmail, string? appVersion = null)
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
            AppVersion = appVersion ?? LiveTranslatorOverlay.Core.Update.AppVersion.DisplayString,
            Status = "Pending",
            ResolutionNote = ""
        };

        var list = Load();
        list.Insert(0, record);
        Save(list);
        return record;
    }

    public static async Task<bool> RefreshStatusesAsync(string? currentAppVersion = null)
    {
        var records = Load();
        if (records.Count == 0) return false;
        using var response = await HttpClient.GetAsync("https://raw.githubusercontent.com/nnavyy/LiveTranslatorOverlay/main/reports_status.json");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        bool modified = ApplyStatuses(records, doc.RootElement);
        if (modified) Save(records);
        return modified;
    }

    // A release/version change is never evidence that a particular report was fixed.
    internal static bool ApplyStatuses(List<FeedbackRecord> records, JsonElement statuses)
    {
        bool modified = false;
        foreach (var record in records)
        {
            if (!statuses.TryGetProperty(record.Id, out var entry) || entry.ValueKind != JsonValueKind.Object) continue;
            if (entry.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
                && status.GetString() is string newStatus && newStatus is "Pending" or "InProgress" or "Fixed"
                && record.Status != newStatus)
            {
                record.Status = newStatus;
                modified = true;
            }
            if (entry.TryGetProperty("note", out var note) && note.ValueKind == JsonValueKind.String
                && record.ResolutionNote != note.GetString())
            {
                record.ResolutionNote = note.GetString() ?? "";
                modified = true;
            }
        }
        return modified;
    }
}