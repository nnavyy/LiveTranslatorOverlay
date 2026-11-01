using System.Collections.Generic;
namespace LiveTranslatorOverlay.Core.Localization;
public static class LanguageCatalog
{
    public static IReadOnlyDictionary<string, string> Names { get; } = Create();
    private static Dictionary<string, string> Create()
    {
        var names = new Dictionary<string, string>
        {
        ["sq"] = "Albanian (sq)",
        ["ar"] = "Arabic (ar)",
        ["az"] = "Azerbaijani (az)",
        ["bs"] = "Bosnian (bs)",
        ["bg"] = "Bulgarian (bg)",
        ["zh"] = "Chinese Simplified (zh)",
        ["zh-TW"] = "Chinese Traditional (zh-TW)",
        ["hr"] = "Croatian (hr)",
        ["en"] = "English (en)",
        ["fr"] = "French (fr)",
        ["ka"] = "Georgian (ka)",
        ["de"] = "German (de)",
        ["el"] = "Greek (el)",
        ["id"] = "Indonesian (id)",
        ["ja"] = "Japanese (ja)",
        ["ko"] = "Korean (ko)",
        ["mk"] = "Macedonian (mk)",
        ["mn"] = "Mongolian (mn)",
        ["ro"] = "Romanian (ro)",
        ["ru"] = "Russian (ru)",
        ["sr"] = "Serbian (sr)",
        ["sl"] = "Slovenian (sl)",
        ["es"] = "Spanish (es)",
        ["uk"] = "Ukrainian (uk)",
        ["uz"] = "Uzbek (uz)",
        };
        foreach (var language in AdditionalLanguages.Names) names[language.Key] = language.Value;
        return names;
    }
}
