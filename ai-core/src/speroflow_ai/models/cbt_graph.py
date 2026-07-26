"""Typed data contracts for the reviewed CBT educational-resource graph.

The models keep source documents separate from clinician-reviewed concepts. They
are intentionally not diagnostic models and never represent a user's mental
state or treatment plan.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any


REVIEW_STATUSES = frozenset(
    {
        "candidate",
        "pending_clinical_review",
        "approved",
        "deprecated",
        "source_imported",
    }
)
PUBLISHABLE_REVIEW_STATUS = "approved"

ALLOWED_RELATIONSHIP_TYPES = frozenset(
    {
        "MENTIONS",
        "TEACHES",
        "PRACTICES",
        "MAY_SUPPORT",
        "ILLUSTRATES",
        "SUGGESTS_RESOURCE",
        "SEQUENCE",
    }
)


@dataclass
class CBTDomain:
    """A source collection such as Depression or Anxiety."""

    domain_id: str
    name: str
    source_file_count: int
    description: str = ""
    source_organization: str = "Centre for Clinical Interventions"
    license_status: str = "requires_permission"

    def to_dict(self) -> dict[str, Any]:
        return {
            "domain_id": self.domain_id,
            "name": self.name,
            "source_file_count": self.source_file_count,
            "description": self.description,
            "source_organization": self.source_organization,
            "license_status": self.license_status,
        }


@dataclass
class CBTDocument:
    """A source markdown document and its immutable provenance metadata."""

    node_id: str
    domain_id: str
    title: str
    document_type: str
    source_relpath: str
    source_sha256: str
    content: str
    source_url: str = "https://www.cci.health.wa.gov.au/"
    source_organization: str = "Centre for Clinical Interventions"
    license_status: str = "requires_permission"
    content_language: str = "en"
    module_order: int | None = None
    series_id: str = ""
    review_status: str = "source_imported"

    def to_dict(self) -> dict[str, Any]:
        return {
            "node_id": self.node_id,
            "domain_id": self.domain_id,
            "title": self.title,
            "document_type": self.document_type,
            "source_relpath": self.source_relpath,
            "source_sha256": self.source_sha256,
            "content": self.content,
            "source_url": self.source_url,
            "source_organization": self.source_organization,
            "license_status": self.license_status,
            "content_language": self.content_language,
            "module_order": self.module_order,
            "series_id": self.series_id,
            "review_status": self.review_status,
        }


@dataclass
class CBTSection:
    """A deterministic source section, not a clinician-authored concept."""

    section_id: str
    document_id: str
    domain_id: str
    title: str
    heading_level: int
    ordinal: int
    source_relpath: str
    source_anchor: str
    start_line: int
    end_line: int
    content_sha256: str
    content: str
    parent_section_id: str = ""
    review_status: str = "source_imported"

    @property
    def node_id(self) -> str:
        return self.section_id

    def to_dict(self) -> dict[str, Any]:
        return {
            "node_id": self.section_id,
            "section_id": self.section_id,
            "document_id": self.document_id,
            "domain_id": self.domain_id,
            "title": self.title,
            "heading_level": self.heading_level,
            "ordinal": self.ordinal,
            "source_relpath": self.source_relpath,
            "source_anchor": self.source_anchor,
            "start_line": self.start_line,
            "end_line": self.end_line,
            "content_sha256": self.content_sha256,
            "content": self.content,
            "parent_section_id": self.parent_section_id,
            "review_status": self.review_status,
        }


@dataclass
class CBTConcept:
    """Base class for reviewed canonical CBT concepts."""

    node_id: str
    name: str
    description: str = ""
    aliases: list[str] = field(default_factory=list)
    source_document_ids: list[str] = field(default_factory=list)
    review_status: str = "pending_clinical_review"
    taxonomy_version: str = "1.0.0"
    reviewed_by: str = ""
    reviewed_at: str = ""

    @property
    def concept_type(self) -> str:
        return "concept"

    @property
    def neo4j_label(self) -> str:
        return "CBTConcept"

    @property
    def is_publishable(self) -> bool:
        return self.review_status == PUBLISHABLE_REVIEW_STATUS

    def to_dict(self) -> dict[str, Any]:
        return {
            "node_id": self.node_id,
            "name": self.name,
            "description": self.description,
            "aliases": self.aliases,
            "source_document_ids": self.source_document_ids,
            "review_status": self.review_status,
            "taxonomy_version": self.taxonomy_version,
            "reviewed_by": self.reviewed_by,
            "reviewed_at": self.reviewed_at,
            "concept_type": self.concept_type,
        }


@dataclass
class Distortion(CBTConcept):
    """A canonical cognitive-pattern record after clinical review."""

    @property
    def concept_type(self) -> str:
        return "distortion"

    @property
    def neo4j_label(self) -> str:
        return "CBTDistortion"


@dataclass
class Technique(CBTConcept):
    """A reviewed educational technique, not a treatment recommendation."""

    steps: list[str] = field(default_factory=list)
    evidence_level: str = "not_rated"

    @property
    def concept_type(self) -> str:
        return "technique"

    @property
    def neo4j_label(self) -> str:
        return "CBTTechnique"

    def to_dict(self) -> dict[str, Any]:
        return {
            **super().to_dict(),
            "steps": self.steps,
            "evidence_level": self.evidence_level,
        }


@dataclass
class MicroHabit(CBTConcept):
    """A clinician-reviewed optional educational practice."""

    frequency: str = ""
    duration_minutes: int | None = None

    @property
    def concept_type(self) -> str:
        return "micro_habit"

    @property
    def neo4j_label(self) -> str:
        return "CBTMicroHabit"

    def to_dict(self) -> dict[str, Any]:
        return {
            **super().to_dict(),
            "frequency": self.frequency,
            "duration_minutes": self.duration_minutes,
        }


@dataclass
class SituationMapping:
    """A reviewed source example, never an inference about a user."""

    node_id: str
    situation: str
    automatic_thought: str
    source_document_id: str
    distortion_id: str = ""
    technique_id: str = ""
    evidence_locator: str = ""
    review_status: str = "pending_clinical_review"
    taxonomy_version: str = "1.0.0"
    reviewed_by: str = ""
    reviewed_at: str = ""

    @property
    def is_publishable(self) -> bool:
        return self.review_status == PUBLISHABLE_REVIEW_STATUS

    def to_dict(self) -> dict[str, Any]:
        return {
            "node_id": self.node_id,
            "situation": self.situation,
            "automatic_thought": self.automatic_thought,
            "source_document_id": self.source_document_id,
            "distortion_id": self.distortion_id,
            "technique_id": self.technique_id,
            "evidence_locator": self.evidence_locator,
            "review_status": self.review_status,
            "taxonomy_version": self.taxonomy_version,
            "reviewed_by": self.reviewed_by,
            "reviewed_at": self.reviewed_at,
        }


@dataclass
class CBTRelationship:
    """A provenance-backed edge between reviewed graph records."""

    source_id: str
    target_id: str
    relationship_type: str
    source_document_id: str
    evidence_locator: str
    review_status: str = "pending_clinical_review"
    taxonomy_version: str = "1.0.0"
    reviewed_by: str = ""
    reviewed_at: str = ""
    confidence: float | None = None

    @property
    def is_publishable(self) -> bool:
        return self.review_status == PUBLISHABLE_REVIEW_STATUS

    def to_dict(self) -> dict[str, Any]:
        return {
            "source_id": self.source_id,
            "target_id": self.target_id,
            "relationship_type": self.relationship_type,
            "source_document_id": self.source_document_id,
            "evidence_locator": self.evidence_locator,
            "review_status": self.review_status,
            "taxonomy_version": self.taxonomy_version,
            "reviewed_by": self.reviewed_by,
            "reviewed_at": self.reviewed_at,
            "confidence": self.confidence,
        }


@dataclass
class CBTGraph:
    """Complete parsed graph ready for idempotent ingestion."""

    manifest_version: str
    domains: list[CBTDomain] = field(default_factory=list)
    documents: list[CBTDocument] = field(default_factory=list)
    sections: list[CBTSection] = field(default_factory=list)
    distortions: list[Distortion] = field(default_factory=list)
    techniques: list[Technique] = field(default_factory=list)
    micro_habits: list[MicroHabit] = field(default_factory=list)
    situation_mappings: list[SituationMapping] = field(default_factory=list)
    relationships: list[CBTRelationship] = field(default_factory=list)

    @property
    def concepts(self) -> list[CBTConcept]:
        return [*self.distortions, *self.techniques, *self.micro_habits]

    @property
    def all_node_ids(self) -> set[str]:
        return {
            *(domain.domain_id for domain in self.domains),
            *(document.node_id for document in self.documents),
            *(section.section_id for section in self.sections),
            *(concept.node_id for concept in self.concepts),
            *(mapping.node_id for mapping in self.situation_mappings),
        }

    def summary(self) -> str:
        publishable_concepts = sum(concept.is_publishable for concept in self.concepts)
        publishable_relationships = sum(
            relationship.is_publishable for relationship in self.relationships
        )
        return (
            "CBT graph: "
            f"{len(self.domains)} domains, {len(self.documents)} documents, "
            f"{len(self.sections)} sections, "
            f"{publishable_concepts} approved concepts, "
            f"{publishable_relationships} approved relationships"
        )
