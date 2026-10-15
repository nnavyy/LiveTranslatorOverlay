using System;
using System.Reflection;

namespace LiveTranslatorOverlay.Core.Update
{
    public static class AppVersion
    {
        public static readonly Version Current = new Version(1, 2, 3);
        public static string DisplayString => $"v{Current.Major}.{Current.Minor}.{Current.Build}";

        public static bool IsNewerThanCurrent(string tagOrVersion)
        {
            if (string.IsNullOrWhiteSpace(tagOrVersion)) return false;
            string clean = tagOrVersion.Trim().TrimStart('v', 'V');
            if (Version.TryParse(clean, out var remoteVersion))
            {
                return remoteVersion > Current;
            }
            return false;
        }
    }
}
