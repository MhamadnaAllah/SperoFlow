"""Pydantic contracts for the internal role-level Balance Agent evaluator."""

from __future__ import annotations

from datetime import datetime
from typing import Literal
from uuid import UUID

from pydantic import BaseModel, Field, model_validator


LifeArea = Literal[
    "work",
    "family",
    "physical",
    "spiritual",
    "social",
    "learning",
    "personal",
]
RoleCategory = Literal["internal", "external"]
BalanceRiskLevel = Literal["low", "medium", "high", "insufficient_data"]
BalanceDataQuality = Literal[
    "sufficient",
    "insufficient_activity",
    "insufficient_classification",
]
BalanceMeasurement = Literal["tasks", "minutes"]
WellbeingSignal = Literal["unknown", "low", "moderate", "elevated"]


class BalanceRole(BaseModel):
    """An active owner role supplied by the primary application backend."""

    role_id: UUID
    role_name: str = Field(min_length=1, max_length=160)
    role_category: RoleCategory
    life_area: LifeArea


class BalanceRoleWorkload(BaseModel):
    """One aggregate of completed work for an explicit active role."""

    role_id: UUID
    completed_task_count: int = Field(ge=0, le=100_000)
    completed_minutes: int | None = Field(default=None, ge=0, le=10_000_000)

    @model_validator(mode="after")
    def validate_volume(self) -> "BalanceRoleWorkload":
        if self.completed_task_count == 0 and (self.completed_minutes or 0) > 0:
            raise ValueError("completed_minutes requires at least one completed task")
        if self.completed_task_count > 0 and self.completed_minutes == 0:
            raise ValueError("completed_minutes must be positive when provided for completed tasks")
        return self


class BalanceEvaluationRequest(BaseModel):
    """Minimal owner-checked aggregate input sent by the trusted primary backend."""

    subject_id: UUID
    request_id: UUID
    window_start: datetime
    window_end: datetime
    active_roles: list[BalanceRole] = Field(min_length=1, max_length=100)
    role_workloads: list[BalanceRoleWorkload] = Field(default_factory=list, max_length=100)
    unclassified_completed_task_count: int = Field(default=0, ge=0, le=100_000)
    wellbeing_signal: WellbeingSignal = "unknown"
    wellbeing_signal_observed_at: datetime | None = None

    @model_validator(mode="after")
    def validate_snapshot(self) -> "BalanceEvaluationRequest":
        if self.window_start.tzinfo is None or self.window_end.tzinfo is None:
            raise ValueError("window_start and window_end must include timezone information")
        if self.window_start >= self.window_end:
            raise ValueError("window_start must be earlier than window_end")

        role_ids = [role.role_id for role in self.active_roles]
        if len(set(role_ids)) != len(role_ids):
            raise ValueError("active_roles must not contain duplicate role IDs")

        workload_ids = [workload.role_id for workload in self.role_workloads]
        if len(set(workload_ids)) != len(workload_ids):
            raise ValueError("role_workloads must contain at most one entry per role")
        if not set(workload_ids).issubset(role_ids):
            raise ValueError("role_workloads may only contain active roles")

        if (
            self.wellbeing_signal_observed_at is not None
            and self.wellbeing_signal_observed_at.tzinfo is None
        ):
            raise ValueError("wellbeing_signal_observed_at must include timezone information")
        return self


class BalanceRoleStat(BaseModel):
    """Normalized output for every active role."""

    role_id: UUID
    role_name: str = Field(min_length=1, max_length=160)
    role_category: RoleCategory
    life_area: LifeArea
    completed_task_count: int = Field(ge=0)
    completed_minutes: int | None = Field(default=None, ge=0)
    share_percent: float = Field(ge=0.0, le=100.0)


class BalanceSuggestion(BaseModel):
    """A user-confirmable Q2 suggestion for one explicit role."""

    suggestion_id: UUID
    role_id: UUID
    role_name: str = Field(min_length=1, max_length=160)
    life_area: LifeArea
    title: str = Field(min_length=1, max_length=200)
    description: str = Field(min_length=1, max_length=500)
    duration_minutes: int = Field(ge=5, le=30)
    recommended_quadrant: Literal["q2"] = "q2"
    action: Literal["propose"] = "propose"
    requires_confirmation: bool = True


class BalanceEvaluationResponse(BaseModel):
    """Deterministic role-level assessment returned to the primary backend."""

    request_id: UUID
    audit_key: str = Field(min_length=16, max_length=128)
    risk_level: BalanceRiskLevel
    data_quality: BalanceDataQuality
    measurement: BalanceMeasurement
    attention_score: int = Field(ge=0, le=100)
    classified_completed_task_count: int = Field(ge=0)
    unclassified_completed_task_count: int = Field(ge=0)
    classification_coverage_percent: float = Field(ge=0.0, le=100.0)
    dominant_role_id: UUID | None = None
    dominant_role_name: str | None = Field(default=None, max_length=160)
    dominant_life_area: LifeArea | None = None
    dominant_share_percent: float | None = Field(default=None, ge=0.0, le=100.0)
    neglected_role_ids: list[UUID] = Field(default_factory=list)
    role_stats: list[BalanceRoleStat] = Field(default_factory=list)
    insight: str = Field(min_length=1, max_length=600)
    suggestion: BalanceSuggestion | None = None