using System.Collections.Generic;

namespace LiveTranslatorOverlay.Core.Localization;

public static class AdditionalLanguages
{
    public static IReadOnlyDictionary<string, string> Names { get; } = new Dictionary<string, string>
    {
        ["ms"] = "Malay · Bahasa Melayu",
        ["vi"] = "Vietnamese · Tiếng Việt",
        ["th"] = "Thai · ไทย",
        ["pt"] = "Portuguese · Português",
        ["it"] = "Italian · Italiano",
        ["nl"] = "Dutch · Nederlands",
        ["pl"] = "Polish · Polski",
        ["hi"] = "Hindi · हिन्दी",
        ["tr"] = "Turkish · Türkçe"
    };

    public static string NameOrCode(string code) => Names.TryGetValue(code, out var name) ? name : code;
}
