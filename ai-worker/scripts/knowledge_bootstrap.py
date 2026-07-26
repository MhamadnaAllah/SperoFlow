"""Explicit, idempotent worker command for curated roadmap and CBT releases."""

from __future__ import annotations

import argparse
import logging
from pathlib import Path

from neo4j import GraphDatabase

from speroflow_ai.config import get_settings
from speroflow_ai.models.graph import Roadmap, RoadmapNode
from speroflow_ai.parsers import CBTParser, JsonParser, MarkdownParser
from speroflow_ai.services.cbt_ingest import CBTGraphIngester
from speroflow_ai.services.graph_embed import EmbeddingPipeline
from speroflow_ai.services.graph_ingest import GraphIngester, SchemaManager


logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)-7s | %(name)s | %(message)s",
    datefmt="%H:%M:%S",
)
logger = logging.getLogger("speroflow.knowledge_bootstrap")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Ingest a reviewed, versioned SperoFlow knowledge release.")
    parser.add_argument("--knowledge-root", type=Path, default=Path("/knowledge-base"))
    parser.add_argument("--embed", action="store_true", help="Generate vectors after idempotent graph ingestion.")
    parser.add_argument("--device", choices=("cpu", "cuda", "mps"), default="cpu")
    parser.add_argument("--skip-roadmaps", action="store_true")
    parser.add_argument("--skip-cbt", action="store_true")
    parser.add_argument("--confirm-knowledge-release", action="store_true", help="Required acknowledgement before graph writes.")
    parser.add_argument("--confirm-license-permission", action="store_true", help="Required when ingesting CBT source material.")
    return parser.parse_args()


def _find_roadmap_json(roadmap_dir: Path) -> Path | None:
    primary = roadmap_dir / f"{roadmap_dir.name}.json"
    if primary.is_file():
        return primary
    return next((path for path in sorted(roadmap_dir.glob("*.json")) if "migration" not in path.name.casefold()), None)


def _load_roadmap(roadmap_dir: Path) -> Roadmap | None:
    roadmap_name = roadmap_dir.name
    parser = JsonParser()
    markdown = MarkdownParser()
    json_path = _find_roadmap_json(roadmap_dir)
    if json_path is not None:
        nodes, edges = parser.parse(json_path, roadmap_name)
        content = markdown.parse_content_dir(roadmap_dir / "content")
        for node in nodes:
            node.content = content.get(node.node_id, node.content)
        return Roadmap(name=roadmap_name, nodes=nodes, edges=edges)

    content_nodes = markdown.parse_content_as_nodes(roadmap_dir / "content")
    if not content_nodes:
        return None
    nodes = [
        RoadmapNode(
            node_id=item.node_id,
            node_type="topic",
            label_text=item.label,
            roadmap_name=roadmap_name,
            content=item.content,
        )
        for item in content_nodes
    ]
    return Roadmap(name=roadmap_name, nodes=nodes, edges=[])


def ingest_roadmaps(driver, roadmaps_dir: Path, *, embed: bool, device: str) -> dict[str, int]:
    if not roadmaps_dir.is_dir():
        raise ValueError(f"Roadmap source directory does not exist: {roadmaps_dir}")
    SchemaManager(driver).create_constraints_and_indexes()
    ingester = GraphIngester(driver)
    totals = {"roadmaps": 0, "topics": 0, "subtopics": 0, "edges": 0, "embedded": 0}
    for roadmap_dir in sorted(path for path in roadmaps_dir.iterdir() if path.is_dir()):
        roadmap = _load_roadmap(roadmap_dir)
        if roadmap is None or not roadmap.nodes:
            logger.warning("Skipping empty roadmap source: %s", roadmap_dir.name)
            continue
        stats = ingester.ingest_roadmap(roadmap)
        totals["roadmaps"] += 1
        totals["topics"] += stats.get("topics", 0)
        totals["subtopics"] += stats.get("subtopics", 0)
        totals["edges"] += stats.get("edges", 0)
    if embed:
        for label in ("Topic", "Subtopic"):
            result = EmbeddingPipeline(driver=driver, node_label=label, device=device).run()
            totals["embedded"] += result.get("embedded", 0)
    return totals


def ingest_cbt(driver, graph_dir: Path, source_dir: Path, *, embed: bool, device: str) -> dict[str, int]:
    graph = CBTParser(data_dir=graph_dir, source_root=source_dir).parse()
    ingester = CBTGraphIngester(driver, batch_size=get_settings().batch_size)
    ingester.ensure_schema()
    totals = ingester.ingest_graph(graph)
    totals["embedded"] = 0
    if embed:
        for label in ("CBTSection", "CBTDocument"):
            result = EmbeddingPipeline(driver=driver, node_label=label, device=device).run()
            totals["embedded"] += result.get("embedded", 0)
    return totals


def main() -> None:
    args = parse_args()
    if not args.confirm_knowledge_release:
        raise SystemExit("Refusing graph writes without --confirm-knowledge-release.")
    if not args.skip_cbt and not args.confirm_license_permission:
        raise SystemExit("Refusing CBT ingestion without --confirm-license-permission.")

    root = args.knowledge_root.resolve()
    manifest = root / "manifest.json"
    if not manifest.is_file():
        raise SystemExit(f"Knowledge manifest is required: {manifest}")
    settings = get_settings()
    driver = GraphDatabase.driver(settings.neo4j_uri, auth=(settings.neo4j_user, settings.neo4j_password))
    try:
        driver.verify_connectivity()
        if not args.skip_roadmaps:
            stats = ingest_roadmaps(driver, root / "roadmaps", embed=args.embed, device=args.device)
            logger.info("Roadmap release ingested: %s", stats)
        if not args.skip_cbt:
            stats = ingest_cbt(driver, root / "cbt" / "graph", root / "cbt" / "source", embed=args.embed, device=args.device)
            logger.info("CBT release ingested: %s", stats)
    finally:
        driver.close()


if __name__ == "__main__":
    main()