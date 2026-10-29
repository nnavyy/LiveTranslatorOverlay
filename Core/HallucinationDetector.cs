using System;
using System.Collections.Generic;
using System.Linq;

namespace LiveTranslatorOverlay.Core
{
    public static class HallucinationDetector
    {
        private static readonly string[] SubstringBlacklist =
        {
            // Subtitle credits & author tags
            "dimatorzok", "dima torzok", "amara.org", "opensubtitles", "subscene",
            "subtitles by", "subtitle by", "translated by", "translation by",
            "captions by", "transcript by", "sync and", "encoded by",
            "created by", "produced by", "presented by",
            "terjemahan oleh", "diterjemahkan oleh", "subtitel oleh",
            "\u0441\u0443\u0431\u0442\u0438\u0442\u0440",         // субтитр (субтитры, субтитров, etc.)
            "\u0440\u0435\u0434\u0430\u043a\u0442\u043e\u0440 \u0441\u0443\u0431\u0442\u0438\u0442\u0440", // редактор субтитр
            "\u043a\u043e\u0440\u0440\u0435\u043a\u0442\u043e\u0440",         // корректор
            "\u043f\u0435\u0440\u0435\u0432\u043e\u0434 \u0441\u0443\u0431\u0442\u0438\u0442\u0440", // перевод субтитр
            "\u5b57\u5e55",                           // 字幕 (Chinese: subtitles)

            // Continuation & endings (hallucinated on pauses)
            "\u043f\u0440\u043e\u0434\u043e\u043b\u0436\u0435\u043d\u0438\u0435 \u0441\u043b\u0435\u0434\u0443\u0435\u0442", // продолжение следует (to be continued)
            "to be continued", "the end",
            "\u043a\u043e\u043d\u0435\u0446", // конец (the end)
            "\u5b8c",                                 // 完 (Chinese: the end)
            "\u7d42\u308f\u308a",                     // 終わり (Japanese: the end)

            // Thanks for watching / Subscribe hallucinations
            "thank you for watching", "thanks for watching",
            "thanks for listening", "thank you for listening",
            "thanks for the attention",
            "\u0441\u043f\u0430\u0441\u0438\u0431\u043e \u0437\u0430 \u0432\u043d\u0438\u043c\u0430\u043d\u0438\u0435", // спасибо за внимание
            "\u0441\u043f\u0430\u0441\u0438\u0431\u043e \u0437\u0430 \u043f\u0440\u043e\u0441\u043c\u043e\u0442\u0440", // спасибо за просмотр
            "please subscribe", "subscribe to my channel", "like and subscribe",
            "\u043f\u043e\u0434\u043f\u0438\u0441\u044b\u0432\u0430\u0439\u0442\u0435\u0441\u044c", // подписывайтесь
            "\u043f\u043e\u0434\u043f\u0438\u0441\u044b\u0432\u0430", // подписыва
            "terima kasih telah menonton", "terima kasih sudah menonton", "jangan lupa subscribe",
            "\u611f\u8c22\u89c2\u770b",               // 感谢观看 (Chinese: thanks for watching)
            "\u8acb\u8a02\u95b1",                     // 請訂閱 (Chinese Traditional: subscribe)
            "\u8bf7\u8ba2\u9605",                     // 请订阅 (Chinese Simplified: subscribe)

            // Prompt & instruction echoes (Whisper hallucinating instructions during songs/music)
            "日本語の会話を正確に書き起こしてください",
            "会話の書き起こし",
            "書き起こし",
            "正確に書き起こして",
            "transcribe spoken conversation directly",
            "accurate transcription of spoken conversation",
            "please transcribe the japanese conversation accurately",
            "please transcribe",
            "accurate transcription",
            "transcribe the conversation",
            "transcribe spoken conversation",
            "transkripsikan percakapan",
            "точная транскрипция",
            "请准确转录",
            "대화를 정확하게",
            "نسخ المحادثة",

            // Noise tags, music, and song markers
            "[blank_audio]", "[music]", "(music)", "music playing", "applause",
            "[silence]", "[noise]", "(applause)", "[inaudible]",
            "[singing]", "(singing)", "[instrumental]", "(instrumental)",
            "[音楽]", "(音楽)", "【音楽】", "[bgm]", "(bgm)",
            "[歌]", "(歌)", "歌唱", "演奏", "効果音", "歓声",

            // Musical note hallucinations (common Whisper artifact on background music)
            "\u266a", "\u266b", "\u266c", "\u266d", "\u266e", "\u266f",
            "\u2669",

            // Japanese & Korean common hallucinations
            "\u3054\u8996\u8074",                     // ご視聴 (watching)
            "\u30c1\u30e3\u30f3\u30cd\u30eb\u767b\u9332", // チャンネル登録 (subscribe)
            "\u3064\u3065\u304f",                     // つづく (to be continued)
            "\u304a\u75b2\u308c\u69d8",               // お疲れ様 (otsukaresama - common hallucination)
            "\uc2dc\uccad",                           // 시청 (viewing)
            "\uad6c\ub3c5",                           // 구독 (subscribe)
            "\uac10\uc0ac\ud569\ub2c8\ub2e4",         // 감사합니다 (thank you - Korean)

            // Thai common hallucinations
            "\u0e02\u0e2d\u0e1a\u0e04\u0e38\u0e13",   // ขอบคุณ (thank you - Thai)
            "\u0e15\u0e34\u0e14\u0e15\u0e32\u0e21",   // ติดตาม (follow/subscribe - Thai)

            // Arabic common hallucinations
            "\u0634\u0643\u0631\u0627 \u0644\u0643\u0645",   // شكرا لكم (thank you - Arabic)
            "\u0627\u0634\u062a\u0631\u0643 \u0641\u064a \u0627\u0644\u0642\u0646\u0627\u0629", // اشترك في القناة (subscribe - Arabic)
            "\u062a\u0631\u062c\u0645\u0629 \u0646\u0627\u0646\u0633\u064a", // ترجمة نانسي

            // Ukrainian common hallucinations
            "дякую за перегляд", "дякуємо за перегляд", "підписуйтесь на канал",
            "підпишіться на канал", "далі буде", "субтитри зробив", "редактор субтитрів",

            // Azerbaijani & Uzbek common hallucinations
            "izlədiyiniz üçün təşəkkürlər", "kanala abunə olun", "altyazı",
            "tomosha qilganingiz uchun rahmat", "obuna bo'ling",

            // Georgian & Mongolian common hallucinations
            "მადლობა ყურებისთვის", "გამოიწერეთ არხი", "სუბტიტრები",
            "үзсэнд баярлалаа", "хадмал орчуулга",

            // Balkan languages common hallucinations (Serbian, Croatian, Bosnian, Bulgarian, Romanian, Greek, Albanian, Macedonian, Slovenian)
            "hvala na gledanju", "хвала на гледању", "pretplatite se", "претплатите се",
            "благодаря за гледането", "абонирайте се",
            "mulțumesc pentru vizionare", "abonați-vă", "subtitrare realizată",
            "ευχαριστώ για την παρακολούθηση", "εγγραφείτε στο κανάλι", "υπότιτλοι",
            "faleminderit për shikimin", "abonohuni në kanal",
            "ви благодариме за гледањето", "претплатете се",
            "hvala za ogled", "naročite se na kanal",

            // LLM meta-commentary hallucinations
            "here is the translation", "as an ai language model", "i cannot translate",
            "no text to translate", "please provide the text"
        };

        private static readonly string[] ExactMatches =
        {
            // Exact standalone hallucination words produced when hearing silence/hiss
            "thank you", "thanks", "thank you very much", "thanks a lot",
            "okay", "ok", "oh",
            "hmm", "mm", "mmm", "mhm", "uh", "um", "ah", "huh",
            "yeah", "yes", "no", "yep", "nope",
            "spasiba", "spasibo",
            "\u0441\u043f\u0430\u0441\u0438\u0431\u043e",             // спасибо
            "\u0441\u043f\u0430\u0441\u0438\u0431\u043e.",            // спасибо.
            "\u0441\u043f\u0430\u0441\u0438\u0431\u043e!",            // спасибо!
            "\u0431\u043e\u043b\u044c\u0448\u043e\u0435 \u0441\u043f\u0430\u0441\u0438\u0431\u043e", // большое спасибо
            "\u0434\u0430",                           // да (yes - Russian)
            "\u043d\u0435\u0442",                     // нет (no - Russian)
            "terima kasih", "terimakasih", "terima kasih banyak",
            "bye", "bye bye", "goodbye", "bye-bye",
            "you", "you.",
            ".", "..", "...", "!", "?",
            "\u3042\u308a\u304c\u3068\u3046",         // ありがとう (arigatou)
            "\u3042\u308a\u304c\u3068\u3046\u3054\u3056\u3044\u307e\u3059", // ありがとうございます
            "\u306f\u3044",                           // はい (hai - yes)
            "\u3044\u3044\u3048",                     // いいえ (iie - no)
            "音楽", "music", "bgm", "singing", "song",
            "日本語の会話を正確に書き起こしてください",
            "会話の書き起こし",
            "書き起こし",
            "正確に書き起こしてください",
            // Ukrainian, Balkan, Caucasian, Turkic standalone silence hallucinations
            "дякую", "дякую.", "дякую!",
            "təşəkkürlər", "rahmat", "მადლობა", "баярлалаа",
            "hvala", "хвала", "благодаря", "mulțumesc", "ευχαριστώ", "faleminderit",
            "субтитри", "субтитры", "subtitles", "subtitle", "titlovi", "altyazı",
            "mbc 뉴스", "kbs 뉴스", "sbs 뉴스"
        };

        public static bool IsHallucination(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            string trimmed = text.Trim();
            string lower = trimmed.ToLowerInvariant();

            // Punctuation and whitespace stripping for exact comparison
            string stripped = new string(lower.Where(c => !char.IsPunctuation(c) && c != '\u266a' && c != '\u266b').ToArray()).Trim();
            if (stripped.Length == 0) return true;

            // Short-text rejection: transcripts <= 2 meaningful characters are noise
            if (stripped.Length <= 2) return true;

            // Check exact stripped matches (e.g. "спасибо", "thank you", "spasiba")
            foreach (var match in ExactMatches)
            {
                string matchStripped = new string(match.Where(c => !char.IsPunctuation(c)).ToArray()).Trim();
                if (stripped == matchStripped || lower == match)
                    return true;
            }

            // Check substring blacklist (e.g. "Субтитры создавал DimaTorzok", "Продолжение следует...")
            foreach (var kw in SubstringBlacklist)
            {
                if (lower.Contains(kw))
                    return true;
            }

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

            // 2. Strip accidental history format echoes like "[source] -> [target]"
            if (result.Contains("->"))
            {
                int arrowIdx = result.LastIndexOf("->", StringComparison.Ordinal);
                string afterArrow = result.Substring(arrowIdx + 2).Trim();
                if (!string.IsNullOrWhiteSpace(afterArrow))
                    result = afterArrow;
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
