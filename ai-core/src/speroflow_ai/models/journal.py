"""Private contracts for bounded, non-clinical journal reflections."""

from __future__ import annotations

from pydantic import BaseModel, ConfigDict, Field, field_validator


def _to_camel(value: str) -> str:
    head, *tail = value.split("_")
    return head + "".join(part.capitalize() for part in tail)


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=_to_camel, populate_by_name=True)


class JournalReflectionEntry(CamelModel):
    content: str = Field(min_length=1, max_length=6_000)
    mood: str | None = Field(default=None, max_length=32)

    @field_validator("content")
    @classmethod
    def normalize_content(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("content is required")
        return normalized

    @field_validator("mood")
    @classmethod
    def normalize_mood(cls, value: str | None) -> str | None:
        return value.strip() if value else None


class JournalReflectionRequest(CamelModel):
    """A bounded owner-checked context assembled by the main ASP.NET API."""

    current_entry: JournalReflectionEntry
    prior_entries: list[JournalReflectionEntry] = Field(default_factory=list, max_length=7)


class JournalReflectionResponse(CamelModel):
    """A reviewable observation, not a diagnosis or a persistent action."""

    emotions: list[str] = Field(min_length=1, max_length=6)
    feedback: str = Field(min_length=1, max_length=600)
    progress_summary: str = Field(min_length=1, max_length=600)

    @field_validator("emotions")
    @classmethod
    def normalize_emotions(cls, values: list[str]) -> list[str]:
        normalized: list[str] = []
        seen: set[str] = set()
        for value in values:
            candidate = value.strip()
            key = candidate.casefold()
            if not candidate or len(candidate) > 80 or key in seen:
                continue
            seen.add(key)
            normalized.append(candidate)
        if not normalized:
            raise ValueError("at least one emotion is required")
        return normalized

    @field_validator("feedback", "progress_summary")
    @classmethod
    def normalize_text(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized:
            raise ValueError("reflection text is required")
        return normalized