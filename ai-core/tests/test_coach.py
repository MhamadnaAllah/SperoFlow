"""Tests for the private Coach service using standard unittest."""

import unittest
from speroflow_ai.config import Settings
from speroflow_ai.services.coach import CoachService


class CoachServiceTests(unittest.IsolatedAsyncioTestCase):
    async def test_coach_fallback_habit_query(self) -> None:
        settings = Settings(router_provider="keyword", llm_provider="keyword")
        service = CoachService(settings)

        payload = {
            "user_message": "I want to build a better daily morning routine",
            "active_roles": [{"name": "Personal", "defaultLifeArea": "personal"}],
            "open_tasks": [],
            "active_habits": [],
        }

        res = await service.respond(payload)
        self.assertTrue(
            "habit" in res.message_content.lower() or "routine" in res.message_content.lower()
        )
        self.assertGreaterEqual(len(res.observations), 1)
        self.assertTrue(any(obs.scope == "HabitPattern" for obs in res.observations))
        self.assertGreaterEqual(len(res.proposals), 1)
        self.assertEqual(res.proposals[0].kind, "CreateHabit")

    async def test_coach_fallback_overwhelmed_query(self) -> None:
        settings = Settings(router_provider="keyword", llm_provider="keyword")
        service = CoachService(settings)

        payload = {
            "user_message": "I am feeling overwhelmed with too many tasks",
            "active_roles": [],
            "open_tasks": [],
            "active_habits": [],
        }

        res = await service.respond(payload)
        self.assertTrue(
            "quadrant" in res.message_content.lower() or "workload" in res.message_content.lower()
        )
        self.assertGreaterEqual(len(res.observations), 1)
        self.assertEqual(res.observations[0].scope, "QuadrantImbalance")
        self.assertGreaterEqual(len(res.proposals), 1)
        self.assertEqual(res.proposals[0].kind, "CreateTask")


if __name__ == "__main__":
    unittest.main()
