using System.Text;

namespace LiveTranslatorOverlay.Core.Japanese;

internal static class WindowsPhoneticReader
{
    internal static string Read(string text)
    {
        var words = Windows.Globalization.JapanesePhoneticAnalyzer.GetWords(text);
        if (words == null || words.Count == 0) return text;
        var result = new StringBuilder();
        foreach (var word in words)
            result.Append(string.IsNullOrWhiteSpace(word.YomiText) ? word.DisplayText
                : JapaneseScriptConverter.KatakanaToHiragana(word.YomiText));
        return result.ToString();
    }
}
