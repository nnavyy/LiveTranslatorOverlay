using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LiveTranslatorOverlay.Core
{
    public static class HallucinationDetector
    {
        private static readonly RegexOptions RxOpts =
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private static readonly Regex WatermarkClauseRegex = new(
            @"(?:(?:subtitles?|captions?|transcript|translated|translation|synced(?:\s+and\s+corrected)?)\s+(?:made\s+)?by\b|субтитр\w*\s+(?:сделал|создавал|подогнал|добавил|подготовил|предоставил|от)\b|редактор\s+субтитр\w*|корректор\b|перевод\s+субтитр\w*|субтитри\s+зробив|редактор\s+субтитрів|subtitrare\s+realizată|ترجمة\s+نانسي|terjemahan\s+oleh|diterjemahkan\s+oleh|subtitel\s+oleh)[^\r\n]*?(?=$|[\r\n]|[.!?](?:\s|$)|♪)",
            RxOpts);

        private static readonly Regex KnownWatermarkRegex = new(
            @"[^.!?\r\n]*\b(?:dima\s*torzok|дима\s*торзок|amara\.org|opensubtitles(?:\.org)?|subscene)\b[^\r\n]*?(?=$|[\r\n]|[.!?](?:\s|$)|♪)",
            RxOpts);

        private static readonly Regex NonSpeechTagRegex = new(
            @"\[(?:blank_audio|music|silence|noise|inaudible|singing|instrumental|applause|音楽|bgm|歌)\]|\((?:music|applause|singing|instrumental|音楽|bgm|歌)\)|【音楽】|[\u2669-\u266F]+",
            RxOpts);

        private static readonly HashSet<string> StandaloneClichéSet = new(StringComparer.Ordinal)
        {
            "thank you for watching",
            "thanks for watching",
            "thanks for listening",
            "thank you for listening",
            "thanks for the attention",
            "please subscribe",
            "subscribe to my channel",
            "like and subscribe",
            "to be continued",
            "the end",
            "продолжение следует",
            "спасибо за просмотр",
            "спасибо за внимание",
            "подписывайтесь на канал",
            "подписывайтесь",
            "terima kasih telah menonton",
            "terima kasih sudah menonton",
            "jangan lupa subscribe",
            "ご視聴ありがとうございました",
            "ご視聴ありがとうございます",
            "チャンネル登録",
            "チャンネル登録お願いします",
            "感谢观看",
            "请订阅",
            "請訂閱",
            "시청해주셔서 감사합니다",
            "구독과 좋아요",
            "дякую за перегляд",
            "дякуємо за перегляд",
            "підписуйтесь на канал",
            "підпишіться на канал",
            "izlədiyiniz üçün təşəkkürlər",
            "tomosha qilganingiz uchun rahmat",
            "here is the translation",
            "as an ai language model",
            "i cannot translate",
            "no text to translate",
            "please provide the text",
            "transcribe spoken conversation directly",
            "accurate transcription of spoken conversation",
            "please transcribe the japanese conversation accurately",
            "日本語の会話を正確に書き起こしてください",
            "会話の書き起こし"
        };

        public static string StripWhisperArtifacts(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            string cleaned = WatermarkClauseRegex.Replace(text, "");
            cleaned = KnownWatermarkRegex.Replace(cleaned, "");
            cleaned = NonSpeechTagRegex.Replace(cleaned, "");
            cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");
            cleaned = Regex.Replace(cleaned, @"\s*[,;:\-]+\s*$", "");
            cleaned = Regex.Replace(cleaned, @"(?<=[.!?])\s*[.!?]+\s*$", "");
            cleaned = cleaned.Trim();

            return cleaned.Any(char.IsLetterOrDigit) ? cleaned : string.Empty;
        }

        public static bool IsWhisperArtifact(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return WatermarkClauseRegex.IsMatch(text) || KnownWatermarkRegex.IsMatch(text);
        }

        private static string NormalizeForComparison(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            bool lastWasSpace = true;
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                    lastWasSpace = false;
                }
                else if (char.IsWhiteSpace(c) && !lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            return sb.ToString().Trim();
        }

        public static bool IsHallucination(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            string trimmed = text.Trim();
            string cleaned = StripWhisperArtifacts(trimmed);
            if (string.IsNullOrWhiteSpace(cleaned)) return true;

            string stripped = NormalizeForComparison(cleaned);
            if (stripped.Length == 0) return true;

            if (StandaloneClichéSet.Contains(stripped)) return true;

            // Repeated characters check (e.g. "ааааааа", "......", "------")
            if (stripped.Length > 3)
            {
                bool allSame = true;
                char first = stripped[0];
                for (int i = 1; i < stripped.Length; i++)
                {
                    if (stripped[i] != first)
                    {
                        allSame = false;
                        break;
                    }
                }
                if (allSame) return true;
            }

            // Repetition detector: if >60% of words are the same word, it's hallucination
            // e.g. "спасибо спасибо спасибо спасибо" or "thank you thank you thank you"
            var words = stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 3)
            {
                var wordCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var w in words)
                {
                    if (wordCounts.ContainsKey(w))
                        wordCounts[w]++;
                    else
                        wordCounts[w] = 1;
                }

                int maxCount = 0;
                foreach (var kv in wordCounts)
                {
                    if (kv.Value > maxCount) maxCount = kv.Value;
                }

                if ((double)maxCount / words.Length > 0.60)
                    return true;

                // N-gram (2-word & 3-word phrase) repetition detector
                // Catches loops like "I don't know I don't know I don't know"
                for (int n = 2; n <= 3; n++)
                {
                    if (words.Length >= n * 3)
                    {
                        var ngramCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        int maxNgram = 0;
                        for (int i = 0; i <= words.Length - n; i++)
                        {
                            string ngram = string.Join(" ", words, i, n);
                            ngramCounts[ngram] = ngramCounts.TryGetValue(ngram, out int c) ? c + 1 : 1;
                            if (ngramCounts[ngram] > maxNgram) maxNgram = ngramCounts[ngram];
                        }
                        if (maxNgram >= 3 && (double)(maxNgram * n) / words.Length > 0.55)
                            return true;
                    }
                }
            }

            // Space-less / CJK / Character-block repetition detector
            // Catches Whisper loops in Japanese/Chinese/Korean without spaces (e.g. "ありがとうありがとうありがとう")
            string noSpace = new string(stripped.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (noSpace.Length >= 6)
            {
                for (int patLen = 2; patLen <= Math.Min(14, noSpace.Length / 3); patLen++)
                {
                    for (int start = 0; start <= noSpace.Length - patLen * 3; start++)
                    {
                        ReadOnlySpan<char> pattern = noSpace.AsSpan(start, patLen);
                        int consecutiveRepeats = 1;
                        int pos = start + patLen;
                        while (pos + patLen <= noSpace.Length && noSpace.AsSpan(pos, patLen).Equals(pattern, StringComparison.Ordinal))
                        {
                            consecutiveRepeats++;
                            pos += patLen;
                        }
                        if (consecutiveRepeats >= 3 && (consecutiveRepeats * patLen >= noSpace.Length * 0.50 || consecutiveRepeats >= 4))
                            return true;
                    }
                }
            }

            // Musical note only strings (♪♪♪ or ♫♫)
            if (trimmed.All(c => c == '\u266a' || c == '\u266b' || c == '\u266c' || char.IsWhiteSpace(c) || char.IsPunctuation(c)))
                return true;

            return false;
        }

        /// <summary>
        /// Cleans LLM translation output to strip thinking blocks, meta prefixes, history bracket echoes,
        /// and trailing translator commentary.
        /// </summary>
        public static string CleanTranslationOutput(string? rawTranslation, string? originalText = null)
        {
            if (string.IsNullOrWhiteSpace(rawTranslation)) return string.Empty;

            string result = rawTranslation.Trim();

            // 1. Strip <think>...</think> blocks or unclosed <think> tags
            while (result.Contains("<think>", StringComparison.OrdinalIgnoreCase))
            {
                int startIdx = result.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
                int endIdx = result.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
                if (endIdx != -1 && endIdx > startIdx)
                {
                    result = (result.Substring(0, startIdx) + result.Substring(endIdx + 8)).Trim();
                }
                else
                {
                    result = result.Substring(0, startIdx).Trim();
                    break;
                }
            }

            // 3. Strip common LLM meta-prefixes
            string[] prefixes =
            {
                "Translation:", "Translated text:", "Here is the translation:", "Output:",
                "Terjemahan:", "Hasil terjemahan:", "Перевод:", "Переклад:"
            };
            foreach (var prefix in prefixes)
            {
                if (result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(prefix.Length).Trim();
                }
            }

            // 4. Strip accidental wrapping brackets [ ... ] or quotes "..." if the original didn't have them
            bool origBracketed = !string.IsNullOrEmpty(originalText) && originalText.Trim().StartsWith("[") && originalText.Trim().EndsWith("]");
            if (!origBracketed && result.Length >= 2 && result.StartsWith("[") && result.EndsWith("]") && !result.Substring(1, result.Length - 2).Contains("["))
            {
                result = result.Substring(1, result.Length - 2).Trim();
            }

            bool origQuoted = !string.IsNullOrEmpty(originalText) && (originalText.Trim().StartsWith("\"") || originalText.Trim().StartsWith("«"));
            if (!origQuoted && result.Length >= 2 &&
                ((result.StartsWith("\"") && result.EndsWith("\"")) || (result.StartsWith("“") && result.EndsWith("”"))))
            {
                result = result.Substring(1, result.Length - 2).Trim();
            }

            // 5. Strip trailing "(Note: ...)" or "[Note: ...]" translator commentary
            int noteIdx = result.IndexOf("(Note:", StringComparison.OrdinalIgnoreCase);
            if (noteIdx < 0) noteIdx = result.IndexOf("[Note:", StringComparison.OrdinalIgnoreCase);
            if (noteIdx < 0) noteIdx = result.IndexOf("(Translator's note", StringComparison.OrdinalIgnoreCase);
            if (noteIdx > 0)
            {
                result = result.Substring(0, noteIdx).Trim();
            }

            return result;
        }
    }
}
