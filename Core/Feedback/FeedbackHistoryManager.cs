using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiveTranslatorOverlay.Core.Feedback;

public static class FeedbackHistoryManager
{
    private static readonly object Gate = new();
    private static string HistoryFile => Path.Combine(AppSettings.DataDirectory, "feedback_history.json");
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    static FeedbackHistoryManager() => HttpClient.DefaultRequestHeaders.Add("User-Agent", "LiveTranslatorOverlay");

    public static List<FeedbackRecord> Load()
    {
        lock (Gate)
        {
            if (!File.Exists(HistoryFile)) return new();
            var records = JsonSerializer.Deserialize<List<FeedbackRecord>>(File.ReadAllText(HistoryFile))
                ?? throw new InvalidDataException("The report history file is invalid.");
            records.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            return records;
        }
    }

    public static void Save(List<FeedbackRecord> items)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(AppSettings.DataDirectory);
            string temporary = HistoryFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, HistoryFile, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static FeedbackRecord AddRecord(string type, string subject, string description, string contactEmail, string? appVersion = null, string deliveryState = "Sending")
    {
        lock (Gate)
        {
            var record = new FeedbackRecord
            {
                Id = "REP-" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
                DeviceId = AppSettings.GetOrCreateDeviceId(),
                Timestamp = DateTime.UtcNow, Type = type, Subject = subject, Description = description,
                ContactEmail = contactEmail, AppVersion = appVersion ?? Update.AppVersion.DisplayString,
                Status = "Pending", DeliveryState = deliveryState
            };
            var records = Load(); records.Insert(0, record); Save(records);
            return record;
        }
    }

    public static void SetDeliveryState(string id, string state)
    {
        if (state is not ("Sending" or "Sent" or "Failed" or "ComposerOpened")) throw new ArgumentException("Unknown delivery state.");
        lock (Gate)
        {
            var records = Load();
            var record = records.Find(r => r.Id == id) ?? throw new InvalidDataException("Report history entry is missing.");
            record.DeliveryState = state; Save(records);
        }
    }

    public static async Task<bool> RefreshStatusesAsync(string? currentAppVersion = null)
    {
        if (Load().Count == 0) return false;
        using var response = await HttpClient.GetAsync("https://raw.githubusercontent.com/nnavyy/LiveTranslatorOverlay/main/reports_status.json");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        lock (Gate)
        {
            // Reload after the network request so reports created while it ran are retained.
            var records = Load();
            bool modified = ApplyStatuses(records, doc.RootElement);
            if (modified) Save(records);
            return modified;
        }
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
