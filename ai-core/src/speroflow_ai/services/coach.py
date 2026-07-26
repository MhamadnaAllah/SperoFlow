"""Private Coach orchestration service with structured proposals and fallback."""

from __future__ import annotations

import json
import logging
from typing import Any, Dict, List, Optional
from pydantic import BaseModel, Field

from speroflow_ai.config import Settings
from speroflow_ai.services.bedrock_client import invoke_bedrock

logger = logging.getLogger("speroflow.services.coach")

_SYSTEM_PROMPT = """You are a Covey-inspired productivity coach AI.
Analyze the user's message alongside their active roles, open tasks, and habits.
Provide encouraging, strategic advice aligned with Eisenhower Quadrant 2 (Important, Not Urgent) thinking.

Rules:
1. Provide a direct message content response.
2. If you detect patterns (e.g. habit consistency drops, quadrant imbalance), generate observation items.
3. If you suggest new habits, tasks, roadmap changes, or scheduling changes, return them as structured proposals.
4. Output MUST be valid JSON with fields: message_content, observations (list), proposals (list).
5. Proposal objects MUST have: kind (CreateHabit, CreateTask, ApplyTaskSchedule, ApplyGoalRoadmap), title, description, payload (JSON string or object)."""


class CoachObservationItem(BaseModel):
    scope: str = Field(default="HabitPattern", description="HabitPattern, QuadrantImbalance, ReflectionInsight, SchedulingTrend")
    content: str = Field(..., description="Observation insight text")


class CoachProposalItem(BaseModel):
    kind: str = Field(..., description="CreateHabit, CreateTask, ApplyTaskSchedule, ApplyGoalRoadmap")
    title: str = Field(..., description="User-facing title of proposal")
    description: str = Field(default="", description="User-facing description")
    payload: Any = Field(..., description="Structured payload for proposal")


class CoachResponse(BaseModel):
    message_content: str = Field(..., description="Coach message response")
    observations: List[CoachObservationItem] = Field(default_factory=list)
    proposals: List[CoachProposalItem] = Field(default_factory=list)


class CoachService:
    def __init__(self, settings: Settings):
        self._settings = settings

    async def respond(self, payload: Dict[str, Any]) -> CoachResponse:
        user_message = payload.get("user_message", "").strip()

        if self._uses_deterministic_fallback():
            return self._fallback(user_message, payload)

        try:
            raw = await invoke_bedrock(
                model_id=self._settings.llm_model,
                system_prompt=_SYSTEM_PROMPT,
                user_text=json.dumps(payload, ensure_ascii=False),
                settings=self._settings,
                max_tokens=1000,
                temperature=0.3,
            )
            return self._parse_response(raw, user_message, payload)
        except Exception as exc:
            logger.warning("Coach model unavailable; using fallback: %s", type(exc).__name__)
            return self._fallback(user_message, payload)

    def _uses_deterministic_fallback(self) -> bool:
        return (
            str(getattr(self._settings, "router_provider", "")).casefold() == "keyword"
            or str(getattr(self._settings, "llm_provider", "")).casefold() == "keyword"
        )

    @staticmethod
    def _parse_response(raw: str, user_message: str, payload: Dict[str, Any]) -> CoachResponse:
        start = raw.find("{")
        end = raw.rfind("}")
        if start < 0 or end <= start:
            raise ValueError("Model response did not contain a JSON object.")
        
        parsed = json.loads(raw[start : end + 1])
        
        # Ensure payload fields are stringified JSON if dicts
        proposals = []
        for prop in parsed.get("proposals", []):
            prop_payload = prop.get("payload")
            if isinstance(prop_payload, dict):
                prop_payload = json.dumps(prop_payload)
            proposals.append(CoachProposalItem(
                kind=prop.get("kind", "CreateTask"),
                title=prop.get("title", "Coach Recommendation"),
                description=prop.get("description", ""),
                payload=prop_payload or "{}"
            ))

        return CoachResponse(
            message_content=parsed.get("message_content", "Protect your high-impact Q2 priorities."),
            observations=[CoachObservationItem(**obs) for obs in parsed.get("observations", [])],
            proposals=proposals,
        )

    @staticmethod
    def _fallback(user_message: str, payload: Dict[str, Any]) -> CoachResponse:
        msg_lower = user_message.lower()
        observations: List[CoachObservationItem] = []
        proposals: List[CoachProposalItem] = []

        if "habit" in msg_lower or "routine" in msg_lower:
            message = "Building strong daily habits is key to long-term balance. I've prepared a habit proposal for your review."
            observations.append(CoachObservationItem(
                scope="HabitPattern",
                content="User is interested in establishing a consistent daily routine."
            ))
            proposals.append(CoachProposalItem(
                kind="CreateHabit",
                title="Daily Q2 Planning Focus",
                description="Dedicate 15 minutes every morning to align your tasks with core life roles.",
                payload=json.dumps({
                    "title": "Daily Q2 Planning Focus",
                    "description": "Dedicate 15 minutes every morning to align your tasks with core life roles.",
                    "lifeArea": "Personal",
                    "targetPerWeek": 5
                })
            ))
        elif any(k in msg_lower for k in ("cbt", "anxiety", "burnout")):
            message = "I noticed signals of stress or cognitive overload. Grounded in CBT principles, establishing a daily grounding habit can help restore balance."
            observations.append(CoachObservationItem(
                scope="HabitPattern",
                content="Identified elevated stress/burnout signals; proposed CBT grounding exercise."
            ))
            proposals.append(CoachProposalItem(
                kind="CreateHabit",
                title="CBT Daily Grounding Exercise",
                description="Practice 5 minutes of 5-4-3-2-1 sensory grounding every morning to manage stress.",
                payload=json.dumps({
                    "title": "CBT Daily Grounding Exercise",
                    "description": "Practice 5 minutes of 5-4-3-2-1 sensory grounding every morning to manage stress.",
                    "lifeArea": "Personal",
                    "targetPerWeek": 7
                })
            ))
        elif "overwhelmed" in msg_lower or "busy" in msg_lower or "stress" in msg_lower:
            message = "When workload builds up, focus on Quadrant 2 planning to filter out urgent-but-unimportant tasks."
            observations.append(CoachObservationItem(
                scope="QuadrantImbalance",
                content="User experiencing workload pressure; recommended Q2 task triage."
            ))
            proposals.append(CoachProposalItem(
                kind="CreateTask",
                title="Review Quadrant 2 Focus Blocks",
                description="Identify urgent tasks to delegate or eliminate, and protect 2 hours for strategic work.",
                payload=json.dumps({
                    "title": "Review Quadrant 2 Focus Blocks",
                    "description": "Identify urgent tasks to delegate or eliminate, and protect 2 hours for strategic work.",
                    "lifeArea": "Personal",
                    "quadrant": "Q2",
                    "estimatedMinutes": 30
                })
            ))
        else:
            message = "Great reflection! Aligning your daily focus with core life roles ensures balanced, sustainable progress."
            observations.append(CoachObservationItem(
                scope="ReflectionInsight",
                content="User actively reflecting on daily priorities and life balance."
            ))

        return CoachResponse(
            message_content=message,
            observations=observations,
            proposals=proposals,
        )
