from __future__ import annotations

import asyncio
import unittest
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

from speroflow_ai.models.role_discovery import RoleDiscoveryRequest
from speroflow_ai.services.role_discovery import RoleDiscoveryService


class RoleDiscoveryServiceTests(unittest.TestCase):
    def _request(self) -> RoleDiscoveryRequest:
        return RoleDiscoveryRequest.model_validate(
            {
                "existingRoles": ["Parent"],
                "signals": [
                    {"kind": "task", "label": "Prepare the team one-on-one notes", "lifeArea": "work"},
                    {"kind": "project", "label": "Plan the engineering team review", "lifeArea": "work"},
                    {"kind": "habit", "label": "Study leadership material", "lifeArea": "learning"},
                ],
            }
        )

    def test_keyword_fallback_requires_repeated_explicit_signals(self) -> None:
        settings = SimpleNamespace(router_provider="keyword", llm_provider="bedrock", llm_model="unused")

        result = asyncio.run(RoleDiscoveryService(settings).discover(self._request()))

        self.assertEqual(1, len(result.candidates))
        self.assertEqual("Manager", result.candidates[0].name)
        self.assertEqual([0, 1], result.candidates[0].evidence_indexes)

    def test_model_output_filters_existing_roles_and_invalid_evidence(self) -> None:
        settings = SimpleNamespace(router_provider="bedrock", llm_provider="bedrock", llm_model="test-model")
        model_response = '''{
          "candidates": [
            {"name":"Parent","lifeArea":"family","confidence":0.95,"evidenceIndexes":[0,1]},
            {"name":"Manager","lifeArea":"work","confidence":0.88,"evidenceIndexes":[0,1]}
          ]
        }'''

        with patch(
            "speroflow_ai.services.role_discovery.invoke_bedrock",
            new=AsyncMock(return_value=model_response),
        ):
            result = asyncio.run(RoleDiscoveryService(settings).discover(self._request()))

        self.assertEqual(["Manager"], [candidate.name for candidate in result.candidates])

    def test_invalid_model_output_uses_conservative_fallback(self) -> None:
        settings = SimpleNamespace(router_provider="bedrock", llm_provider="bedrock", llm_model="test-model")

        with patch(
            "speroflow_ai.services.role_discovery.invoke_bedrock",
            new=AsyncMock(return_value="not-json"),
        ):
            result = asyncio.run(RoleDiscoveryService(settings).discover(self._request()))

        self.assertEqual("Manager", result.candidates[0].name)


if __name__ == "__main__":
    unittest.main()