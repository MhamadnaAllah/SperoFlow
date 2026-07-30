from __future__ import annotations

import asyncio
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from speroflow_ai.models.journal import JournalReflectionRequest
from speroflow_ai.services.journal_reflection import JournalReflectionService


class JournalReflectionServiceTests(unittest.TestCase):
    def _request(self) -> JournalReflectionRequest:
        return JournalReflectionRequest.model_validate(
            {
                "currentEntry": {
                    "content": "I felt tired after work, but I was grateful for a quiet walk.",
                    "mood": "Tired",
                },
                "priorEntries": [
                    {"content": "I made time for breakfast before a busy day.", "mood": "Calm"}
                ],
            }
        )

    def test_keyword_mode_uses_bounded_deterministic_reflection(self) -> None:
        settings = SimpleNamespace(router_provider="keyword", llm_provider="bedrock", llm_model="unused")

        result = asyncio.run(JournalReflectionService(settings).analyze(self._request()))

        self.assertTrue(any(e.lower() == "tired" for e in result.emotions))
        self.assertLessEqual(len(result.emotions), 6)
        self.assertLessEqual(len(result.feedback), 600)
        self.assertLessEqual(len(result.progress_summary), 600)

    def test_valid_model_json_is_normalized(self) -> None:
        settings = SimpleNamespace(router_provider="bedrock", llm_provider="bedrock", llm_model="test-model")
        model_response = '{"emotions":["hopeful","Hopeful"],"feedback":"You noticed a useful choice.","progressSummary":"Keep noticing what supports that choice."}'

        with patch(
            "speroflow_ai.services.journal_reflection.invoke_bedrock",
            new=AsyncMock(return_value=model_response),
        ):
            result = asyncio.run(JournalReflectionService(settings).analyze(self._request()))

        self.assertTrue(any(e.lower() == "hopeful" for e in result.emotions))
        self.assertEqual("Keep noticing what supports that choice.", result.progress_summary)

    def test_invalid_model_output_falls_back_without_journal_logging(self) -> None:
        settings = SimpleNamespace(router_provider="bedrock", llm_provider="bedrock", llm_model="test-model")

        with patch(
            "speroflow_ai.services.journal_reflection.invoke_bedrock",
            new=AsyncMock(return_value="not-json"),
        ):
            result = asyncio.run(JournalReflectionService(settings).analyze(self._request()))

        self.assertTrue(result.emotions)
        self.assertIn("patterns", result.progress_summary)


if __name__ == "__main__":
    unittest.main()