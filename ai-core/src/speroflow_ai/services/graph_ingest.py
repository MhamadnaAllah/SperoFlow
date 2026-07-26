"""
Graph ingestion service — parses roadmap data and writes it to Neo4j Aura.

Supports two modes:
  - JSON+Markdown: parse the structured roadmap.sh JSON file + content markdown
  - Text: create a single Topic node from raw free-form text

Both modes are idempotent (MERGE semantics).
"""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Optional

from neo4j import Driver, ManagedTransaction

from speroflow_ai.models.graph import Roadmap, RoadmapEdge, RoadmapNode
from speroflow_ai.parsers import JsonParser, MarkdownParser

logger = logging.getLogger("speroflow.services.graph_ingest")

BATCH_SIZE = 500

CONSTRAINT_STATEMENTS = [
    "CREATE CONSTRAINT unique_topic_id IF NOT EXISTS FOR (n:Topic) REQUIRE n.node_id IS UNIQUE",
    "CREATE CONSTRAINT unique_subtopic_id IF NOT EXISTS FOR (n:Subtopic) REQUIRE n.node_id IS UNIQUE",
    "CREATE CONSTRAINT unique_roadmap_name IF NOT EXISTS FOR (r:Roadmap) REQUIRE r.roadmap_name IS UNIQUE",
]

INDEX_STATEMENTS = [
    "CREATE INDEX idx_topic_roadmap IF NOT EXISTS FOR (n:Topic) ON (n.roadmap_name)",
    "CREATE INDEX idx_subtopic_roadmap IF NOT EXISTS FOR (n:Subtopic) ON (n.roadmap_name)",
    "CREATE INDEX idx_topic_label IF NOT EXISTS FOR (n:Topic) ON (n.label_text)",
    "CREATE INDEX idx_subtopic_label IF NOT EXISTS FOR (n:Subtopic) ON (n.label_text)",
]


class SchemaManager:
    """Creates Neo4j constraints and indexes."""

    def __init__(self, driver: Driver) -> None:
        self._driver = driver

    def create_constraints_and_indexes(self) -> None:
        logger.info("Creating Neo4j schema constraints and indexes...")
        with self._driver.session() as session:
            for stmt in CONSTRAINT_STATEMENTS + INDEX_STATEMENTS:
                try:
                    session.run(stmt)
                except Exception as exc:
                    logger.warning("Schema warning: %s", exc)
        logger.info("Schema setup complete.")

    def clear_database(self) -> None:
        logger.warning("Clearing entire Neo4j database...")
        with self._driver.session() as session:
            try:
                session.run(
                    "CALL apoc.periodic.iterate('MATCH (n) RETURN n', "
                    "'DETACH DELETE n', {batchSize: 5000, parallel: false})"
                )
            except Exception:
                session.run("MATCH (n) DETACH DELETE n")
        logger.info("Database cleared.")


class GraphIngester:
    """
    High-performance batch ingestion of Roadmap objects into Neo4j.

    Uses UNWIND for batching and MERGE for idempotency.
    """

    def __init__(self, driver: Driver, batch_size: int = BATCH_SIZE) -> None:
        self._driver = driver
        self._batch_size = batch_size

    def ingest_roadmap(self, roadmap: Roadmap) -> dict[str, int]:
        stats = {"roadmap": 0, "topics": 0, "subtopics": 0, "edges": 0}
        with self._driver.session() as session:
            session.execute_write(self._merge_roadmap, roadmap.name)
            stats["roadmap"] = 1

            topics = [n for n in roadmap.nodes if n.node_type == "topic"]
            if topics:
                for batch in self._batched(topics):
                    session.execute_write(self._ingest_topic_batch, batch)
                stats["topics"] = len(topics)
                for batch in self._batched([t.node_id for t in topics]):
                    session.execute_write(self._create_contains_edges, roadmap.name, batch)

            subtopics = [n for n in roadmap.nodes if n.node_type == "subtopic"]
            if subtopics:
                for batch in self._batched(subtopics):
                    session.execute_write(self._ingest_subtopic_batch, batch)
                stats["subtopics"] = len(subtopics)

            if roadmap.edges:
                for batch in self._batched(roadmap.edges):
                    session.execute_write(self._ingest_edge_batch, batch)
                stats["edges"] = len(roadmap.edges)

        logger.info(
            "[%s] Ingested: %d topics, %d subtopics, %d edges",
            roadmap.name, stats["topics"], stats["subtopics"], stats["edges"],
        )
        return stats

    @staticmethod
    def _merge_roadmap(tx: ManagedTransaction, roadmap_name: str) -> None:
        tx.run(
            "MERGE (r:Roadmap {roadmap_name: $name}) "
            "ON CREATE SET r.created_at = datetime() "
            "ON MATCH SET r.updated_at = datetime()",
            name=roadmap_name,
        )

    @staticmethod
    def _ingest_topic_batch(
        tx: ManagedTransaction, nodes: list[RoadmapNode]
    ) -> None:
        tx.run(
            "UNWIND $nodes AS node MERGE (t:Topic {node_id: node.node_id}) "
            "SET t.label_text = node.label_text, t.roadmap_name = node.roadmap_name, "
            "t.content = node.content, t.url = node.url",
            nodes=[n.to_dict() for n in nodes],
        )

    @staticmethod
    def _ingest_subtopic_batch(
        tx: ManagedTransaction, nodes: list[RoadmapNode]
    ) -> None:
        tx.run(
            "UNWIND $nodes AS node MERGE (s:Subtopic {node_id: node.node_id}) "
            "SET s.label_text = node.label_text, s.roadmap_name = node.roadmap_name, "
            "s.content = node.content, s.url = node.url",
            nodes=[n.to_dict() for n in nodes],
        )

    @staticmethod
    def _create_contains_edges(
        tx: ManagedTransaction, roadmap_name: str, topic_ids: list[str]
    ) -> None:
        tx.run(
            "UNWIND $topic_ids AS tid "
            "MATCH (r:Roadmap {roadmap_name: $roadmap_name}) "
            "MATCH (t:Topic {node_id: tid}) "
            "MERGE (r)-[:CONTAINS]->(t)",
            roadmap_name=roadmap_name,
            topic_ids=topic_ids,
        )

    @staticmethod
    def _ingest_edge_batch(tx: ManagedTransaction, edges: list[RoadmapEdge]) -> None:
        edge_dicts = [e.to_dict() for e in edges]
        try:
            tx.run(
                "UNWIND $edges AS edge "
                "MATCH (src) WHERE src.node_id = edge.source_id AND (src:Topic OR src:Subtopic) "
                "MATCH (tgt) WHERE tgt.node_id = edge.target_id AND (tgt:Topic OR tgt:Subtopic) "
                "CALL apoc.merge.relationship(src, edge.relationship_type, {}, {}, tgt, {}) "
                "YIELD rel RETURN count(rel)",
                edges=edge_dicts,
            )
        except Exception:
            # Fallback without APOC
            leads_to = [e for e in edge_dicts if e["relationship_type"] == "LEADS_TO"]
            related_to = [e for e in edge_dicts if e["relationship_type"] == "RELATED_TO"]
            if leads_to:
                tx.run(
                    "UNWIND $edges AS edge "
                    "MATCH (src) WHERE src.node_id = edge.source_id AND (src:Topic OR src:Subtopic) "
                    "MATCH (tgt) WHERE tgt.node_id = edge.target_id AND (tgt:Topic OR tgt:Subtopic) "
                    "MERGE (src)-[:LEADS_TO]->(tgt)",
                    edges=leads_to,
                )
            if related_to:
                tx.run(
                    "UNWIND $edges AS edge "
                    "MATCH (src) WHERE src.node_id = edge.source_id AND (src:Topic OR src:Subtopic) "
                    "MATCH (tgt) WHERE tgt.node_id = edge.target_id AND (tgt:Topic OR tgt:Subtopic) "
                    "MERGE (src)-[:RELATED_TO]->(tgt)",
                    edges=related_to,
                )

    def _batched(self, items: list) -> list[list]:
        return [items[i : i + self._batch_size] for i in range(0, len(items), self._batch_size)]


def ingest_text_as_node(
    driver: Driver,
    roadmap_name: str,
    text: str,
) -> dict[str, int]:
    """
    Create a single Topic node from raw text input.
    Used when source_type='text' in the /api/ingest endpoint.
    """
    import hashlib
    node_id = f"text-{hashlib.md5(text[:100].encode(), usedforsecurity=False).hexdigest()[:12]}"
    label_text = text.split("\n")[0][:120].strip() or roadmap_name

    node = RoadmapNode(
        node_id=node_id,
        node_type="topic",
        label_text=label_text,
        roadmap_name=roadmap_name,
        content=text,
    )
    roadmap = Roadmap(name=roadmap_name, nodes=[node], edges=[])

    ingester = GraphIngester(driver)
    stats = ingester.ingest_roadmap(roadmap)
    return stats
