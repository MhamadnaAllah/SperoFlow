"""Private role-discovery orchestration with a conservative deterministic fallback."""

from __future__ import annotations

import json
import logging
from collections.abc import Iterable
from typing import Any

from speroflow_ai.config import Settings
from speroflow_ai.models.role_discovery import (
    RoleDiscoveryCandidate,
    RoleDiscoveryRequest,
    RoleDiscoveryResponse,
)
from speroflow_ai.services.bedrock_client import invoke_bedrock


logger = logging.getLogger("speroflow.services.role_discovery")

_SYSTEM_PROMPT = """You discover possible life roles for a productivity application.
Only propose an external role when at least two supplied signals explicitly and repeatedly support the same ongoing responsibility.
Use the user's existing roles to avoid duplicates. Do not infer protected traits, diagnoses, relationships, or identity from weak clues.
Return exactly one JSON object with a candidates array. Each candidate must contain name, lifeArea, confidence, and two or three evidenceIndexes.
Use only zero-based indexes from the supplied signals. Do not create tasks or modify data. Return no candidates when the evidence is insufficient."""


class RoleDiscoveryService:
    def __init__(self, settings: Settings):
        self._settings = settings

    async def discover(self, request: RoleDiscoveryRequest) -> RoleDiscoveryResponse:
        if self._uses_deterministic_fallback():
            return self._fallback(request)

        try:
            raw = await invoke_bedrock(
                model_id=self._settings.llm_model,
                system_prompt=_SYSTEM_PROMPT,
                user_text=json.dumps(request.model_dump(by_alias=True), ensure_ascii=False),
                settings=self._settings,
                max_tokens=600,
                temperature=0.1,
            )
            return self._parse_response(raw, request)
        except Exception as exc:
            logger.warning("Role discovery model was unavailable; using deterministic fallback: %s", type(exc).__name__)
            return self._fallback(request)

    def _uses_deterministic_fallback(self) -> bool:
        return (
            str(getattr(self._settings, "router_provider", "")).casefold() == "keyword"
            or str(getattr(self._settings, "llm_provider", "")).casefold() == "keyword"
        )

    @staticmethod
    def _parse_response(raw: str, request: RoleDiscoveryRequest) -> RoleDiscoveryResponse:
        start = raw.find("{")
        end = raw.rfind("}")
        if start < 0 or end <= start:
            raise ValueError("Model response did not contain a JSON object.")
        response = RoleDiscoveryResponse.model_validate(json.loads(raw[start : end + 1]))
        existing = {_canonical(value) for value in request.existing_roles}
        candidates: list[RoleDiscoveryCandidate] = []
        for candidate in response.candidates:
            name_key = _canonical(candidate.name)
            valid_indexes = [
                index
                for index in candidate.evidence_indexes
                if 0 <= index < len(request.signals)
            ]
            if (
                not name_key
                or name_key in existing
                or candidate.confidence < 0.65
                or len(valid_indexes) < 2
            ):
                continue
            existing.add(name_key)
            candidates.append(candidate.model_copy(update={"evidence_indexes": valid_indexes[:3]}))
        return RoleDiscoveryResponse(candidates=candidates[:5])

    @staticmethod
    def _fallback(request: RoleDiscoveryRequest) -> RoleDiscoveryResponse:
        existing = {_canonical(value) for value in request.existing_roles}
        patterns: list[tuple[str, str, tuple[str, ...]]] = [
            ("Parent", "family", ("parent", "child", "daughter", "son", "school pickup", "family")),
            ("Manager", "work", ("manager", "team", "direct report", "one-on-one", "staff")),
            ("Founder", "work", ("founder", "startup", "company", "cofounder", "investor")),
            ("Student", "learning", ("course", "study", "exam", "class", "assignment")),
            ("Volunteer", "social", ("volunteer", "community", "charity", "nonprofit")),
            ("Caregiver", "family", ("caregiver", "caregiving", "appointment for", "hospital visit")),
            ("Mentor", "social", ("mentor", "mentoring", "mentee", "coaching session")),
        ]
        candidates: list[RoleDiscoveryCandidate] = []
        for name, life_area, markers in patterns:
            if _canonical(name) in existing:
                continue
            indexes = _matching_indexes(request.signals, markers)
            if len(indexes) < 2:
                continue
            existing.add(_canonical(name))
            candidates.append(
                RoleDiscoveryCandidate(
                    name=name,
                    life_area=life_area,
                    confidence=0.75,
                    evidence_indexes=indexes[:3],
                )
            )
            if len(candidates) == 5:
                break
        return RoleDiscoveryResponse(candidates=candidates)


def _matching_indexes(signals: Iterable[Any], markers: tuple[str, ...]) -> list[int]:
    return [
        index
        for index, signal in enumerate(signals)
        if any(marker in signal.label.casefold() for marker in markers)
    ]


def _canonical(value: str) -> str:
    return "".join(character.casefold() for character in value if character.isalnum())