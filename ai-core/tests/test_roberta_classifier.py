import os
import sys
import unittest

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "../src")))

from speroflow_ai.services.roberta_classifier import RoBERTaEmotionClassifier


class TestRoBERTaEmotionClassifier(unittest.TestCase):
    def setUp(self) -> None:
        self.classifier = RoBERTaEmotionClassifier(model_dir="/nonexistent/model/path")

    def test_empty_input_returns_neutral(self) -> None:
        result = self.classifier.classify("")
        self.assertEqual(result, ["Neutral"])

    def test_heuristic_fallback_detects_emotions(self) -> None:
        res1 = self.classifier.classify("I feel so happy and excited today!")
        self.assertIn("Joy", res1)

        res2 = self.classifier.classify("I am feeling angry and frustrated with this issue.")
        self.assertIn("Anger", res2)

        res3 = self.classifier.classify("Very worried and anxious about the deadline.")
        self.assertIn("Fear", res3)

    def test_merge_emotions_deduplicates(self) -> None:
        from speroflow_ai.services.journal_reflection import JournalReflectionService

        merged = JournalReflectionService._merge_emotions(["Joy", "Calm"], ["joy", "Grateful"])
        self.assertEqual(merged, ["Joy", "Calm", "Grateful"])


if __name__ == "__main__":
    unittest.main()
