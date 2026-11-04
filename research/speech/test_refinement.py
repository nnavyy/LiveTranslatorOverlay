import copy
import unittest
from expanded_utils import choose_refinement


class RefinementTests(unittest.TestCase):
    def setUp(self):
        self.parent = dict(speech={lang: dict(wer=wer, falseRejections=0)
            for lang, wer in (("ru", .24), ("en", .04))}, nonSpeech=dict(emittedClips=0))

    def test_accepts_russian_gain(self):
        candidate = copy.deepcopy(self.parent)
        candidate["speech"]["ru"]["wer"] = .22
        self.assertTrue(choose_refinement(self.parent, {"10": candidate})["developmentEligible"])

    def test_rejects_regressions_and_no_gain(self):
        for kind in ("same", "russian", "english", "controls", "blank"):
            candidate = copy.deepcopy(self.parent)
            if kind != "same":
                candidate["speech"]["ru"]["wer"] = .22
            if kind == "english":
                candidate["speech"]["en"]["wer"] = .041
            if kind == "russian":
                candidate["speech"]["ru"]["wer"] = .241
                candidate["speech"]["en"]["wer"] = .02
            if kind == "controls":
                candidate["nonSpeech"]["emittedClips"] = 1
            if kind == "blank":
                candidate["speech"]["ru"]["falseRejections"] = 1
            with self.subTest(kind=kind):
                self.assertFalse(choose_refinement(self.parent, {"10": candidate})["developmentEligible"])

    def test_eligible_candidate_wins_over_regressing_candidate(self):
        good, bad = copy.deepcopy(self.parent), copy.deepcopy(self.parent)
        good["speech"]["ru"]["wer"] = .23
        bad["speech"]["ru"]["wer"] = .20
        bad["speech"]["en"]["wer"] = .05
        result = choose_refinement(self.parent, {"10": good, "20": bad})
        self.assertEqual(result["checkpoint"], "10")
        self.assertTrue(result["developmentEligible"])


if __name__ == "__main__":
    unittest.main()
