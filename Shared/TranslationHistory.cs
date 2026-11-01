
namespace LiveTranslatorOverlay.Core;

// Session-only history. Persistence/export always requires an explicit user action.
public sealed class TranslationHistory
{
    private readonly Queue<string> _entries = new();
    public void Add(string source, string translation)
    {
        if (string.IsNullOrWhiteSpace(translation)) return;
        _entries.Enqueue($"[{DateTime.Now:HH:mm:ss}]\nOriginal: {source}\nTranslated: {translation}\n\n");
        while (_entries.Count > 500) _entries.Dequeue();
    }
    public void Clear() => _entries.Clear();
    public override string ToString() => string.Concat(_entries);
}
