"""Pydantic response schemas exposed by the private AI API."""

from __future__ import annotations

from typing import Any, Literal, Optional

from pydantic import BaseModel, Field


class ErrorResponse(BaseModel):
    detail: str


class VectorMatchResponse(BaseModel):
    node_id: str
    label_text: str
    roadmap_name: str
    score: float
    content_snippet: str
    neighbors: list[dict[str, Any]] = Field(default_factory=list)


class QueryResponse(BaseModel):
    answer: str
    strategy_used: str
    sources: list[str] = Field(default_factory=list)
    vector_matches: list[VectorMatchResponse] = Field(default_factory=list)
    generated_cypher: Optional[str] = None
    error: Optional[str] = None
    scope: Literal["roadmap", "dataset"] = "roadmap"
    citations: list[dict[str, Any]] = Field(default_factory=list)


class CBTResourceResponse(BaseModel):
    node_id: str
    title: str
    domain_id: str
    domain_name: str
    document_type: str
    source_reference: str
    source_url: str
    score: float
    retrieval_scope: Literal["section", "document"] = "document"
    section_id: str = ""
    section_title: str = ""
    parent_section_title: str = ""
    source_anchor: str = ""
    content_excerpt: str = ""
    reviewed_concepts: list[str] = Field(default_factory=list)


class CBTPersonalizationResponse(BaseModel):
    enabled: bool = False
    applied: bool = False
    policy: str = "disabled"
    reason: str = ""
    candidate_count: int = 0
    personalized_count: int = 0
    exploration_applied: bool = False


class CBTQueryResponse(BaseModel):
    status: Literal["ok", "urgent_support"]
    disclaimer: str
    resources: list[CBTResourceResponse] = Field(default_factory=list)
    urgent_support: bool = False
    urgent_support_message: str = ""
    personalization: CBTPersonalizationResponse = Field(default_factory=CBTPersonalizationResponse)


class CBTPreferenceFeedbackResponse(BaseModel):
    status: Literal["ok"]
    resource_key: str
    preference_score: float
    feedback_count: int
    helpful_count: int
    not_helpful_count: int
    message: str


class CBTHabitResponse(BaseModel):
    title: str
    description: str
    frequency: str
    duration_minutes: int
    domain: str
    source_title: str


class CBTAdviceSourceResponse(BaseModel):
    node_id: str
    title: str
    domain: str
    document_type: str
    score: float
    content_excerpt: str = ""


class CBTAdviceResponse(BaseModel):
    status: Literal["ok", "urgent_support", "no_results", "error"]
    advice: str = ""
    habits: list[CBTHabitResponse] = Field(default_factory=list)
    detected_themes: list[str] = Field(default_factory=list)
    sources: list[CBTAdviceSourceResponse] = Field(default_factory=list)
    model_used: str = ""
    disclaimer: str = ""
    urgent_support: bool = False
    urgent_support_message: str = ""
    error: Optional[str] = None


class LearningStep(BaseModel):
    topic: str
    description: str
    estimated_hours: float
    resources: list[str] = Field(default_factory=list)


class LearningTimeline(BaseModel):
    goal: str
    steps: list[LearningStep]
    total_estimated_hours: float
    motivational_summary: str