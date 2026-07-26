"""Pydantic request schemas exposed by the private AI API."""

from __future__ import annotations

from typing import Literal, Optional

from pydantic import BaseModel, Field, field_validator, model_validator


class QueryRequest(BaseModel):
    """Private GraphRAG request for roadmap or explicitly selected datasets."""

    question: str = Field(min_length=1, max_length=4_000)
    strategy: Literal["vector", "cypher", "hybrid"] = "hybrid"
    top_k: Optional[int] = Field(default=None, ge=1, le=20)
    scope: Literal["roadmap", "dataset"] = "roadmap"
    dataset_ids: list[str] = Field(default_factory=list, max_length=20)
    knowledge_access_grant: str | None = Field(default=None, max_length=16_000)

    @field_validator("dataset_ids")
    @classmethod
    def normalize_dataset_ids(cls, values: list[str]) -> list[str]:
        return list(dict.fromkeys(value.strip() for value in values if value and value.strip()))

    @model_validator(mode="after")
    def require_explicit_dataset_scope(self) -> "QueryRequest":
        if self.scope == "dataset" and not self.dataset_ids:
            raise ValueError("dataset_ids is required when scope is dataset.")
        if self.scope == "dataset" and not self.knowledge_access_grant:
            raise ValueError("knowledge_access_grant is required when scope is dataset.")
        if self.scope == "roadmap" and self.dataset_ids:
            raise ValueError("dataset_ids is allowed only when scope is dataset.")
        if self.scope == "roadmap" and self.knowledge_access_grant:
            raise ValueError("knowledge_access_grant is allowed only when scope is dataset.")
        return self


class CBTQueryRequest(BaseModel):
    """Bounded query for cited CBT educational resources."""

    query: str = Field(min_length=1, max_length=2_000)
    top_k: int = Field(default=3, ge=1, le=10)
    domain_ids: list[str] = Field(default_factory=list, max_length=10)


class CBTPreferenceFeedbackRequest(BaseModel):
    """Explicit helpfulness feedback for a returned CBT educational resource."""

    resource_node_id: str = Field(min_length=1, max_length=300)
    section_id: str = Field(default="", max_length=300)
    feedback: Literal["helpful", "not_helpful"]
    resource_title: str = Field(default="", max_length=500)
    domain_id: str = Field(default="", max_length=200)
    source_reference: str = Field(default="", max_length=1_000)


class CBTAdviceRequest(BaseModel):
    """Request grounded CBT educational resources and micro-habits."""

    text: str = Field(min_length=3, max_length=4_000)
    emotions: Optional[list[dict]] = None
    domain_ids: list[str] = Field(default_factory=list, max_length=10)
    top_k: int = Field(default=5, ge=1, le=10)


class PrerequisiteRequest(BaseModel):
    """Generate a prerequisite learning path for a graph topic."""

    goal_name: str = Field(min_length=1, max_length=500)