using System;
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
            "terjemahan oleh", "diterjemahkan oleh", "subtitel oleh",
            "\u0441\u0443\u0431\u0442\u0438\u0442\u0440",         // субтитр (субтитры, субтитров, etc.)
            "\u0440\u0435\u0434\u0430\u043a\u0442\u043e\u0440 \u0441\u0443\u0431\u0442\u0438\u0442\u0440", // редактор субтитр
            "\u043a\u043e\u0440\u0440\u0435\u043a\u0442\u043e\u0440",         // корректор
            "\u043f\u0435\u0440\u0435\u0432\u043e\u0434 \u0441\u0443\u0431\u0442\u0438\u0442\u0440", // перевод субтитр

            // Continuation & endings (hallucinated on pauses)
            "\u043f\u0440\u043e\u0434\u043e\u043b\u0436\u0435\u043d\u0438\u0435 \u0441\u043b\u0435\u0434\u0443\u0435\u0442", // продолжение следует (to be continued)
            "to be continued", "the end",
            "\u043a\u043e\u043d\u0435\u0446", // конец (the end)

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

            // Noise tags
            "[blank_audio]", "[music]", "(music)", "music playing", "applause",

            // Japanese & Korean common hallucinations
            "\u3054\u8996\u8074",                     // ご視聴 (watching)
            "\u30c1\u30e3\u30f3\u30cd\u30eb\u767b\u9332", // チャンネル登録 (subscribe)
            "\u3064\u3065\u304f",                     // つづく (to be continued)
            "\uc2dc\uccad",                           // 시청 (viewing)
            "\uad6c\ub3c5",                           // 구독 (subscribe)
        };

        private static readonly string[] ExactMatches =
        {
            // Exact standalone hallucination words produced when hearing silence/hiss
            "thank you", "thanks", "thank you very much", "thanks a lot",
            "spasiba", "spasibo",
            "\u0441\u043f\u0430\u0441\u0438\u0431\u043e",             // спасибо
            "\u0441\u043f\u0430\u0441\u0438\u0431\u043e.",            // спасибо.
            "\u0441\u043f\u0430\u0441\u0438\u0431\u043e!",            // спасибо!
            "\u0431\u043e\u043b\u044c\u0448\u043e\u0435 \u0441\u043f\u0430\u0441\u0438\u0431\u043e", // большое спасибо
            "terima kasih", "terimakasih", "terima kasih banyak",
            "bye", "bye bye", "goodbye",
            "you", "you.",
            ".", "..", "...", "!", "?",
            "\u3042\u308a\u304c\u3068\u3046",         // ありがとう (arigatou)
            "\u3042\u308a\u304c\u3068\u3046\u3054\u3016\u3044\u307e\u3059", // ありがとうございます
        };

        public static bool IsHallucination(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;

            string trimmed = text.Trim();
            string lower = trimmed.ToLowerInvariant();

            // Punctuation and whitespace stripping for exact comparison
            string stripped = new string(lower.Where(c => !char.IsPunctuation(c)).ToArray()).Trim();
            if (stripped.Length == 0) return true;

            // Check exact stripped matches (e.g. "спасибо", "thank you", "spasiba")
            foreach (var match in ExactMatches)
            {
                if (stripped == match || lower == match)
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

            return false;
        }
    }
}
