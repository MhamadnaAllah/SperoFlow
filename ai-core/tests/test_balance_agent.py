from __future__ import annotations

import unittest
from datetime import datetime, timedelta, timezone
from types import SimpleNamespace
from uuid import UUID

from speroflow_ai.models.balance import BalanceEvaluationRequest
from speroflow_ai.services.balance_agent import BalanceAgent


MANAGER_ROLE_ID = UUID("019f1fb5-4299-7062-a4fb-5f6cc0fcb201")
PARENT_ROLE_ID = UUID("019f1fb5-4299-7062-a4fb-5f6cc0fcb202")


class BalanceAgentTests(unittest.TestCase):
    def setUp(self) -> None:
        self.settings = SimpleNamespace(
            balance_max_lookback_days=31,
            balance_min_classified_tasks=3,
            balance_min_classification_coverage=0.60,
            balance_medium_concentration_threshold=0.70,
            balance_high_concentration_threshold=0.85,
            balance_suggestion_duration_minutes=15,
        )

    def _request(self, *, unclassified: int = 0) -> BalanceEvaluationRequest:
        end = datetime(2026, 7, 21, 12, 0, tzinfo=timezone.utc)
        return BalanceEvaluationRequest.model_validate(
            {
                "subject_id": "019f1fb5-4299-7062-a4fb-5f6cc0fcb200",
                "request_id": "019f1fb5-4299-7062-a4fb-5f6cc0fcb203",
                "window_start": (end - timedelta(days=7)).isoformat(),
                "window_end": end.isoformat(),
                "active_roles": [
                    {
                        "role_id": str(MANAGER_ROLE_ID),
                        "role_name": "Manager",
                        "role_category": "external",
                        "life_area": "work",
                    },
                    {
                        "role_id": str(PARENT_ROLE_ID),
                        "role_name": "Parent",
                        "role_category": "external",
                        "life_area": "family",
                    },
                ],
                "role_workloads": [
                    {
                        "role_id": str(MANAGER_ROLE_ID),
                        "completed_task_count": 3,
                        "completed_minutes": 90,
                    }
                ],
                "unclassified_completed_task_count": unclassified,
                "wellbeing_signal": "unknown",
            }
        )

    def test_targets_the_explicit_neglected_role(self) -> None:
        result = BalanceAgent(self.settings).evaluate(self._request())

        self.assertEqual("high", result.risk_level)
        self.assertIsNotNone(result.suggestion)
        self.assertEqual(PARENT_ROLE_ID, result.suggestion.role_id)
        self.assertEqual("Parent", result.suggestion.role_name)
        self.assertEqual("family", result.suggestion.life_area)
        self.assertIn(PARENT_ROLE_ID, result.neglected_role_ids)

    def test_unclassified_work_reduces_snapshot_quality(self) -> None:
        result = BalanceAgent(self.settings).evaluate(self._request(unclassified=3))

        self.assertEqual("insufficient_data", result.risk_level)
        self.assertEqual("insufficient_classification", result.data_quality)
        self.assertIsNone(result.suggestion)


if __name__ == "__main__":
    unittest.main()