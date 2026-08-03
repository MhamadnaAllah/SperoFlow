"""Fail-closed parser for the CBT source manifest and reviewed taxonomy data."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path, PurePosixPath
from typing import Any, TypeVar

from speroflow_ai.models.cbt_graph import (
    ALLOWED_RELATIONSHIP_TYPES,
    PUBLISHABLE_REVIEW_STATUS,
    REVIEW_STATUSES,
    CBTConcept,
    CBTDocument,
    CBTDomain,
    CBTGraph,
    CBTRelationship,
    CBTSection,
    Distortion,
    MicroHabit,
    SituationMapping,
    Technique,
)
from speroflow_ai.parsers.cbt_markdown import parse_markdown_sections


TConcept = TypeVar("TConcept", bound=CBTConcept)


class CBTParser:
    """Parse source records and only publish clinically reviewed taxonomy data."""

    def __init__(
        self,
        data_dir: Path,
        source_root: Path | None = None,
        include_unreviewed_entities: bool = False,
    ) -> None:
        self._data_dir = Path(data_dir)
        self._source_root = Path(source_root) if source_root else self._data_dir.parent / "CBT-Data-md"
        self._include_unreviewed_entities = include_unreviewed_entities

    def parse(self) -> CBTGraph:
        manifest = self._load_json(self._data_dir / "manifest.json")
        if not isinstance(manifest, dict):
            raise ValueError("CBT manifest must be a JSON object")

        manifest_version = self._require_string(manifest, "schema_version", "manifest")
        domains = self._parse_domains(manifest.get("domains", []))
        documents, sections = self._parse_documents(
            manifest.get("documents", []), {d.domain_id for d in domains}
        )
        document_ids = {document.node_id for document in documents}
        section_ids = {section.section_id for section in sections}

        distortions = self._parse_concepts("distortions.json", Distortion, document_ids)
        techniques = self._parse_concepts("techniques.json", Technique, document_ids)
        micro_habits = self._parse_concepts("micro_habits.json", MicroHabit, document_ids)
        concept_ids = {
            *(concept.node_id for concept in distortions),
            *(concept.node_id for concept in techniques),
            *(concept.node_id for concept in micro_habits),
        }
        mappings = self._parse_mappings(document_ids, concept_ids)
        relationships = self._parse_relationships(
            document_ids=document_ids,
            section_ids=section_ids,
            concept_ids=concept_ids,
            mapping_ids={mapping.node_id for mapping in mappings},
        )

        graph = CBTGraph(
            manifest_version=manifest_version,
            domains=domains,
            documents=documents,
            sections=sections,
            distortions=distortions,
            techniques=techniques,
            micro_habits=micro_habits,
            situation_mappings=mappings,
            relationships=relationships,
        )
        self._ensure_unique_node_ids(graph)
        return graph

    def _parse_domains(self, raw_domains: Any) -> list[CBTDomain]:
        if not isinstance(raw_domains, list):
            raise ValueError("manifest.domains must be a list")
        domains: list[CBTDomain] = []
        for index, raw in enumerate(raw_domains):
            if not isinstance(raw, dict):
                raise ValueError(f"manifest.domains[{index}] must be an object")
            domains.append(
                CBTDomain(
                    domain_id=self._require_string(raw, "domain_id", f"domain {index}"),
                    name=self._require_string(raw, "name", f"domain {index}"),
                    source_file_count=self._require_int(raw, "source_file_count", f"domain {index}"),
                    description=str(raw.get("description", "")),
                    source_organization=str(raw.get("source_organization", "Centre for Clinical Interventions")),
                    license_status=str(raw.get("license_status", "requires_permission")),
                )
            )
        return domains

    def _parse_documents(
        self, raw_documents: Any, domain_ids: set[str]
    ) -> tuple[list[CBTDocument], list[CBTSection]]:
        if not isinstance(raw_documents, list):
            raise ValueError("manifest.documents must be a list")
        documents: list[CBTDocument] = []
        sections: list[CBTSection] = []
        for index, raw in enumerate(raw_documents):
            if not isinstance(raw, dict):
                raise ValueError(f"manifest.documents[{index}] must be an object")
            domain_id = self._require_string(raw, "domain_id", f"document {index}")
            if domain_id not in domain_ids:
                raise ValueError(f"document {index} references unknown domain '{domain_id}'")
            source_relpath = self._require_string(raw, "source_relpath", f"document {index}")
            source_path = self._resolve_source_path(source_relpath)
            source_bytes = source_path.read_bytes()
            content = source_bytes.decode("utf-8").strip()
            expected_hash = self._require_string(raw, "source_sha256", f"document {index}")
            # Normalize CRLF -> LF before hashing so the integrity check is
            # stable across Windows (autocrlf) and Linux checkouts of the KB.
            canonical_bytes = source_bytes.decode("utf-8").replace("\r\n", "\n").encode("utf-8")
            actual_hash = hashlib.sha256(canonical_bytes).hexdigest()
            if actual_hash != expected_hash:
                raise ValueError(
                    f"source hash mismatch for '{source_relpath}'. Rebuild the CBT manifest before ingestion."
                )
            node_id = self._require_string(raw, "node_id", f"document {index}")
            module_order = raw.get("module_order")
            if module_order is not None and not isinstance(module_order, int):
                raise ValueError(f"document {index} has an invalid module_order")
            documents.append(
                CBTDocument(
                    node_id=node_id,
                    domain_id=domain_id,
                    title=self._require_string(raw, "title", f"document {index}"),
                    document_type=self._require_string(raw, "document_type", f"document {index}"),
                    source_relpath=source_relpath,
                    source_sha256=expected_hash,
                    content=content,
                    source_url=str(raw.get("source_url", "https://www.cci.health.wa.gov.au/")),
                    source_organization=str(raw.get("source_organization", "Centre for Clinical Interventions")),
                    license_status=str(raw.get("license_status", "requires_permission")),
                    content_language=str(raw.get("content_language", "en")),
                    module_order=module_order,
                    series_id=str(raw.get("series_id", "")),
                )
            )
            for section in parse_markdown_sections(
                document_id=node_id,
                domain_id=domain_id,
                source_relpath=source_relpath,
                text=source_bytes.decode("utf-8"),
            ):
                sections.append(
                    CBTSection(
                        section_id=section.section_id,
                        document_id=section.document_id,
                        domain_id=section.domain_id,
                        title=section.title,
                        heading_level=section.heading_level,
                        ordinal=section.ordinal,
                        source_relpath=section.source_relpath,
                        source_anchor=section.source_anchor,
                        start_line=section.start_line,
                        end_line=section.end_line,
                        content_sha256=section.content_sha256,
                        content=section.content,
                        parent_section_id=section.parent_section_id,
                    )
                )
        return documents, sections

    def _parse_concepts(
        self,
        filename: str,
        concept_class: type[TConcept],
        document_ids: set[str],
    ) -> list[TConcept]:
        records = self._load_collection(self._data_dir / "taxonomy" / filename)
        concepts: list[TConcept] = []
        for index, raw in enumerate(records):
            context = f"{filename}[{index}]"
            self._validate_review_record(raw, context)
            source_document_ids = self._string_list(raw.get("source_document_ids", []), context)
            if raw.get("review_status") == PUBLISHABLE_REVIEW_STATUS and not source_document_ids:
                raise ValueError(f"{context} is approved but has no source document provenance")
            unknown_sources = set(source_document_ids) - document_ids
            if unknown_sources:
                raise ValueError(f"{context} references unknown source documents: {sorted(unknown_sources)}")
            concept = concept_class(
                node_id=self._require_string(raw, "node_id", context),
                name=self._require_string(raw, "name", context),
                description=str(raw.get("description", "")),
                aliases=self._string_list(raw.get("aliases", []), context),
                source_document_ids=source_document_ids,
                review_status=str(raw.get("review_status", "pending_clinical_review")),
                taxonomy_version=str(raw.get("taxonomy_version", "1.0.0")),
                reviewed_by=str(raw.get("reviewed_by", "")),
                reviewed_at=str(raw.get("reviewed_at", "")),
            )
            if isinstance(concept, Technique):
                concept.steps = self._string_list(raw.get("steps", []), context)
                concept.evidence_level = str(raw.get("evidence_level", "not_rated"))
            if isinstance(concept, MicroHabit):
                concept.frequency = str(raw.get("frequency", ""))
                duration_minutes = raw.get("duration_minutes")
                if duration_minutes is not None and not isinstance(duration_minutes, int):
                    raise ValueError(f"{context} has an invalid duration_minutes")
                concept.duration_minutes = duration_minutes
            if self._include_unreviewed_entities or concept.is_publishable:
                concepts.append(concept)
        return concepts

    def _parse_mappings(
        self,
        document_ids: set[str],
        concept_ids: set[str],
    ) -> list[SituationMapping]:
        records = self._load_collection(self._data_dir / "taxonomy" / "situation_mappings.json")
        mappings: list[SituationMapping] = []
        for index, raw in enumerate(records):
            context = f"situation_mappings.json[{index}]"
            self._validate_review_record(raw, context)
            if not self._include_unreviewed_entities and raw.get("review_status") != PUBLISHABLE_REVIEW_STATUS:
                continue
            source_document_id = self._require_string(raw, "source_document_id", context)
            if source_document_id not in document_ids:
                raise ValueError(f"{context} references unknown source document '{source_document_id}'")
            distortion_id = str(raw.get("distortion_id", ""))
            technique_id = str(raw.get("technique_id", ""))
            for concept_id in (distortion_id, technique_id):
                if concept_id and concept_id not in concept_ids:
                    raise ValueError(f"{context} references unavailable concept '{concept_id}'")
            mapping = SituationMapping(
                node_id=self._require_string(raw, "node_id", context),
                situation=self._require_string(raw, "situation", context),
                automatic_thought=self._require_string(raw, "automatic_thought", context),
                source_document_id=source_document_id,
                distortion_id=distortion_id,
                technique_id=technique_id,
                evidence_locator=self._require_string(raw, "evidence_locator", context),
                review_status=str(raw.get("review_status", "pending_clinical_review")),
                taxonomy_version=str(raw.get("taxonomy_version", "1.0.0")),
                reviewed_by=str(raw.get("reviewed_by", "")),
                reviewed_at=str(raw.get("reviewed_at", "")),
            )
            mappings.append(mapping)
        return mappings

    def _parse_relationships(
        self,
        document_ids: set[str],
        section_ids: set[str],
        concept_ids: set[str],
        mapping_ids: set[str],
    ) -> list[CBTRelationship]:
        records = self._load_collection(self._data_dir / "taxonomy" / "relationships.json")
        known_ids = document_ids | section_ids | concept_ids | mapping_ids
        relationships: list[CBTRelationship] = []
        for index, raw in enumerate(records):
            context = f"relationships.json[{index}]"
            self._validate_review_record(raw, context)
            if not self._include_unreviewed_entities and raw.get("review_status") != PUBLISHABLE_REVIEW_STATUS:
                continue
            relationship_type = self._require_string(raw, "relationship_type", context)
            if relationship_type not in ALLOWED_RELATIONSHIP_TYPES:
                raise ValueError(f"{context} has unsupported relationship type '{relationship_type}'")
            source_id = self._require_string(raw, "source_id", context)
            target_id = self._require_string(raw, "target_id", context)
            source_document_id = self._require_string(raw, "source_document_id", context)
            if source_id not in known_ids or target_id not in known_ids:
                raise ValueError(f"{context} references an unavailable source or target node")
            if source_document_id not in document_ids:
                raise ValueError(f"{context} references unknown evidence document '{source_document_id}'")
            confidence = raw.get("confidence")
            if confidence is not None and (not isinstance(confidence, (int, float)) or not 0 <= confidence <= 1):
                raise ValueError(f"{context} confidence must be a number between 0 and 1")
            relationship = CBTRelationship(
                source_id=source_id,
                target_id=target_id,
                relationship_type=relationship_type,
                source_document_id=source_document_id,
                evidence_locator=self._require_string(raw, "evidence_locator", context),
                review_status=str(raw.get("review_status", "pending_clinical_review")),
                taxonomy_version=str(raw.get("taxonomy_version", "1.0.0")),
                reviewed_by=str(raw.get("reviewed_by", "")),
                reviewed_at=str(raw.get("reviewed_at", "")),
                confidence=float(confidence) if confidence is not None else None,
            )
            relationships.append(relationship)
        return relationships

    def _validate_review_record(self, raw: Any, context: str) -> None:
        if not isinstance(raw, dict):
            raise ValueError(f"{context} must be an object")
        status = str(raw.get("review_status", "pending_clinical_review"))
        if status not in REVIEW_STATUSES:
            raise ValueError(f"{context} has unsupported review status '{status}'")
        if status == PUBLISHABLE_REVIEW_STATUS:
            if not str(raw.get("reviewed_by", "")).strip() or not str(raw.get("reviewed_at", "")).strip():
                raise ValueError(f"{context} is approved but missing reviewer provenance")

    def _resolve_source_path(self, source_relpath: str) -> Path:
        rel = PurePosixPath(source_relpath)
        if rel.is_absolute() or ".." in rel.parts:
            raise ValueError(f"invalid source-relative path '{source_relpath}'")
        source_root = self._source_root.resolve()
        source_path = (source_root.joinpath(*rel.parts)).resolve()
        try:
            source_path.relative_to(source_root)
        except ValueError as exc:
            raise ValueError(f"source path escapes source root: '{source_relpath}'") from exc
        if not source_path.is_file():
            raise ValueError(f"source file not found: '{source_relpath}'")
        return source_path

    @staticmethod
    def _ensure_unique_node_ids(graph: CBTGraph) -> None:
        ids = [
            *(domain.domain_id for domain in graph.domains),
            *(document.node_id for document in graph.documents),
            *(section.section_id for section in graph.sections),
            *(concept.node_id for concept in graph.concepts),
            *(mapping.node_id for mapping in graph.situation_mappings),
        ]
        if len(ids) != len(set(ids)):
            raise ValueError("CBT graph contains duplicate node IDs")

    @staticmethod
    def _load_json(path: Path) -> Any:
        if not path.is_file():
            raise ValueError(f"required CBT data file not found: {path}")
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            raise ValueError(f"invalid JSON in {path}: {exc}") from exc

    def _load_collection(self, path: Path) -> list[dict[str, Any]]:
        raw = self._load_json(path)
        if isinstance(raw, dict):
            raw = raw.get("items", [])
        if not isinstance(raw, list):
            raise ValueError(f"{path.name} must contain a list or an object with an items list")
        return raw

    @staticmethod
    def _require_string(raw: dict[str, Any], key: str, context: str) -> str:
        value = raw.get(key)
        if not isinstance(value, str) or not value.strip():
            raise ValueError(f"{context} requires a non-empty string '{key}'")
        return value.strip()

    @staticmethod
    def _require_int(raw: dict[str, Any], key: str, context: str) -> int:
        value = raw.get(key)
        if not isinstance(value, int) or value < 0:
            raise ValueError(f"{context} requires a non-negative integer '{key}'")
        return value

    @staticmethod
    def _string_list(value: Any, context: str) -> list[str]:
        if not isinstance(value, list) or any(not isinstance(item, str) for item in value):
            raise ValueError(f"{context} requires a list of strings")
        return [item.strip() for item in value if item.strip()]
