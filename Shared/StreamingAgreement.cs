using System.Globalization;
using System.Text;

namespace LiveTranslatorOverlay.Core;

public readonly record struct StreamingAgreementState(
    string CommittedText,
    string ProvisionalText,
    string FullText,
    string NewlyCommittedText);

/// <summary>
/// Implements Local Agreement-2 (LA-2) word and CJK-grapheme stabilization over
/// incremental sliding-window ASR hypotheses (Macháček et al., arXiv:2307.14743).
/// </summary>
public sealed class StreamingAgreement
{
    private readonly record struct StreamToken(string Text, string MatchKey, bool LeadingSpace, bool IsTrailingPunctOnly)
    {
        public bool EndsWithTerminalPunctuation =>
            Text.Length > 0 && IsTerminalPunct(Text[^1]);
    }

    private readonly List<StreamToken> _committedTokens = [];
    private List<StreamToken> _prevTailTokens = [];

    public void Reset()
    {
        _committedTokens.Clear();
        _prevTailTokens = [];
    }

    public string CurrentCommittedText => JoinTokens(_committedTokens);

    public StreamingAgreementState Observe(string? hypothesis)
    {
        var allTokens = Tokenize(hypothesis);
        if (allTokens.Count == 0)
        {
            string committedOnly = JoinTokens(_committedTokens);
            return new StreamingAgreementState(committedOnly, "", committedOnly, "");
        }

        int committedPrefixMatched = CountMatchedCommittedPrefix(allTokens);
        var currentTail = allTokens.Skip(committedPrefixMatched).ToList();

        int lcpCount = ComputeLcp(_prevTailTokens, currentTail);

        // Hold back a single trailing token when the hypothesis did not grow past it and it carries
        // synthetic terminal punctuation (Whisper commonly appends '.' to short intermediate windows).
        if (lcpCount > 0 && lcpCount == currentTail.Count && currentTail[^1].EndsWithTerminalPunctuation)
        {
            lcpCount--;
        }

        var newlyCommitted = new List<StreamToken>(lcpCount);
        for (int i = 0; i < lcpCount; i++)
        {
            var token = currentTail[i];
            if (_committedTokens.Count == 0 && newlyCommitted.Count == 0)
            {
                token = token with { LeadingSpace = false };
            }
            newlyCommitted.Add(token);
        }

        if (newlyCommitted.Count > 0)
        {
            _committedTokens.AddRange(newlyCommitted);
        }

        var remainingTail = currentTail.Skip(lcpCount).ToList();
        _prevTailTokens = remainingTail;

        string committedText = JoinTokens(_committedTokens);
        string provisionalText = JoinTokens(remainingTail);
        string fullText = CombineParts(committedText, provisionalText, remainingTail);
        string newlyCommittedText = JoinTokens(newlyCommitted);

        return new StreamingAgreementState(committedText, provisionalText, fullText, newlyCommittedText);
    }

    public StreamingAgreementState FinalizeSegment(string? finalHypothesis)
    {
        var allTokens = Tokenize(finalHypothesis);
        int committedPrefixMatched = CountMatchedCommittedPrefix(allTokens);
        var tail = allTokens.Skip(committedPrefixMatched).ToList();

        if (tail.Count == 0 && _prevTailTokens.Count > 0 && allTokens.Count == 0)
        {
            tail = _prevTailTokens;
        }

        var newlyCommitted = new List<StreamToken>(tail.Count);
        for (int i = 0; i < tail.Count; i++)
        {
            var token = tail[i];
            if (_committedTokens.Count == 0 && newlyCommitted.Count == 0)
            {
                token = token with { LeadingSpace = false };
            }
            newlyCommitted.Add(token);
        }

        if (newlyCommitted.Count > 0)
        {
            _committedTokens.AddRange(newlyCommitted);
        }

        _prevTailTokens = [];
        string committedText = JoinTokens(_committedTokens);
        string newlyCommittedText = JoinTokens(newlyCommitted);
        return new StreamingAgreementState(committedText, "", committedText, newlyCommittedText);
    }

    private int CountMatchedCommittedPrefix(IReadOnlyList<StreamToken> tokens)
    {
        if (_committedTokens.Count == 0 || tokens.Count == 0)
        {
            return 0;
        }

        // Exact prefix match first (ignoring casing/trailing punctuation).
        int exact = 0;
        int limit = Math.Min(_committedTokens.Count, tokens.Count);
        while (exact < limit && TokensMatch(_committedTokens[exact], tokens[exact]))
        {
            exact++;
        }

        if (exact == _committedTokens.Count)
        {
            return exact;
        }

        // If Whisper revised a single committed word earlier in the window, keep index alignment bounded
        // so already-committed words are not emitted a second time.
        int anchorWindow = Math.Min(tokens.Count, _committedTokens.Count + 2);
        var lastCommitted = _committedTokens[^1];
        for (int idx = Math.Max(0, _committedTokens.Count - 2); idx < anchorWindow; idx++)
        {
            if (TokensMatch(lastCommitted, tokens[idx]))
            {
                return idx + 1;
            }
        }

        return Math.Min(tokens.Count, _committedTokens.Count);
    }

    private static int ComputeLcp(IReadOnlyList<StreamToken> a, IReadOnlyList<StreamToken> b)
    {
        int n = Math.Min(a.Count, b.Count);
        int i = 0;
        while (i < n && TokensMatch(a[i], b[i]))
        {
            i++;
        }
        return i;
    }

    private static bool TokensMatch(StreamToken a, StreamToken b) =>
        a.MatchKey.Length > 0 && string.Equals(a.MatchKey, b.MatchKey, StringComparison.Ordinal);

    private static List<StreamToken> Tokenize(string? text)
    {
        var tokens = new List<StreamToken>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        string trimmed = text.Trim();
        int i = 0;
        bool pendingSpace = false;

        while (i < trimmed.Length)
        {
            if (!Rune.TryGetRuneAt(trimmed, i, out var rune))
            {
                i++;
                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = tokens.Count > 0;
                i += rune.Utf16SequenceLength;
                continue;
            }

            if (IsCjkOrKana(rune))
            {
                int start = i;
                i += rune.Utf16SequenceLength;

                // Attach immediate trailing punctuation to the CJK unit only for display; normalize for matching.
                while (i < trimmed.Length && Rune.TryGetRuneAt(trimmed, i, out var nextRune) && IsSoftPunct(nextRune))
                {
                    i += nextRune.Utf16SequenceLength;
                }

                string raw = trimmed[start..i];
                string key = NormalizeMatchKey(raw);
                tokens.Add(new StreamToken(raw, key, pendingSpace, key.Length == 0));
                pendingSpace = false;
                continue;
            }

            int wordStart = i;
            while (i < trimmed.Length && Rune.TryGetRuneAt(trimmed, i, out var r))
            {
                if (Rune.IsWhiteSpace(r) || IsCjkOrKana(r))
                {
                    break;
                }
                i += r.Utf16SequenceLength;
            }

            string wordRaw = trimmed[wordStart..i];
            string wordKey = NormalizeMatchKey(wordRaw);
            if (wordKey.Length == 0 && tokens.Count > 0)
            {
                // Standalone punctuation attaches to the preceding token for clean streaming join.
                var prev = tokens[^1];
                tokens[^1] = prev with { Text = prev.Text + wordRaw };
            }
            else
            {
                tokens.Add(new StreamToken(wordRaw, wordKey, pendingSpace, wordKey.Length == 0));
            }
            pendingSpace = false;
        }

        return tokens;
    }

    private static string NormalizeMatchKey(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (var rune in token.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune) || rune.Value is '\'' or '’' or '-')
            {
                sb.Append(Rune.ToLowerInvariant(rune));
            }
        }
        return sb.Length > 0 ? sb.ToString() : token.Trim().ToLowerInvariant();
    }

    private static bool IsCjkOrKana(Rune r)
    {
        int cp = r.Value;
        return (cp >= 0x3040 && cp <= 0x30FF) // Hiragana + Katakana
            || (cp >= 0x31F0 && cp <= 0x31FF)
            || (cp >= 0x4E00 && cp <= 0x9FFF) // CJK Unified Ideographs
            || (cp >= 0x3400 && cp <= 0x4DBF)
            || (cp >= 0xF900 && cp <= 0xFAFF);
    }

    private static bool IsSoftPunct(Rune r)
    {
        var cat = Rune.GetUnicodeCategory(r);
        return cat is UnicodeCategory.OtherPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.FinalQuotePunctuation;
    }

    private static bool IsTerminalPunct(char ch) =>
        ch is '.' or '!' or '?' or '。' or '！' or '？' or ',' or '、' or ';' or '；';

    private static string JoinTokens(IReadOnlyList<StreamToken> tokens, bool hasPrecedingTokens = false)
    {
        if (tokens.Count == 0)
        {
            return "";
        }

        var sb = new StringBuilder();
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (i > 0 || hasPrecedingTokens)
            {
                if (token.LeadingSpace)
                {
                    sb.Append(' ');
                }
                else if (i > 0 && !StartsWithCjk(token.Text) && !EndsWithCjk(tokens[i - 1].Text))
                {
                    sb.Append(' ');
                }
            }
            sb.Append(token.Text);
        }
        return sb.ToString();
    }

    private static string CombineParts(string committed, string provisional, IReadOnlyList<StreamToken> remainingTail)
    {
        if (committed.Length == 0) return provisional;
        if (provisional.Length == 0) return committed;

        if (remainingTail.Count > 0 && !remainingTail[0].LeadingSpace && StartsWithCjk(provisional) && EndsWithCjk(committed))
        {
            return committed + provisional;
        }
        return provisional.StartsWith(' ') ? committed + provisional : committed + " " + provisional;
    }

    private static bool StartsWithCjk(string s) =>
        s.Length > 0 && Rune.TryGetRuneAt(s, 0, out var r) && IsCjkOrKana(r);

    private static bool EndsWithCjk(string s)
    {
        if (s.Length == 0) return false;
        int idx = char.IsLowSurrogate(s[^1]) && s.Length >= 2 ? s.Length - 2 : s.Length - 1;
        return Rune.TryGetRuneAt(s, idx, out var r) && IsCjkOrKana(r);
    }
}
