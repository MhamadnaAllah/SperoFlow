"""Private contracts for bounded Eisenhower classification."""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator


Quadrant = Literal["q1", "q2", "q3", "q4"]
LifeArea = Literal[
    "work",
    "family",
    "physical",
    "spiritual",
    "social",
    "learning",
    "personal",
]


def _to_camel(value: str) -> str:
    head, *tail = value.split("_")
    return head + "".join(part.capitalize() for part in tail)


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=_to_camel, populate_by_name=True)


class EisenhowerTask(CamelModel):
    title: str = Field(min_length=1, max_length=500)
    description: str = Field(default="", max_length=4_000)
    life_area: LifeArea
    due_at: str | None = Field(default=None, max_length=64)
    estimated_minutes: int | None = Field(default=None, ge=1, le=1_440)

    @field_validator("title", "description")
    @classmethod
    def normalize_text(cls, value: str) -> str:
        return value.strip()


class EisenhowerGoal(CamelModel):
    title: str = Field(min_length=1, max_length=240)
    description: str = Field(default="", max_length=1_000)
    life_area: LifeArea
    target_at: str | None = Field(default=None, max_length=64)

    @field_validator("title", "description")
    @classmethod
    def normalize_text(cls, value: str) -> str:
        return value.strip()


class EisenhowerJournalEntry(CamelModel):
    content: str = Field(min_length=1, max_length=1_200)
    mood: str | None = Field(default=None, max_length=32)

    @field_validator("content")
    @classmethod
    def normalize_content(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("content is required")
        return normalized


class EisenhowerJournalInsight(CamelModel):
    feedback: str = Field(min_length=1, max_length=600)
    progress_summary: str = Field(min_length=1, max_length=600)

    @field_validator("feedback", "progress_summary")
    @classmethod
    def normalize_text(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("insight text is required")
        return normalized


class EisenhowerClassificationRequest(CamelModel):
    """Owner-checked snapshot supplied only by ASP.NET."""

    task: EisenhowerTask
    goals: list[EisenhowerGoal] = Field(default_factory=list, max_length=12)
    journals: list[EisenhowerJournalEntry] = Field(default_factory=list, max_length=3)
    insights: list[EisenhowerJournalInsight] = Field(default_factory=list, max_length=4)


class EisenhowerClassificationResponse(CamelModel):
    suggested_quadrant: Quadrant
    confidence: float = Field(ge=0.0, le=1.0)
    rationale: str = Field(min_length=1, max_length=600)

    @field_validator("rationale")
    @classmethod
    def normalize_rationale(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("rationale is required")
        return normalized