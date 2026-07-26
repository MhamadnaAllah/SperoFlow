from __future__ import annotations

import asyncio
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from speroflow_ai.models.eisenhower import EisenhowerClassificationRequest
from speroflow_ai.services.eisenhower_classifier import EisenhowerClassifier


class EisenhowerClassifierTests(unittest.TestCase):
    def _request(self, title: str = "Finish the data engineering capstone") -> EisenhowerClassificationRequest:
        return EisenhowerClassificationRequest.model_validate(
            {
                "task": {
                    "title": title,
                    "description": "Prepare the final project for the course.",
                    "lifeArea": "learning",
                    "dueAt": "2026-07-21T12:00:00Z",
                    "estimatedMinutes": 90,
                },
                "goals": [
                    {
                        "title": "Complete data engineering course",
                        "description": "Finish the learning path and capstone.",
                        "lifeArea": "learning",
                    }
                ],
                "journals": [{"content": "I want to make steady progress today.", "mood": "focused"}],
                "insights": [{"feedback": "Pacing work has helped.", "progressSummary": "You are building consistency."}],
            }
        )

    def test_keyword_fallback_uses_active_goals_and_urgency(self) -> None:
        settings = SimpleNamespace(router_provider="keyword", llm_provider="bedrock", llm_model="unused")

        result = asyncio.run(EisenhowerClassifier(settings).classify(self._request()))

        self.assertEqual("q1", result.suggested_quadrant)
        self.assertGreaterEqual(result.confidence, 0.4)

    def test_keyword_fallback_avoids_urgent_and_unimportant_as_q1(self) -> None:
        settings = SimpleNamespace(router_provider="keyword", llm_provider="bedrock", llm_model="unused")

        result = asyncio.run(EisenhowerClassifier(settings).classify(self._request("Reply to an urgent promotional email")))

        self.assertEqual("q3", result.suggested_quadrant)

    def test_model_response_is_validated(self) -> None:
        settings = SimpleNamespace(router_provider="bedrock", llm_provider="bedrock", llm_model="test-model")
        response = '{"suggestedQuadrant":"q2","confidence":0.88,"rationale":"This supports an active learning goal without an immediate deadline."}'

        with patch(
            "speroflow_ai.services.eisenhower_classifier.invoke_bedrock",
            new=AsyncMock(return_value=response),
        ):
            result = asyncio.run(EisenhowerClassifier(settings).classify(self._request("Practice the data pipeline exercises")))

        self.assertEqual("q2", result.suggested_quadrant)
        self.assertEqual(0.88, result.confidence)


if __name__ == "__main__":
    unittest.main()