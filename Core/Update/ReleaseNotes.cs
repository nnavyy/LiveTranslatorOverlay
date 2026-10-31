using System;
using System.IO;
using LiveTranslatorOverlay.Core.Localization;

namespace LiveTranslatorOverlay.Core.Update;

public static class ReleaseNotes
{
    private static string SeenVersionPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveTranslatorOverlay", "last_seen_release.txt");

    public static bool ShouldNotify(string? lastSeenVersion) => lastSeenVersion != AppVersion.DisplayString;

    public static bool HasUnseenRelease()
    {
        try { return ShouldNotify(File.Exists(SeenVersionPath) ? File.ReadAllText(SeenVersionPath).Trim() : null); }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    public static void MarkSeen()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SeenVersionPath)!);
        File.WriteAllText(SeenVersionPath, AppVersion.DisplayString);
    }

    public static string Text => LocalizationManager.Instance.Get("ReleaseNotes140");
}
