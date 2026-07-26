"""Private, non-persistent Eisenhower classification with a conservative fallback."""

from __future__ import annotations

import json
import logging
from datetime import UTC, datetime
from typing import Any

from speroflow_ai.config import Settings
from speroflow_ai.models.eisenhower import (
    EisenhowerClassificationRequest,
    EisenhowerClassificationResponse,
)
from speroflow_ai.services.bedrock_client import invoke_bedrock


logger = logging.getLogger("speroflow.services.eisenhower")

_NON_DISCRIMINATIVE_TERMS = {
    "about", "after", "before", "course", "final", "from", "into", "project", "task", "that", "the", "this", "with", "work",
}

_SYSTEM_PROMPT = """You classify a single productivity task into the Eisenhower Matrix.
Return exactly one JSON object with suggestedQuadrant, confidence, and rationale.
Use q1 only for urgent and important work, q2 for important but not urgent work,
q3 for urgent but not important work, and q4 for neither urgent nor important work.
Use active goals to determine importance. Use recent journals and approved insights only
as context for workload sensitivity; never diagnose, assess risk, make medical claims,
or infer facts that are not written in the supplied snapshot. Never modify data.
Keep the rationale brief, practical, and grounded in the provided information."""


class EisenhowerClassifier:
    def __init__(self, settings: Settings):
        self._settings = settings

    async def classify(
        self,
        request: EisenhowerClassificationRequest,
    ) -> EisenhowerClassificationResponse:
        if self._uses_deterministic_fallback():
            return self._fallback(request)

        try:
            raw = await invoke_bedrock(
                model_id=self._settings.llm_model,
                system_prompt=_SYSTEM_PROMPT,
                user_text=json.dumps(request.model_dump(by_alias=True), ensure_ascii=False),
                settings=self._settings,
                max_tokens=300,
                temperature=0.0,
            )
            return self._parse_response(raw)
        except Exception as exc:
            logger.warning("Eisenhower model was unavailable; using deterministic fallback: %s", type(exc).__name__)
            return self._fallback(request)

    def _uses_deterministic_fallback(self) -> bool:
        return (
            str(getattr(self._settings, "router_provider", "")).casefold() == "keyword"
            or str(getattr(self._settings, "llm_provider", "")).casefold() == "keyword"
        )

    @staticmethod
    def _parse_response(raw: str) -> EisenhowerClassificationResponse:
        start = raw.find("{")
        end = raw.rfind("}")
        if start < 0 or end <= start:
            raise ValueError("Model response did not contain a JSON object.")
        payload: Any = json.loads(raw[start : end + 1])
        return EisenhowerClassificationResponse.model_validate(payload)

    @staticmethod
    def _fallback(request: EisenhowerClassificationRequest) -> EisenhowerClassificationResponse:
        task_text = f"{request.task.title} {request.task.description}".casefold()
        goal_terms = {
            token
            for goal in request.goals
            for token in _terms(f"{goal.title} {goal.description}")
            if len(token) >= 4 and token not in _NON_DISCRIMINATIVE_TERMS
        }
        task_terms = {
            token
            for token in _terms(task_text)
            if len(token) >= 4 and token not in _NON_DISCRIMINATIVE_TERMS
        }
        # A shared life area is useful context, but is not enough evidence that an
        # arbitrary urgent item advances a specific active goal.
        supports_goal = len(goal_terms.intersection(task_terms)) >= 2
        urgency = any(marker in task_text for marker in ("urgent", "asap", "overdue", "today", "deadline", "immediately"))
        due_today = _due_today(request.task.due_at)
        important = supports_goal

        if urgency or due_today:
            quadrant = "q1" if important else "q3"
            confidence = 0.72 if important else 0.62
            rationale = "The task has an immediate time signal" + (" and connects to an active goal." if important else ".")
        elif important:
            quadrant = "q2"
            confidence = 0.68
            rationale = "The task aligns with an active goal and can be scheduled deliberately."
        elif any(marker in task_text for marker in ("delete", "unsubscribe", "scroll", "browse")):
            quadrant = "q4"
            confidence = 0.58
            rationale = "The task does not show urgency or a clear connection to an active goal."
        else:
            quadrant = "q2"
            confidence = 0.45
            rationale = "There is not enough evidence for urgency; review this task before scheduling it."

        return EisenhowerClassificationResponse(
            suggested_quadrant=quadrant,
            confidence=confidence,
            rationale=rationale,
        )


def _terms(value: str) -> list[str]:
    return [
        "".join(character for character in token if character.isalnum())
        for token in value.split()
        if token
    ]


def _due_today(value: str | None) -> bool:
    if not value:
        return False
    try:
        normalized = value.replace("Z", "+00:00")
        due = datetime.fromisoformat(normalized)
    except ValueError:
        return False
    if due.tzinfo is None:
        due = due.replace(tzinfo=UTC)
    return due.astimezone(UTC).date() <= datetime.now(UTC).date()