import copy
import unittest
from specialist_protocol import apply_gate, choose, goals, VAD_SHA256


class SpecialistTests(unittest.TestCase):
    def setUp(self):
        self.base = dict(speech={lang: dict(wer=wer,falseRejections=0)
                                for lang,wer in (("en",.04),("ru",.24))},nonSpeech=dict(emittedClips=0))

    def test_targets_distinguish_five_percent_mean(self):
        score=copy.deepcopy(self.base)
        score['speech']['ru']['wer']=.07
        score['speech']['en']['wer']=.03
        result=goals(score)
        self.assertTrue(result['russianAtMost7'] and result['englishAtMost3'])
        self.assertAlmostEqual(result['macroMeanWer'],.05)
        self.assertFalse(result['macroMeanAtMost3'])

    def test_beam_search_cannot_trade_english_for_russian(self):
        good,bad=copy.deepcopy(self.base),copy.deepcopy(self.base)
        good['speech']['ru']['wer']=bad['speech']['ru']['wer']=.08
        bad['speech']['en']['wer']=.041
        result=choose(self.base,{'1':good,'5':bad})
        self.assertEqual(result['englishBeams'],1)
        self.assertTrue(result['developmentEligible'])

    def test_gate_rejections_remain_in_predictions(self):
        row=dict(id='x',split='dev',sha256='audio',prediction='words',reference='spoken words')
        gate=dict(audioSha256='audio',split='dev',neuralGate=False,threshold=.5,vadSha256=VAD_SHA256)
        self.assertEqual(apply_gate([row],{'x':gate})[0]['prediction'],'')
        gate['audioSha256']='changed'
        with self.assertRaises(ValueError):
            apply_gate([row],{'x':gate})


if __name__=='__main__':
    unittest.main()
