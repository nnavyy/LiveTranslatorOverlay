using System;

namespace LiveTranslatorOverlay.Core.Feedback;

public class FeedbackRecord
{
    public string Id { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "Bug Report";
    public string Subject { get; set; } = "";
    public string Description { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string AppVersion { get; set; } = "v1.1.0";
    public string Status { get; set; } = "Pending"; // "Pending", "InProgress", "Fixed"
    public string ResolutionNote { get; set; } = "";
}
