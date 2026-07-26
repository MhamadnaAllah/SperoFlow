"""Local ONNX DistilRoBERTa emotion classification service.

Provides fast (~12ms), CPU-bound multi-label emotion tagging for journaling.
"""

from __future__ import annotations

import logging
import os
from typing import Any

logger = logging.getLogger("speroflow.services.roberta_classifier")

# 7 fine-grained emotion labels from j-hartmann/emotion-english-distilroberta-base
DEFAULT_EMOTION_MAP = {
    "joy": "Joy",
    "sadness": "Sadness",
    "anger": "Anger",
    "fear": "Fear",
    "surprise": "Surprise",
    "disgust": "Disgust",
    "neutral": "Neutral",
}


class RoBERTaEmotionClassifier:
    """ONNX-accelerated emotion classifier running locally on CPU."""

    def __init__(self, model_dir: str | None = None) -> None:
        self.model_dir = model_dir or os.getenv("ROBERTA_MODEL_DIR", "/app/models/roberta")
        self._model: Any = None
        self._tokenizer: Any = None
        self._is_initialized = False

    def initialize(self) -> bool:
        """Lazily initialize ONNX runtime pipeline if model weights exist."""
        if self._is_initialized:
            return True
        try:
            from optimum.onnxruntime import ORTModelForSequenceClassification
            from transformers import AutoTokenizer

            if os.path.exists(self.model_dir):
                logger.info("Loading ONNX DistilRoBERTa model from local path: %s", self.model_dir)
                self._tokenizer = AutoTokenizer.from_pretrained(self.model_dir)
                self._model = ORTModelForSequenceClassification.from_pretrained(self.model_dir)
                self._is_initialized = True
                return True
            else:
                logger.info("ONNX model directory %s not found. RoBERTa fallback active.", self.model_dir)
                return False
        except Exception as exc:
            logger.warning("Failed to initialize ONNX RoBERTa model (%s): %s", type(exc).__name__, exc)
            return False

    def classify(self, text: str, threshold: float = 0.20) -> list[str]:
        """Classify input text into 1-6 titlecase emotion labels."""
        if not text or not text.strip():
            return ["Neutral"]

        if not self.initialize():
            return self._heuristic_fallback(text)

        try:
            import torch
            inputs = self._tokenizer(text[:512], return_tensors="pt", truncation=True, padding=True)
            outputs = self._model(**inputs)
            logits = outputs.logits
            probs = torch.nn.functional.softmax(logits, dim=-1)[0]

            id2label = getattr(self._model.config, "id2label", {})
            emotions: list[str] = []

            for idx, prob in enumerate(probs):
                score = float(prob)
                if score >= threshold:
                    raw_label = id2label.get(idx, "").lower()
                    formatted = DEFAULT_EMOTION_MAP.get(raw_label, raw_label.capitalize())
                    if formatted and formatted not in emotions:
                        emotions.append(formatted)

            return emotions if emotions else ["Neutral"]
        except Exception as exc:
            logger.warning("RoBERTa inference error (%s): %s", type(exc).__name__, exc)
            return self._heuristic_fallback(text)

    @staticmethod
    def _heuristic_fallback(text: str) -> list[str]:
        """Keyword-based deterministic fallback when ONNX model is uninitialized."""
        lowered = text.lower()
        emotions = []
        if any(w in lowered for w in ["happy", "great", "joy", "excited", "good", "calm", "peace"]):
            emotions.append("Joy")
        if any(w in lowered for w in ["sad", "down", "tired", "depressed", "unhappy"]):
            emotions.append("Sadness")
        if any(w in lowered for w in ["angry", "frustrated", "annoyed", "mad"]):
            emotions.append("Anger")
        if any(w in lowered for w in ["fear", "scared", "worried", "anxious", "stress"]):
            emotions.append("Fear")
        if any(w in lowered for w in ["surprised", "unexpected", "amazed"]):
            emotions.append("Surprise")

        return emotions if emotions else ["Neutral"]
