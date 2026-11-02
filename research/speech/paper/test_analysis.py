"""Known-result and paired-input checks for manuscript statistics."""
import unittest
from analyze import bootstrap_wer, mcnemar_exact, quantile, statistics, wilson


class AnalysisTests(unittest.TestCase):
    def test_wilson_zero_and_all_events(self):
        self.assertAlmostEqual(wilson(0, 3)[1], 0.5614970317550454)
        self.assertAlmostEqual(wilson(3, 3)[0], 1 - wilson(0, 3)[1])
        with self.assertRaises(ValueError):
            wilson(1, 0)

    def test_exact_paired_binomial_tails(self):
        self.assertEqual(mcnemar_exact(0, 0), 1)
        self.assertEqual(mcnemar_exact(0, 2), .5)
        self.assertEqual(mcnemar_exact(6, 0), .03125)
        self.assertEqual(mcnemar_exact(4, 4), 1)

    def test_interpolated_percentile(self):
        self.assertEqual(quantile([10, 0], .25), 2.5)

    def test_paired_reference_mismatch_rejected(self):
        row = {"id": "a", "kind": "speech", "language": "en", "referenceSha256": "x", "metrics": {"speech": {"en": {"wordErrors": 1, "referenceWords": 10}}}}
        with self.assertRaises(ValueError):
            bootstrap_wer([row], [dict(row, referenceSha256="y")], "en")

    def test_report_recomputed_from_clip_counts(self):
        result = statistics()
        self.assertEqual(result["wer"]["test"]["en"]["baselineErrors"], 20)
        self.assertEqual(result["wer"]["test"]["ru"]["resamples"], 256)
        self.assertEqual(result["gates"]["speech"]["paired"]["exactMcnemarP"], .5)
        self.assertEqual(result["samplePlanning"]["zeroEventsForOneSided95UpperBelow1Percent"], 299)


if __name__ == "__main__":
    unittest.main()
