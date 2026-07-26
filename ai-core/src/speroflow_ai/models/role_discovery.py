"""Contracts for private, approval-gated life-role discovery."""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator


LifeArea = Literal[
    "work",
    "family",
    "physical",
    "spiritual",
    "social",
    "learning",
    "personal",
]
RoleSignalKind = Literal["task", "project", "habit"]


def _to_camel(value: str) -> str:
    head, *tail = value.split("_")
    return head + "".join(part.capitalize() for part in tail)


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=_to_camel, populate_by_name=True)


class RoleDiscoverySignal(CamelModel):
    kind: RoleSignalKind
    label: str = Field(min_length=1, max_length=500)
    life_area: LifeArea

    @field_validator("label")
    @classmethod
    def normalize_label(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("label is required")
        return normalized


class RoleDiscoveryRequest(CamelModel):
    """Bounded, owner-checked application evidence assembled by ASP.NET."""

    existing_roles: list[str] = Field(default_factory=list, max_length=100)
    signals: list[RoleDiscoverySignal] = Field(min_length=2, max_length=80)

    @field_validator("existing_roles")
    @classmethod
    def normalize_existing_roles(cls, values: list[str]) -> list[str]:
        return [value.strip() for value in values if value.strip()][:100]


class RoleDiscoveryCandidate(CamelModel):
    name: str = Field(min_length=1, max_length=160)
    life_area: LifeArea
    confidence: float = Field(ge=0.0, le=1.0)
    evidence_indexes: list[int] = Field(min_length=2, max_length=3)

    @field_validator("name")
    @classmethod
    def normalize_name(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("name is required")
        return normalized

    @field_validator("evidence_indexes")
    @classmethod
    def ensure_distinct_indexes(cls, values: list[int]) -> list[int]:
        normalized = list(dict.fromkeys(values))
        if len(normalized) < 2:
            raise ValueError("at least two distinct evidence indexes are required")
        return normalized


class RoleDiscoveryResponse(CamelModel):
    candidates: list[RoleDiscoveryCandidate] = Field(default_factory=list, max_length=5)