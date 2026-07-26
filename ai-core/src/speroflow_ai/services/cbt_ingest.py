"""Idempotent Neo4j ingestion for the reviewed CBT educational-resource graph."""

from __future__ import annotations

import logging
from collections import defaultdict
from collections.abc import Iterable
from typing import TypeVar

from neo4j import Driver, ManagedTransaction

from speroflow_ai.models.cbt_graph import (
    ALLOWED_RELATIONSHIP_TYPES,
    CBTConcept,
    CBTDomain,
    CBTDocument,
    CBTGraph,
    CBTRelationship,
    CBTSection,
    Distortion,
    MicroHabit,
    SituationMapping,
    Technique,
)


logger = logging.getLogger("speroflow.services.cbt_ingest")

BATCH_SIZE = 250

CONSTRAINT_STATEMENTS = (
    "CREATE CONSTRAINT unique_cbt_domain_id IF NOT EXISTS "
    "FOR (n:CBTDomain) REQUIRE n.domain_id IS UNIQUE",
    "CREATE CONSTRAINT unique_cbt_document_id IF NOT EXISTS "
    "FOR (n:CBTDocument) REQUIRE n.node_id IS UNIQUE",
    "CREATE CONSTRAINT unique_cbt_section_id IF NOT EXISTS "
    "FOR (n:CBTSection) REQUIRE n.node_id IS UNIQUE",
    "CREATE CONSTRAINT unique_cbt_concept_id IF NOT EXISTS "
    "FOR (n:CBTConcept) REQUIRE n.node_id IS UNIQUE",
    "CREATE CONSTRAINT unique_cbt_example_id IF NOT EXISTS "
    "FOR (n:CBTExample) REQUIRE n.node_id IS UNIQUE",
)

INDEX_STATEMENTS = (
    "CREATE INDEX idx_cbt_document_domain IF NOT EXISTS "
    "FOR (n:CBTDocument) ON (n.domain_id)",
    "CREATE INDEX idx_cbt_document_type IF NOT EXISTS "
    "FOR (n:CBTDocument) ON (n.document_type)",
    "CREATE INDEX idx_cbt_document_license IF NOT EXISTS "
    "FOR (n:CBTDocument) ON (n.license_status)",
    "CREATE INDEX idx_cbt_section_document IF NOT EXISTS "
    "FOR (n:CBTSection) ON (n.document_id)",
    "CREATE INDEX idx_cbt_section_domain IF NOT EXISTS "
    "FOR (n:CBTSection) ON (n.domain_id)",
    "CREATE INDEX idx_cbt_concept_review IF NOT EXISTS "
    "FOR (n:CBTConcept) ON (n.review_status)",
    "CREATE INDEX idx_cbt_concept_name IF NOT EXISTS "
    "FOR (n:CBTConcept) ON (n.name)",
)

T = TypeVar("T")


class CBTGraphIngester:
    """Write a parsed CBTGraph using safe, repeatable MERGE operations."""

    def __init__(self, driver: Driver, batch_size: int = BATCH_SIZE) -> None:
        self._driver = driver
        self._batch_size = batch_size

    def ensure_schema(self) -> None:
        """Create only the CBT graph schema; roadmap schema remains independent."""
        with self._driver.session() as session:
            for statement in (*CONSTRAINT_STATEMENTS, *INDEX_STATEMENTS):
                try:
                    session.run(statement).consume()
                except Exception as exc:
                    logger.warning("CBT schema statement did not complete: %s", exc)

    def ingest_graph(self, graph: CBTGraph) -> dict[str, int]:
        """Ingest source records plus only approved curated records."""
        publishable_distortions = [item for item in graph.distortions if item.is_publishable]
        publishable_techniques = [item for item in graph.techniques if item.is_publishable]
        publishable_micro_habits = [item for item in graph.micro_habits if item.is_publishable]
        publishable_mappings = [item for item in graph.situation_mappings if item.is_publishable]
        publishable_relationships = [item for item in graph.relationships if item.is_publishable]

        stats = {
            "domains": len(graph.domains),
            "documents": len(graph.documents),
            "sections": len(graph.sections),
            "distortions": len(publishable_distortions),
            "techniques": len(publishable_techniques),
            "micro_habits": len(publishable_micro_habits),
            "examples": len(publishable_mappings),
            "relationships": 0,
        }

        with self._driver.session() as session:
            for batch in self._batches(graph.domains):
                session.execute_write(self._merge_domains, batch)
            for batch in self._batches(graph.documents):
                session.execute_write(self._merge_documents, batch)
            for batch in self._batches(graph.documents):
                session.execute_write(self._merge_contains_edges, batch)
            for batch in self._batches(graph.sections):
                session.execute_write(self._merge_sections, batch)
            for batch in self._batches(graph.sections):
                session.execute_write(self._merge_document_section_edges, batch)
            parented_sections = [section for section in graph.sections if section.parent_section_id]
            for batch in self._batches(parented_sections):
                session.execute_write(self._merge_section_hierarchy_edges, batch)

            self._merge_concept_groups(session, publishable_distortions, "CBTDistortion")
            self._merge_concept_groups(session, publishable_techniques, "CBTTechnique")
            self._merge_concept_groups(session, publishable_micro_habits, "CBTMicroHabit")

            for batch in self._batches(publishable_mappings):
                session.execute_write(self._merge_examples, batch)
            mapping_links = self._mapping_links(publishable_mappings)
            for relationship_type, group in self._relationships_by_type(
                [*publishable_relationships, *mapping_links]
            ).items():
                for batch in self._batches(group):
                    session.execute_write(self._merge_relationships, relationship_type, batch)
                    stats["relationships"] += len(batch)

        logger.info("%s", graph.summary())
        return stats

    def _merge_concept_groups(
        self,
        session: object,
        concepts: list[CBTConcept],
        label: str,
    ) -> None:
        for batch in self._batches(concepts):
            session.execute_write(self._merge_concepts, label, batch)

    @staticmethod
    def _merge_domains(tx: ManagedTransaction, domains: list[CBTDomain]) -> None:
        tx.run(
            "UNWIND $domains AS domain "
            "MERGE (node:CBTDomain {domain_id: domain.domain_id}) "
            "ON CREATE SET node.created_at = datetime() "
            "SET node += domain, node.updated_at = datetime()",
            domains=[domain.to_dict() for domain in domains],
        ).consume()

    @staticmethod
    def _merge_documents(tx: ManagedTransaction, documents: list[CBTDocument]) -> None:
        tx.run(
            "UNWIND $documents AS document "
            "MERGE (node:CBTDocument {node_id: document.node_id}) "
            "ON CREATE SET node.created_at = datetime() "
            "SET node += document, node.updated_at = datetime()",
            documents=[document.to_dict() for document in documents],
        ).consume()

    @staticmethod
    def _merge_contains_edges(tx: ManagedTransaction, documents: list[CBTDocument]) -> None:
        tx.run(
            "UNWIND $documents AS document "
            "MATCH (domain:CBTDomain {domain_id: document.domain_id}) "
            "MATCH (node:CBTDocument {node_id: document.node_id}) "
            "MERGE (domain)-[:CONTAINS]->(node)",
            documents=[document.to_dict() for document in documents],
        ).consume()

    @staticmethod
    def _merge_sections(tx: ManagedTransaction, sections: list[CBTSection]) -> None:
        tx.run(
            "UNWIND $sections AS section "
            "MERGE (node:CBTSection {node_id: section.node_id}) "
            "ON CREATE SET node.created_at = datetime() "
            "SET node += section, node.updated_at = datetime()",
            sections=[section.to_dict() for section in sections],
        ).consume()

    @staticmethod
    def _merge_document_section_edges(tx: ManagedTransaction, sections: list[CBTSection]) -> None:
        tx.run(
            "UNWIND $sections AS section "
            "MATCH (document:CBTDocument {node_id: section.document_id}) "
            "MATCH (node:CBTSection {node_id: section.node_id}) "
            "MERGE (document)-[:CONTAINS]->(node)",
            sections=[section.to_dict() for section in sections],
        ).consume()

    @staticmethod
    def _merge_section_hierarchy_edges(tx: ManagedTransaction, sections: list[CBTSection]) -> None:
        tx.run(
            "UNWIND $sections AS section "
            "MATCH (parent:CBTSection {node_id: section.parent_section_id}) "
            "MATCH (node:CBTSection {node_id: section.node_id}) "
            "MERGE (parent)-[:CONTAINS]->(node)",
            sections=[section.to_dict() for section in sections],
        ).consume()

    @staticmethod
    def _merge_concepts(
        tx: ManagedTransaction,
        label: str,
        concepts: list[CBTConcept],
    ) -> None:
        if label not in {"CBTDistortion", "CBTTechnique", "CBTMicroHabit"}:
            raise ValueError(f"unsupported CBT concept label: {label}")
        tx.run(
            "UNWIND $concepts AS concept "
            "MERGE (node:CBTConcept {node_id: concept.node_id}) "
            "ON CREATE SET node.created_at = datetime() "
            "SET node += concept, node.updated_at = datetime() "
            f"SET node:{label}",
            concepts=[concept.to_dict() for concept in concepts],
        ).consume()

    @staticmethod
    def _merge_examples(tx: ManagedTransaction, mappings: list[SituationMapping]) -> None:
        tx.run(
            "UNWIND $mappings AS mapping "
            "MERGE (node:CBTExample {node_id: mapping.node_id}) "
            "ON CREATE SET node.created_at = datetime() "
            "SET node += mapping, node.updated_at = datetime()",
            mappings=[mapping.to_dict() for mapping in mappings],
        ).consume()

    @staticmethod
    def _merge_relationships(
        tx: ManagedTransaction,
        relationship_type: str,
        relationships: list[CBTRelationship],
    ) -> None:
        if relationship_type not in ALLOWED_RELATIONSHIP_TYPES:
            raise ValueError(f"unsupported CBT relationship type: {relationship_type}")
        tx.run(
            "UNWIND $relationships AS relationship "
            "MATCH (source {node_id: relationship.source_id}) "
            "MATCH (target {node_id: relationship.target_id}) "
            f"MERGE (source)-[edge:{relationship_type}]->(target) "
            "SET edge += relationship, edge.updated_at = datetime()",
            relationships=[relationship.to_dict() for relationship in relationships],
        ).consume()

    @staticmethod
    def _mapping_links(mappings: list[SituationMapping]) -> list[CBTRelationship]:
        links: list[CBTRelationship] = []
        for mapping in mappings:
            base = {
                "source_id": mapping.node_id,
                "source_document_id": mapping.source_document_id,
                "evidence_locator": mapping.evidence_locator,
                "review_status": mapping.review_status,
                "taxonomy_version": mapping.taxonomy_version,
                "reviewed_by": mapping.reviewed_by,
                "reviewed_at": mapping.reviewed_at,
            }
            if mapping.distortion_id:
                links.append(
                    CBTRelationship(
                        target_id=mapping.distortion_id,
                        relationship_type="ILLUSTRATES",
                        **base,
                    )
                )
            if mapping.technique_id:
                links.append(
                    CBTRelationship(
                        target_id=mapping.technique_id,
                        relationship_type="SUGGESTS_RESOURCE",
                        **base,
                    )
                )
        return links

    @staticmethod
    def _relationships_by_type(
        relationships: list[CBTRelationship],
    ) -> dict[str, list[CBTRelationship]]:
        grouped: dict[str, list[CBTRelationship]] = defaultdict(list)
        for relationship in relationships:
            if relationship.relationship_type not in ALLOWED_RELATIONSHIP_TYPES:
                raise ValueError(
                    f"unsupported CBT relationship type: {relationship.relationship_type}"
                )
            grouped[relationship.relationship_type].append(relationship)
        return grouped

    def _batches(self, values: list[T]) -> Iterable[list[T]]:
        for offset in range(0, len(values), self._batch_size):
            yield values[offset : offset + self._batch_size]
