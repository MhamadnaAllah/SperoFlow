from __future__ import annotations

import asyncio
import unittest
from datetime import date, datetime, time, timezone
from types import SimpleNamespace

from speroflow_ai.services.auto_scheduler import AutoSchedulerAgent, BookedEvent, TaskSpec, UserDailyContext


class AutoSchedulerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.settings = SimpleNamespace(
            scheduler_day_start="08:00",
            scheduler_day_end="12:00",
            scheduler_buffer_minutes=15,
            scheduler_min_slot_minutes=20,
            scheduler_q1_overflow=5,
            scheduler_stress_reduction=0.25,
            scheduler_timezone="UTC",
            router_provider="keyword",
        )

    def test_available_slots_preserve_buffer(self) -> None:
        slots = AutoSchedulerAgent.find_available_slots(
            booked_events=[
                BookedEvent(
                    title="Standup",
                    start_time=datetime(2026, 7, 6, 9, 0, tzinfo=timezone.utc),
                    end_time=datetime(2026, 7, 6, 10, 0, tzinfo=timezone.utc),
                    source="calendar",
                )
            ],
            target_date=date(2026, 7, 6),
            day_start=time(8, 0),
            day_end=time(12, 0),
            buffer_minutes=15,
            min_slot_minutes=30,
            tzinfo=timezone.utc,
        )
        self.assertEqual(len(slots), 2)
        self.assertEqual(slots[0].start_time, datetime(2026, 7, 6, 8, 0, tzinfo=timezone.utc))
        self.assertEqual(slots[0].end_time, datetime(2026, 7, 6, 8, 45, tzinfo=timezone.utc))
        self.assertEqual(slots[1].start_time, datetime(2026, 7, 6, 10, 15, tzinfo=timezone.utc))

    def test_proposal_is_non_persistent_and_redirects_q1_overflow(self) -> None:
        context = UserDailyContext(
            matrix_load={
                "Q1": {"task_count": 5, "pending": 5, "completed": 0},
                "Q2": {"task_count": 0, "pending": 0, "completed": 0},
                "Q3": {"task_count": 0, "pending": 0, "completed": 0},
                "Q4": {"task_count": 0, "pending": 0, "completed": 0},
            },
            calendar_events=[],
            scheduled_tasks=[],
        )
        result = asyncio.run(
            AutoSchedulerAgent(self.settings).propose(
                task=TaskSpec(
                    title="Urgent task",
                    duration_minutes=30,
                    source="urgent",
                    target_date=date(2026, 7, 6),
                ),
                context=context,
            )
        )
        self.assertEqual(result["status"], "success")
        self.assertEqual(result["decision"]["recommended_quadrant"], "Q2")
        self.assertTrue(result["decision"]["burnout_guard"]["q1_overflow_prevented"])
        self.assertNotIn("task_id", result)
        self.assertNotIn("task", result)



    def test_available_slots_respect_not_before_for_today(self) -> None:
        slots = AutoSchedulerAgent.find_available_slots(
            booked_events=[],
            target_date=date(2026, 7, 6),
            day_start=time(8, 0),
            day_end=time(12, 0),
            buffer_minutes=15,
            min_slot_minutes=20,
            tzinfo=timezone.utc,
            not_before=datetime(2026, 7, 6, 10, 30, tzinfo=timezone.utc),
        )

        self.assertEqual(len(slots), 1)
        self.assertEqual(slots[0].start_time, datetime(2026, 7, 6, 10, 30, tzinfo=timezone.utc))
if __name__ == "__main__":
    unittest.main()