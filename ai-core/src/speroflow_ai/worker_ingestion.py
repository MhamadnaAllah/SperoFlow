"""Synchronous graph-write pipeline used only by the ai-worker process."""

from __future__ import annotations

import json
import tempfile
from pathlib import Path
from typing import Any

from speroflow_ai.config import Settings


def ingest_content(
    settings: Settings,
    roadmap_name: str,
    source_type: str,
    content: str,
) -> dict[str, int]:
    """Run an idempotent ingestion plus embedding pass off the API event loop."""
    from neo4j import GraphDatabase

    sync_driver = GraphDatabase.driver(
        settings.neo4j_uri,
        auth=(settings.neo4j_user, settings.neo4j_password),
    )
    try:
        from speroflow_ai.models.graph import Roadmap
        from speroflow_ai.parsers import JsonParser
        from speroflow_ai.services.graph_embed import EmbeddingPipeline
        from speroflow_ai.services.graph_ingest import GraphIngester, SchemaManager, ingest_text_as_node

        SchemaManager(sync_driver).create_constraints_and_indexes()
        nodes_created = 0
        edges_created = 0
        if source_type == "json":
            try:
                data = json.loads(content)
            except json.JSONDecodeError as exc:
                raise ValueError("Document source_type=json requires valid JSON.") from exc

            with tempfile.NamedTemporaryFile(suffix=".json", mode="w", encoding="utf-8", delete=False) as file:
                json.dump(data, file)
                source_path = Path(file.name)
            try:
                nodes, edges = JsonParser().parse(source_path, roadmap_name)
            finally:
                source_path.unlink(missing_ok=True)

            stats = GraphIngester(sync_driver).ingest_roadmap(Roadmap(name=roadmap_name, nodes=nodes, edges=edges))
            nodes_created = stats.get("topics", 0) + stats.get("subtopics", 0)
            edges_created = stats.get("edges", 0)
        else:
            stats = ingest_text_as_node(sync_driver, roadmap_name, content)
            nodes_created = stats.get("topics", 0)

        vectors_embedded = 0
        for label in ("Topic", "Subtopic"):
            result = EmbeddingPipeline(
                driver=sync_driver,
                node_label=label,
                device="cpu",
            ).run()
            vectors_embedded += result.get("embedded", 0)
        return {
            "nodes_created": nodes_created,
            "edges_created": edges_created,
            "vectors_embedded": vectors_embedded,
        }
    finally:
        sync_driver.close()
