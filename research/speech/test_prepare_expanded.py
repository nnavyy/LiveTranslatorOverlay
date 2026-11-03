import unittest

from prepare_expanded import labels_by_filename


class FleursLabelsTests(unittest.TestCase):
    def test_opening_quote_keeps_the_next_recording_separate(self):
        labels = labels_by_filename(
            b'1\tfirst.wav\t"First sentence.\tfirst sentence\tf\t131520\tFEMALE\n'
            b'2\tnext.wav\tNext sentence."\tnext sentence\tn\t167040\tMALE\n'
        )
        self.assertEqual(labels["first.wav"][3:6], ["first sentence", "f", "131520"])
        self.assertEqual(labels["next.wav"][3:6], ["next sentence", "n", "167040"])
