namespace LiveTranslatorOverlay.Core;

// Session-only history. Persistence/export always requires an explicit user action.
public sealed class TranslationHistory
{
    private readonly Queue<string> _entries = new();
    private long _version;

    // Monotonic count of entries ever added (never reset by Clear). Lets a view that was
    // seeded from a snapshot ignore entries it has already shown.
    public long Version => _version;

    // Returns the formatted entry that was stored, or null when nothing was added.
    public string? Add(string source, string translation) => Add(source, translation, out _);

    public string? Add(string source, string translation, out long version)
    {
        version = _version;
        if (string.IsNullOrWhiteSpace(translation)) return null;
        string entry = $"[{DateTime.Now:HH:mm:ss}]\nOriginal: {source}\nTranslated: {translation}\n\n";
        _entries.Enqueue(entry);
        while (_entries.Count > 500) _entries.Dequeue();
        version = ++_version;
        return entry;
    }

    public void Clear() => _entries.Clear();
    public override string ToString() => string.Concat(_entries);
}

// Decides whether a history view should keep following the newest entry.
public static class HistoryScrollPolicy
{
    public const double DefaultTolerance = 4;

    // True when the reader is at (or within a few px of) the bottom, or nothing scrolls yet.
    public static bool ShouldFollowTail(double verticalOffset, double viewportHeight, double extentHeight, double tolerance = DefaultTolerance) =>
        extentHeight <= viewportHeight + tolerance || verticalOffset + viewportHeight >= extentHeight - tolerance;
}
