import unittest
from metrics import summarize, validate_splits


class MetricsTests(unittest.TestCase):
    def test_micro_word_errors_and_missed_speech(self):
        rows = [dict(language="en", kind="speech", reference="One two three.", prediction="one four three"),
                dict(language="en", kind="speech", reference="Yes", prediction="")]
        score = summarize(rows)["speech"]["en"]
        self.assertEqual(score["wordErrors"], 2)
        self.assertEqual(score["referenceWords"], 4)
        self.assertEqual(score["wer"], 0.5)
        self.assertEqual(score["falseRejections"], 1)

    def test_non_speech_is_not_counted_as_zero_word_error(self):
        score = summarize([dict(language="ru", kind="non_speech", reference="", prediction="Спасибо!")])
        self.assertEqual(score["nonSpeech"]["emissionRate"], 1)
        self.assertEqual(score["speech"], {})

    def test_languages_and_legitimate_closing_phrase_stay_separate(self):
        rows = [dict(language="en", kind="speech", reference="Thank you for watching!", prediction="Thank you for watching."),
                dict(language="ru", kind="speech", reference="Нет", prediction="Да")]
        score = summarize(rows)["speech"]
        self.assertEqual(score["en"]["wer"], 0)
        self.assertEqual(score["ru"]["wer"], 1)

    def test_no_examples_is_unknown(self):
        self.assertIsNone(summarize([])["nonSpeech"]["emissionRate"])

    def test_group_and_hash_leakage_fail(self):
        for field in ("group", "sha256"):
            a = dict(id="a", split="train", group="a", sha256="a")
            b = dict(id="b", split="test", group="b", sha256="b")
            b[field] = a[field]
            with self.assertRaises(ValueError):
                validate_splits([a, b])

    def test_duplicate_ids_fail(self):
        with self.assertRaises(ValueError):
            validate_splits([dict(id="a", split="train", group="a", sha256="a")] * 2)


if __name__ == "__main__":
    unittest.main()
