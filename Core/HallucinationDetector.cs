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

            // Noise tags & markers
            "[blank_audio]", "[music]", "(music)", "music playing", "applause",
            "[silence]", "[noise]", "(applause)", "[inaudible]",

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
            "\u8c22\u8c22",                           // 谢谢 (xièxiè - thank you)
            "\u5bf9",                                 // 对 (duì - yes/right)
            "\u597d",                                 // 好 (hǎo - good/ok)
            "\ub124",                                 // 네 (ne - yes Korean)
        };

        public static bool IsHallucination(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            string trimmed = text.Trim();
            string lower = trimmed.ToLowerInvariant();

            // Punctuation and whitespace stripping for exact comparison
            string stripped = new string(lower.Where(c => !char.IsPunctuation(c) && c != '\u266a' && c != '\u266b').ToArray()).Trim();
            if (stripped.Length == 0) return true;

            // Short-text rejection: transcripts ≤ 2 meaningful characters are noise
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
            }

            // Musical note only strings (♪♪♪ or ♫♫)
            if (trimmed.All(c => c == '\u266a' || c == '\u266b' || c == '\u266c' || char.IsWhiteSpace(c) || char.IsPunctuation(c)))
                return true;

            return false;
        }
    }
}

