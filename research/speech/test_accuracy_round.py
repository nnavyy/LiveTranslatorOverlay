import copy
import unittest
from accuracy_round_protocol import select_language, targets


class AccuracyRoundTests(unittest.TestCase):
    def setUp(self):
        self.base=dict(speech={'en':dict(wer=.0367,falseRejections=0)},nonSpeech=dict(emittedClips=0))

    def test_actual_english_gain_can_win_with_unchanged_russian(self):
        better=copy.deepcopy(self.base)
        better['speech']['en']['wer']=.0267
        result=select_language('en',self.base,{'current':self.base,'new':better})
        self.assertEqual(result['selected'],'new')
        self.assertTrue(result['improved'])

    def test_rejects_blank_regression_and_retains_baseline_on_tie(self):
        better=copy.deepcopy(self.base)
        better['speech']['en'].update(wer=.02,falseRejections=1)
        result=select_language('en',self.base,{'current':self.base,'tie':self.base,'unsafe':better})
        self.assertEqual(result['selected'],'current')

    def test_requested_one_point_targets_do_not_imply_three_percent_mean(self):
        score=dict(speech={'en':dict(wer=.0267),'ru':dict(wer=.047)})
        result=targets(score)
        self.assertTrue(result['russianAtMost4_70'] and result['englishAtMost2_67'])
        self.assertAlmostEqual(result['macroMeanWer'],.03685)
        self.assertFalse(result['meanAtMost3'])
        self.assertFalse(result['bothAtMost2'])

    def test_rejects_control_regression_even_when_wer_improves(self):
        better=copy.deepcopy(self.base)
        better['speech']['en']['wer']=.01
        better['nonSpeech']['emittedClips']=1
        result=select_language('en',self.base,{'current':self.base,'new':better})
        self.assertFalse(result['eligibility']['new'])
        self.assertEqual(result['selected'],'current')


if __name__=='__main__':
    unittest.main()
