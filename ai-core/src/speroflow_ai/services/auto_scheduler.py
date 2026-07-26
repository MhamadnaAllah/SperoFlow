"""Pure scheduling proposal engine for private AI service calls."""

from __future__ import annotations

import json
import logging
import re
from dataclasses import dataclass, field
from datetime import date, datetime, time, timedelta, timezone
from typing import TYPE_CHECKING, Any
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

if TYPE_CHECKING:
    from speroflow_ai.config import Settings


logger = logging.getLogger("speroflow.services.auto_scheduler")

VALID_QUADRANTS = {"Q1", "Q2", "Q3", "Q4"}
HIGH_STRESS_LEVELS = {"high", "overload", "overwhelmed"}

SCHEDULER_SYSTEM_PROMPT = """You are a scheduling proposal assistant.
Use only the supplied task, calendar, matrix, and available-slot snapshot.
Return JSON only with recommended_quadrant, suggested_slot, adjusted_duration_minutes,
and reason. Do not claim to create, update, or persist any task or calendar event."""


@dataclass(frozen=True)
class TimeSlot:
    start_time: datetime
    end_time: datetime

    @property
    def duration_minutes(self) -> int:
        return max(0, int((self.end_time - self.start_time).total_seconds() // 60))

    def to_dict(self) -> dict[str, Any]:
        return {
            "start_time": self.start_time.isoformat(),
            "end_time": self.end_time.isoformat(),
            "duration_minutes": self.duration_minutes,
        }


@dataclass(frozen=True)
class BookedEvent:
    title: str
    start_time: datetime
    end_time: datetime
    source: str

    def to_dict(self) -> dict[str, Any]:
        return {
            "title": self.title,
            "start_time": self.start_time.isoformat(),
            "end_time": self.end_time.isoformat(),
            "source": self.source,
        }


@dataclass
class UserDailyContext:
    """An ASP.NET-provided, owner-checked snapshot for one scheduling day."""

    matrix_load: dict[str, dict[str, int]]
    calendar_events: list[BookedEvent]
    scheduled_tasks: list[BookedEvent]
    stress_level: str = "normal"
    role_distribution: list[dict[str, Any]] = field(default_factory=list)

    @property
    def q1_pending(self) -> int:
        return max(0, int(self.matrix_load.get("Q1", {}).get("pending", 0)))

    @property
    def booked_events(self) -> list[BookedEvent]:
        return sorted([*self.calendar_events, *self.scheduled_tasks], key=lambda event: event.start_time)

    def matrix_summary(self) -> str:
        return "\n".join(
            f"{quadrant}: {self.matrix_load.get(quadrant, {}).get('pending', 0)} pending"
            for quadrant in ("Q1", "Q2", "Q3", "Q4")
        )

    def events_summary(self) -> str:
        if not self.booked_events:
            return "No booked events."
        return "\n".join(
            f"{event.start_time.isoformat()} - {event.end_time.isoformat()}: {event.title} ({event.source})"
            for event in self.booked_events
        )

    def role_summary(self) -> str:
        if not self.role_distribution:
            return "No role distribution data."
        return "\n".join(
            f"{item.get('role_category', 'unknown')}: {item.get('percentage', 0)}%"
            for item in self.role_distribution
        )


@dataclass(frozen=True)
class TaskSpec:
    title: str
    description: str = ""
    duration_minutes: int = 60
    source: str = "manual"
    role_category: str = "personal"
    target_date: date | None = None
    not_before: datetime | None = None


class AutoSchedulerAgent:
    """Produce a validated schedule proposal without accessing or writing app data."""

    def __init__(self, settings: "Settings") -> None:
        self._settings = settings

    def default_target_date(self) -> date:
        return datetime.now(self._timezone).date()

    async def propose(self, *, task: TaskSpec, context: UserDailyContext) -> dict[str, Any]:
        target_date = task.target_date or self.default_target_date()
        original_duration = max(5, min(int(task.duration_minutes), 480))
        effective_duration = self._apply_stress_adjustment(original_duration, context)
        stress_adjusted = effective_duration != original_duration
        slots = self.find_available_slots(
            booked_events=context.booked_events,
            target_date=target_date,
            day_start=_parse_hhmm(getattr(self._settings, "scheduler_day_start", "08:00")),
            day_end=_parse_hhmm(getattr(self._settings, "scheduler_day_end", "22:00")),
            buffer_minutes=int(getattr(self._settings, "scheduler_buffer_minutes", 15)),
            min_slot_minutes=max(5, int(getattr(self._settings, "scheduler_min_slot_minutes", 20))),
            tzinfo=self._timezone,
            not_before=task.not_before,
        )
        if not slots:
            decision = self._no_slot_decision(task, context, stress_adjusted)
        else:
            candidate = await self._llm_schedule_decision(task, context, slots, effective_duration, stress_adjusted)
            decision = self.validate_decision(
                decision=candidate,
                task=task,
                context=context,
                available_slots=slots,
                effective_duration=effective_duration,
                stress_adjusted=stress_adjusted,
            )

        return {
            "status": "success" if decision["suggested_slot"] else "no_available_slot",
            "original_duration": original_duration,
            "effective_duration": effective_duration,
            "stress_adjusted": stress_adjusted,
            "available_slots": [slot.to_dict() for slot in slots],
            "decision": decision,
        }

    @staticmethod
    def find_available_slots(
        *,
        booked_events: list[BookedEvent],
        target_date: date,
        day_start: time,
        day_end: time,
        buffer_minutes: int,
        min_slot_minutes: int,
        tzinfo: timezone | ZoneInfo,
        not_before: datetime | None = None,
    ) -> list[TimeSlot]:
        """Find free slots while preserving an explicit buffer around every booking."""
        current = datetime.combine(target_date, day_start, tzinfo=tzinfo)
        end_of_day = datetime.combine(target_date, day_end, tzinfo=tzinfo)
        if not_before is not None:
            if not_before.tzinfo is None:
                raise ValueError("not_before must include an offset.")
            earliest = not_before.astimezone(tzinfo)
            if earliest.date() > target_date:
                return []
            if earliest.date() == target_date:
                current = max(current, earliest)
        if end_of_day <= current:
            return []

        events = sorted(
            (event for event in booked_events if event.end_time > current and event.start_time < end_of_day),
            key=lambda event: event.start_time,
        )
        slots: list[TimeSlot] = []
        buffer_delta = timedelta(minutes=max(0, buffer_minutes))
        for event in events:
            event_start = max(event.start_time.astimezone(tzinfo), current)
            event_end = min(event.end_time.astimezone(tzinfo), end_of_day)
            slot_end = event_start - buffer_delta
            if slot_end > current:
                candidate = TimeSlot(current, slot_end)
                if candidate.duration_minutes >= min_slot_minutes:
                    slots.append(candidate)
            current = max(current, event_end + buffer_delta)
            if current >= end_of_day:
                break

        if current < end_of_day:
            candidate = TimeSlot(current, end_of_day)
            if candidate.duration_minutes >= min_slot_minutes:
                slots.append(candidate)
        return slots

    def validate_decision(
        self,
        *,
        decision: dict[str, Any],
        task: TaskSpec,
        context: UserDailyContext,
        available_slots: list[TimeSlot],
        effective_duration: int,
        stress_adjusted: bool,
    ) -> dict[str, Any]:
        """Constrain a model suggestion to the supplied snapshot and policy limits."""
        threshold = max(1, int(getattr(self._settings, "scheduler_q1_overflow", 5)))
        q1_overflow = context.q1_pending >= threshold
        quadrant = _normalize_quadrant(decision.get("recommended_quadrant"), task.source)
        if quadrant == "Q1" and q1_overflow:
            quadrant = "Q2"

        slot = _slot_from_decision(decision, available_slots) or _best_slot(available_slots, effective_duration)
        duration = int(decision.get("adjusted_duration_minutes") or effective_duration)
        duration = max(5, min(duration, effective_duration, slot.duration_minutes))
        reason = str(decision.get("reason") or "").strip() or (
            f"Selected the earliest conflict-free {quadrant} slot with buffer time preserved."
        )
        return {
            "recommended_quadrant": quadrant,
            "suggested_slot": {
                "start_time": slot.start_time.isoformat(),
                "end_time": (slot.start_time + timedelta(minutes=duration)).isoformat(),
                "duration_minutes": duration,
            },
            "adjusted_duration_minutes": duration,
            "reason": reason,
            "burnout_guard": {
                "q1_overflow_prevented": bool(q1_overflow and quadrant == "Q2"),
                "stress_adjustment_applied": stress_adjusted,
                "buffer_added": True,
            },
        }

    async def _llm_schedule_decision(
        self,
        task: TaskSpec,
        context: UserDailyContext,
        available_slots: list[TimeSlot],
        effective_duration: int,
        stress_adjusted: bool,
    ) -> dict[str, Any]:
        if (getattr(self._settings, "router_provider", "") or "").lower() != "bedrock":
            return self._fallback_decision(task, context, available_slots, effective_duration, stress_adjusted)
        try:
            from speroflow_ai.services.bedrock_client import invoke_bedrock

            payload = {
                "task": {
                    "title": task.title,
                    "description": task.description,
                    "duration_minutes": effective_duration,
                    "source": task.source,
                    "role_category": task.role_category,
                },
                "matrix_load": context.matrix_load,
                "booked_events": [event.to_dict() for event in context.booked_events],
                "available_slots": [slot.to_dict() for slot in available_slots],
                "stress_level": context.stress_level,
                "role_distribution": context.role_distribution,
            }
            raw = await invoke_bedrock(
                model_id=getattr(self._settings, "scheduler_model_id", ""),
                system_prompt=SCHEDULER_SYSTEM_PROMPT,
                user_text=json.dumps(payload, ensure_ascii=True),
                settings=self._settings,
                max_tokens=300,
                temperature=0.0,
            )
            parsed = _parse_json_object(raw)
            return parsed or self._fallback_decision(task, context, available_slots, effective_duration, stress_adjusted)
        except Exception as exc:
            logger.warning("Scheduler model unavailable; using deterministic proposal: %s", exc)
            return self._fallback_decision(task, context, available_slots, effective_duration, stress_adjusted)

    def _fallback_decision(
        self,
        task: TaskSpec,
        context: UserDailyContext,
        available_slots: list[TimeSlot],
        effective_duration: int,
        stress_adjusted: bool,
    ) -> dict[str, Any]:
        quadrant = _normalize_quadrant(None, task.source)
        if quadrant == "Q1" and context.q1_pending >= max(1, int(getattr(self._settings, "scheduler_q1_overflow", 5))):
            quadrant = "Q2"
        slot = _best_slot(available_slots, effective_duration)
        duration = min(effective_duration, slot.duration_minutes)
        return {
            "recommended_quadrant": quadrant,
            "suggested_slot": {
                "start_time": slot.start_time.isoformat(),
                "end_time": (slot.start_time + timedelta(minutes=duration)).isoformat(),
                "duration_minutes": duration,
            },
            "adjusted_duration_minutes": duration,
            "reason": "Generated a deterministic proposal from the owner-checked schedule snapshot.",
            "burnout_guard": {
                "q1_overflow_prevented": quadrant == "Q2" and context.q1_pending >= max(1, int(getattr(self._settings, "scheduler_q1_overflow", 5))),
                "stress_adjustment_applied": stress_adjusted,
                "buffer_added": True,
            },
        }

    def _no_slot_decision(self, task: TaskSpec, context: UserDailyContext, stress_adjusted: bool) -> dict[str, Any]:
        threshold = max(1, int(getattr(self._settings, "scheduler_q1_overflow", 5)))
        q1_overflow = context.q1_pending >= threshold
        return {
            "recommended_quadrant": "Q2" if q1_overflow else _normalize_quadrant(None, task.source),
            "suggested_slot": {},
            "adjusted_duration_minutes": 0,
            "reason": "No conflict-free slot is available in the selected scheduling window.",
            "burnout_guard": {
                "q1_overflow_prevented": q1_overflow,
                "stress_adjustment_applied": stress_adjusted,
                "buffer_added": False,
            },
        }

    def _apply_stress_adjustment(self, duration_minutes: int, context: UserDailyContext) -> int:
        if context.stress_level.casefold() not in HIGH_STRESS_LEVELS:
            return duration_minutes
        reduction = float(getattr(self._settings, "scheduler_stress_reduction", 0.25))
        return max(5, int(duration_minutes * (1 - min(max(reduction, 0.0), 0.75))))

    @property
    def _timezone(self) -> timezone | ZoneInfo:
        name = str(getattr(self._settings, "scheduler_timezone", "UTC") or "UTC")
        if name.upper() == "UTC":
            return timezone.utc
        try:
            return ZoneInfo(name)
        except ZoneInfoNotFoundError:
            logger.warning("Unknown scheduler timezone %s; using UTC.", name)
            return timezone.utc


def _parse_hhmm(value: str) -> time:
    try:
        return time.fromisoformat(value)
    except ValueError as exc:
        raise ValueError("Scheduler day bounds must use HH:MM format.") from exc


def _best_slot(slots: list[TimeSlot], duration_minutes: int) -> TimeSlot:
    fitting = [slot for slot in slots if slot.duration_minutes >= duration_minutes]
    return min(fitting, key=lambda slot: slot.duration_minutes) if fitting else max(slots, key=lambda slot: slot.duration_minutes)


def _slot_from_decision(decision: dict[str, Any], slots: list[TimeSlot]) -> TimeSlot | None:
    raw = decision.get("suggested_slot")
    if not isinstance(raw, dict):
        return None
    try:
        start = datetime.fromisoformat(str(raw.get("start_time", "")))
        end = datetime.fromisoformat(str(raw.get("end_time", "")))
    except ValueError:
        return None
    if start.tzinfo is None or end.tzinfo is None or end <= start:
        return None
    for slot in slots:
        if start >= slot.start_time and end <= slot.end_time:
            return TimeSlot(start, end)
    return None


def _normalize_quadrant(value: Any, source: str) -> str:
    candidate = str(value or "").upper()
    if candidate in VALID_QUADRANTS:
        return candidate
    if source.casefold() in {"urgent", "deadline"}:
        return "Q1"
    return "Q2"


def _parse_json_object(raw: str) -> dict[str, Any]:
    clean = raw.strip()
    code_block = re.search(r"```(?:json)?\s*(.*?)```", clean, re.DOTALL | re.IGNORECASE)
    if code_block:
        clean = code_block.group(1).strip()
    match = re.search(r"\{.*\}", clean, re.DOTALL)
    if match:
        clean = match.group(0)
    try:
        parsed = json.loads(clean)
    except json.JSONDecodeError:
        return {}
    return parsed if isinstance(parsed, dict) else {}