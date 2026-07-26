"""Deterministic, aggregate-only role-level balance assessment service.

The primary backend owns identity, task persistence, audit history, consent,
and task creation. This service evaluates only a bounded aggregate and returns
a user-confirmable Q2 proposal for an explicit active role.
"""

from __future__ import annotations

import hashlib
import json
from typing import Final
from uuid import NAMESPACE_URL, UUID, uuid5

from speroflow_ai.models.balance import (
    BalanceDataQuality,
    BalanceEvaluationRequest,
    BalanceEvaluationResponse,
    BalanceRole,
    BalanceRoleStat,
    BalanceRoleWorkload,
    BalanceRiskLevel,
    BalanceSuggestion,
    LifeArea,
)
from speroflow_ai.services.balance_settings import BalanceAgentSettings


SUGGESTION_CATALOG: Final[dict[LifeArea, tuple[str, str]]] = {
    "work": (
        "Protect a short planning block",
        "Reserve a small Q2 block to clarify one next step before adding more work.",
    ),
    "family": (
        "Make space for a short family check-in",
        "Set aside a few uninterrupted minutes to connect with someone important to you.",
    ),
    "physical": (
        "Take a brief movement break",
        "Choose a short walk, stretch, or movement activity that fits naturally into today.",
    ),
    "spiritual": (
        "Take a quiet reflective pause",
        "Make room for a brief reflection or practice that feels meaningful to you.",
    ),
    "social": (
        "Send a short check-in",
        "Reach out to someone you value with a simple, low-pressure message or call.",
    ),
    "learning": (
        "Invest in a focused learning moment",
        "Protect a small block for reading, practice, or a topic that matters to you.",
    ),
    "personal": (
        "Create a small personal reset",
        "Reserve a few minutes for a restorative personal activity that fits your day.",
    ),
}

WELLBEING_SCORE_BONUS: Final[dict[str, int]] = {
    "unknown": 0,
    "low": 0,
    "moderate": 8,
    "elevated": 15,
}


class BalanceAgent:
    """Evaluate explicit role aggregates without making clinical claims."""

    def __init__(self, settings: BalanceAgentSettings):
        self._settings = settings

    def evaluate(self, request: BalanceEvaluationRequest) -> BalanceEvaluationResponse:
        """Return a deterministic assessment for one trusted role snapshot."""
        self._validate_window(request)
        audit_key = self._build_audit_key(request)
        workloads = {workload.role_id: workload for workload in request.role_workloads}
        classified_count = sum(workload.completed_task_count for workload in workloads.values())
        total_count = classified_count + request.unclassified_completed_task_count
        coverage_percent = (
            round((classified_count / total_count) * 100.0, 2) if total_count else 0.0
        )

        measurement = self._select_measurement(request)
        volumes = {
            role.role_id: self._volume_for_role(workloads.get(role.role_id), measurement)
            for role in request.active_roles
        }
        total_volume = sum(volumes.values())
        role_stats = self._build_role_stats(
            active_roles=request.active_roles,
            workloads=workloads,
            volumes=volumes,
            total_volume=total_volume,
        )

        data_quality = self._data_quality(
            total_count=total_count,
            classified_count=classified_count,
            coverage_percent=coverage_percent,
        )
        if data_quality != "sufficient":
            return BalanceEvaluationResponse(
                request_id=request.request_id,
                audit_key=audit_key,
                risk_level="insufficient_data",
                data_quality=data_quality,
                measurement=measurement,
                attention_score=0,
                classified_completed_task_count=classified_count,
                unclassified_completed_task_count=request.unclassified_completed_task_count,
                classification_coverage_percent=coverage_percent,
                role_stats=role_stats,
                insight=(
                    "There is not enough clearly role-linked completed activity in this "
                    "view to make a balance suggestion."
                ),
            )

        dominant_role, dominant_volume = self._dominant_role(request.active_roles, volumes)
        dominant_share = dominant_volume / total_volume if total_volume else 0.0
        neglected = [role for role in request.active_roles if volumes.get(role.role_id, 0) == 0]
        attention_score = self._attention_score(
            active_role_count=len(request.active_roles),
            dominant_share=dominant_share,
            neglected_count=len(neglected),
            wellbeing_signal=request.wellbeing_signal,
        )
        risk_level = self._risk_level(
            dominant_share=dominant_share,
            neglected_count=len(neglected),
            wellbeing_signal=request.wellbeing_signal,
        )
        suggestion = None
        if risk_level in {"medium", "high"}:
            suggestion = self._build_suggestion(
                audit_key=audit_key,
                target_role=self._suggestion_target(
                    active_roles=request.active_roles,
                    volumes=volumes,
                    neglected=neglected,
                ),
            )

        return BalanceEvaluationResponse(
            request_id=request.request_id,
            audit_key=audit_key,
            risk_level=risk_level,
            data_quality="sufficient",
            measurement=measurement,
            attention_score=attention_score,
            classified_completed_task_count=classified_count,
            unclassified_completed_task_count=request.unclassified_completed_task_count,
            classification_coverage_percent=coverage_percent,
            dominant_role_id=dominant_role.role_id,
            dominant_role_name=dominant_role.role_name,
            dominant_life_area=dominant_role.life_area,
            dominant_share_percent=round(dominant_share * 100.0, 2),
            neglected_role_ids=[role.role_id for role in neglected],
            role_stats=role_stats,
            insight=self._build_insight(
                risk_level=risk_level,
                dominant_role=dominant_role,
                dominant_share=dominant_share,
                neglected=neglected,
            ),
            suggestion=suggestion,
        )

    def _validate_window(self, request: BalanceEvaluationRequest) -> None:
        window_days = (request.window_end - request.window_start).total_seconds() / 86_400
        if window_days > self._settings.balance_max_lookback_days:
            raise ValueError(
                f"Balance evaluation windows cannot exceed "
                f"{self._settings.balance_max_lookback_days} days."
            )

    def _data_quality(
        self,
        *,
        total_count: int,
        classified_count: int,
        coverage_percent: float,
    ) -> BalanceDataQuality:
        if total_count == 0:
            return "insufficient_activity"
        if coverage_percent < self._settings.balance_min_classification_coverage * 100.0:
            return "insufficient_classification"
        if classified_count < self._settings.balance_min_classified_tasks:
            return "insufficient_activity"
        return "sufficient"

    @staticmethod
    def _select_measurement(request: BalanceEvaluationRequest) -> str:
        observed = [
            workload
            for workload in request.role_workloads
            if workload.completed_task_count > 0
        ]
        if observed and all(workload.completed_minutes is not None for workload in observed):
            return "minutes"
        return "tasks"

    @staticmethod
    def _volume_for_role(workload: BalanceRoleWorkload | None, measurement: str) -> int:
        if workload is None:
            return 0
        if measurement == "minutes":
            return int(workload.completed_minutes or 0)
        return int(workload.completed_task_count)

    @staticmethod
    def _build_role_stats(
        *,
        active_roles: list[BalanceRole],
        workloads: dict[UUID, BalanceRoleWorkload],
        volumes: dict[UUID, int],
        total_volume: int,
    ) -> list[BalanceRoleStat]:
        stats: list[BalanceRoleStat] = []
        for role in active_roles:
            workload = workloads.get(role.role_id)
            volume = volumes[role.role_id]
            share_percent = round((volume / total_volume) * 100.0, 2) if total_volume else 0.0
            stats.append(
                BalanceRoleStat(
                    role_id=role.role_id,
                    role_name=role.role_name,
                    role_category=role.role_category,
                    life_area=role.life_area,
                    completed_task_count=workload.completed_task_count if workload else 0,
                    completed_minutes=workload.completed_minutes if workload else None,
                    share_percent=share_percent,
                )
            )
        return stats

    @staticmethod
    def _dominant_role(
        active_roles: list[BalanceRole],
        volumes: dict[UUID, int],
    ) -> tuple[BalanceRole, int]:
        return max(
            ((role, volumes[role.role_id]) for role in active_roles),
            key=lambda item: item[1],
        )

    def _attention_score(
        self,
        *,
        active_role_count: int,
        dominant_share: float,
        neglected_count: int,
        wellbeing_signal: str,
    ) -> int:
        baseline_share = 1.0 / active_role_count
        concentration = max(0.0, dominant_share - baseline_share) / max(
            1.0 - baseline_share,
            0.01,
        )
        concentration_points = concentration * 60.0
        neglected_points = (neglected_count / active_role_count) * 25.0
        wellbeing_points = WELLBEING_SCORE_BONUS[wellbeing_signal]
        return min(100, round(concentration_points + neglected_points + wellbeing_points))

    def _risk_level(
        self,
        *,
        dominant_share: float,
        neglected_count: int,
        wellbeing_signal: str,
    ) -> BalanceRiskLevel:
        if (
            dominant_share >= self._settings.balance_high_concentration_threshold
            and neglected_count >= 1
        ):
            return "high"
        if (
            dominant_share >= self._settings.balance_medium_concentration_threshold
            or neglected_count >= 2
            or (
                wellbeing_signal == "elevated"
                and (dominant_share >= 0.55 or neglected_count >= 1)
            )
        ):
            return "medium"
        return "low"

    @staticmethod
    def _suggestion_target(
        *,
        active_roles: list[BalanceRole],
        volumes: dict[UUID, int],
        neglected: list[BalanceRole],
    ) -> BalanceRole:
        if neglected:
            return neglected[0]
        return min(active_roles, key=lambda role: volumes[role.role_id])

    def _build_suggestion(
        self,
        *,
        audit_key: str,
        target_role: BalanceRole,
    ) -> BalanceSuggestion:
        title, description = SUGGESTION_CATALOG[target_role.life_area]
        duration = self._settings.balance_suggestion_duration_minutes
        suggestion_id = uuid5(
            NAMESPACE_URL,
            f"speroflow:balance:{audit_key}:{target_role.role_id}",
        )
        return BalanceSuggestion(
            suggestion_id=suggestion_id,
            role_id=target_role.role_id,
            role_name=target_role.role_name,
            life_area=target_role.life_area,
            title=title,
            description=description,
            duration_minutes=duration,
        )

    @staticmethod
    def _build_insight(
        *,
        risk_level: BalanceRiskLevel,
        dominant_role: BalanceRole,
        dominant_share: float,
        neglected: list[BalanceRole],
    ) -> str:
        if risk_level == "low":
            return "This completed-activity view is broadly balanced across the roles you track."

        share = round(dominant_share * 100.0)
        if neglected:
            neglected_labels = ", ".join(role.role_name for role in neglected[:4])
            return (
                f"Recent completed activity is concentrated in {dominant_role.role_name} "
                f"({share}%), while {neglected_labels} has no completed activity in this view."
            )
        return (
            f"Recent completed activity is concentrated in {dominant_role.role_name} "
            f"({share}%). A small Q2 action can help protect time for another role."
        )

    @staticmethod
    def _build_audit_key(request: BalanceEvaluationRequest) -> str:
        canonical = {
            "subject_id": str(request.subject_id),
            "window_start": request.window_start.isoformat(),
            "window_end": request.window_end.isoformat(),
            "active_roles": sorted(
                [
                    {
                        "role_id": str(role.role_id),
                        "role_name": role.role_name,
                        "role_category": role.role_category,
                        "life_area": role.life_area,
                    }
                    for role in request.active_roles
                ],
                key=lambda role: role["role_id"],
            ),
            "role_workloads": sorted(
                [
                    {
                        "role_id": str(workload.role_id),
                        "completed_task_count": workload.completed_task_count,
                        "completed_minutes": workload.completed_minutes,
                    }
                    for workload in request.role_workloads
                ],
                key=lambda workload: workload["role_id"],
            ),
            "unclassified_completed_task_count": request.unclassified_completed_task_count,
            "wellbeing_signal": request.wellbeing_signal,
        }
        encoded = json.dumps(canonical, separators=(",", ":"), sort_keys=True).encode("utf-8")
        return hashlib.sha256(encoded).hexdigest()