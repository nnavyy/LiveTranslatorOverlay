using System.Text;
using System.Text.RegularExpressions;

namespace LiveTranslatorOverlay.Core;

public static class SentenceBoundaryDeduplicator
{
    private static readonly Regex WordPattern = new(@"[\p{L}\p{N}'’]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly record struct WordSpan(string Normalized, int StartIndex, int EndIndex);
    private readonly record struct CharPos(char NormChar, int OrigIdx);

    public static string StripOverlapPrefix(string? previousText, string? currentText)
    {
        if (string.IsNullOrWhiteSpace(currentText))
            return "";

        string trimmedCurrent = currentText.Trim();
        if (string.IsNullOrWhiteSpace(previousText))
            return trimmedCurrent;

        string normPrevAll = NormalizeAlphanumeric(previousText);
        string normCurrAll = NormalizeAlphanumeric(trimmedCurrent);
        if (normCurrAll.Length >= 4 &&
            (string.Equals(normCurrAll, normPrevAll, StringComparison.Ordinal) ||
             normPrevAll.EndsWith(normCurrAll, StringComparison.Ordinal)))
        {
            return "";
        }

        if (ContainsCjkOrKana(trimmedCurrent))
        {
            var prevChars = ExtractCharacters(previousText);
            var currChars = ExtractCharacters(trimmedCurrent);
            int maxCharOverlap = Math.Min(12, Math.Min(prevChars.Count, currChars.Count));
            for (int k = maxCharOverlap; k >= 2; k--)
            {
                int prevOffset = prevChars.Count - k;
                bool matched = true;
                for (int i = 0; i < k; i++)
                {
                    if (prevChars[prevOffset + i].NormChar != currChars[i].NormChar)
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    int cutIdx = currChars[k - 1].OrigIdx + 1;
                    // Avoid slicing in the middle of a non-CJK word inside mixed CJK+Latin text.
                    if (cutIdx < trimmedCurrent.Length &&
                        !IsCjkOrKanaChar(trimmedCurrent[cutIdx - 1]) &&
                        !IsCjkOrKanaChar(trimmedCurrent[cutIdx]) &&
                        char.IsLetterOrDigit(trimmedCurrent[cutIdx]))
                    {
                        continue;
                    }

                    return TrimLeadingBoundary(trimmedCurrent.AsSpan(cutIdx));
                }
            }
        }

        var prevWords = ExtractWords(previousText);
        var currWords = ExtractWords(trimmedCurrent);
        if (prevWords.Count > 0 && currWords.Count > 0)
        {
            int maxWordOverlap = Math.Min(8, Math.Min(prevWords.Count, currWords.Count));
            for (int k = maxWordOverlap; k >= 1; k--)
            {
                if (k == 1 && currWords[0].Normalized.Length < 5)
                    continue;

                // Unspaced CJK strings are handled by the CJK character pass above.
                if (k == 1 && ContainsCjkOrKana(currWords[0].Normalized))
                    continue;

                int prevOffset = prevWords.Count - k;
                bool matched = true;
                for (int i = 0; i < k; i++)
                {
                    if (!string.Equals(prevWords[prevOffset + i].Normalized, currWords[i].Normalized, StringComparison.Ordinal))
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    int sliceStart = currWords[k - 1].EndIndex;
                    return TrimLeadingBoundary(trimmedCurrent.AsSpan(sliceStart));
                }
            }
        }

        return trimmedCurrent;
    }

    private static List<WordSpan> ExtractWords(string text)
    {
        var matches = WordPattern.Matches(text);
        var words = new List<WordSpan>(matches.Count);
        foreach (Match match in matches)
        {
            string normalized = NormalizeWordToken(match.Value);
            if (normalized.Length > 0)
            {
                words.Add(new WordSpan(normalized, match.Index, match.Index + match.Length));
            }
        }
        return words;
    }

    private static List<CharPos> ExtractCharacters(string text)
    {
        var list = new List<CharPos>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))
                continue;

            list.Add(new CharPos(NormalizeChar(c), i));
        }
        return list;
    }

    private static string NormalizeAlphanumeric(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(NormalizeChar(c));
        }
        return sb.ToString();
    }

    private static string NormalizeWordToken(string token)
    {
        var sb = new StringBuilder(token.Length);
        bool hasAlphaNum = false;
        foreach (char c in token)
        {
            if (char.IsLetterOrDigit(c))
            {
                hasAlphaNum = true;
                sb.Append(NormalizeChar(c));
            }
            else if (c is '\'' or '’')
            {
                sb.Append('\'');
            }
        }

        if (!hasAlphaNum)
            return "";

        return sb.ToString().Trim('\'');
    }

    private static char NormalizeChar(char c)
    {
        char lower = char.ToLowerInvariant(c);
        return lower == 'ё' ? 'е' : lower;
    }

    private static bool ContainsCjkOrKana(string text)
    {
        foreach (char c in text)
        {
            if (IsCjkOrKanaChar(c))
                return true;
        }
        return false;
    }

    private static bool IsCjkOrKanaChar(char c) =>
        (c >= '\u3040' && c <= '\u30FF') || // Hiragana + Katakana
        (c >= '\u31F0' && c <= '\u31FF') || // Katakana Phonetic Extensions
        (c >= '\u3400' && c <= '\u4DBF') || // CJK Unified Ideographs Extension A
        (c >= '\u4E00' && c <= '\u9FFF') || // CJK Unified Ideographs
        (c >= '\uF900' && c <= '\uFAFF');   // CJK Compatibility Ideographs

    private static string TrimLeadingBoundary(ReadOnlySpan<char> span)
    {
        int i = 0;
        while (i < span.Length && (char.IsWhiteSpace(span[i]) || IsLeadingClausePunctuation(span[i])))
        {
            i++;
        }

        return i >= span.Length ? "" : span[i..].TrimEnd().ToString();
    }

    private static bool IsLeadingClausePunctuation(char c) =>
        c is ',' or '.' or '!' or '?' or ';' or ':' or '—' or '–' or '-' or '…'
            or '、' or '。' or '，' or '！' or '？' or '；' or '：';
}
