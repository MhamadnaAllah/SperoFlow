"""
JsonParser — Parses roadmap.sh JSON files into RoadmapNode/RoadmapEdge objects.

Extracts only meaningful nodes (topic, subtopic) and "bridges" edges across
UI-layout nodes using BFS, producing a clean knowledge graph.
"""

from __future__ import annotations

import json
import logging
import re
from collections import defaultdict, deque
from pathlib import Path

from speroflow_ai.config import get_settings
from speroflow_ai.models.graph import ContentEntry, RoadmapEdge, RoadmapNode

logger = logging.getLogger(__name__)

NODE_ID_PATTERN = re.compile(r"@([^@]+)$")
H1_PATTERN = re.compile(r"^#\s+(.+)", re.MULTILINE)


class JsonParser:
    """
    Parses a roadmap.sh JSON file into meaningful RoadmapNode/RoadmapEdge objects.

    Edge bridging example:
        (Topic A) ---> [UI Group Node] ---> (Topic B)
        Becomes: (Topic A) ---> (Topic B)
    """

    def parse(
        self, json_path: Path, roadmap_name: str
    ) -> tuple[list[RoadmapNode], list[RoadmapEdge]]:
        try:
            with open(json_path, "r", encoding="utf-8") as f:
                data = json.load(f)
        except (json.JSONDecodeError, FileNotFoundError) as exc:
            logger.error("Failed to parse JSON at %s: %s", json_path, exc)
            return [], []

        settings = get_settings()
        raw_nodes = data.get("nodes", [])
        raw_edges = data.get("edges", [])
        meaningful_ids = {n["id"] for n in raw_nodes if n.get("type") in settings.meaningful_node_types}
        ui_ids = {n["id"] for n in raw_nodes if n.get("type") in settings.ui_layout_node_types}

        nodes = self._extract_meaningful_nodes(raw_nodes, roadmap_name)
        logger.info(
            "[%s] Extracted %d meaningful nodes (%d filtered as UI layout)",
            roadmap_name, len(nodes), len(ui_ids),
        )

        edges = self._bridge_edges(raw_edges, meaningful_ids, ui_ids, roadmap_name)
        logger.info(
            "[%s] Resolved %d bridged edges from %d raw edges",
            roadmap_name, len(edges), len(raw_edges),
        )
        return nodes, edges

    def _extract_meaningful_nodes(
        self, raw_nodes: list[dict], roadmap_name: str
    ) -> list[RoadmapNode]:
        settings = get_settings()
        nodes = []
        for raw in raw_nodes:
            node_type = raw.get("type", "")
            if node_type not in settings.meaningful_node_types:
                continue
            data_block = raw.get("data", {})
            label_text = data_block.get("label", "").strip()
            href = data_block.get("href", None)
            if not label_text:
                continue
            nodes.append(
                RoadmapNode(
                    node_id=raw["id"],
                    node_type=node_type,
                    label_text=label_text,
                    roadmap_name=roadmap_name,
                    url=href,
                )
            )
        return nodes

    def _bridge_edges(
        self,
        raw_edges: list[dict],
        meaningful_ids: set[str],
        ui_ids: set[str],
        roadmap_name: str,
    ) -> list[RoadmapEdge]:
        adjacency: dict[str, set[str]] = defaultdict(set)
        for e in raw_edges:
            src, tgt = e.get("source", ""), e.get("target", "")
            if src and tgt:
                adjacency[src].add(tgt)
                adjacency[tgt].add(src)

        seen_edges: set[tuple[str, str]] = set()
        edges: list[RoadmapEdge] = []

        for raw_edge in raw_edges:
            src = raw_edge.get("source", "")
            tgt = raw_edge.get("target", "")
            style = raw_edge.get("data", {}).get("edgeStyle", "solid")
            if not src or not tgt:
                continue

            src_m = src in meaningful_ids
            tgt_m = tgt in meaningful_ids

            if src_m and tgt_m:
                key = (src, tgt)
                if key not in seen_edges:
                    seen_edges.add(key)
                    edges.append(self._make_edge(src, tgt, style))

            elif src_m and not tgt_m:
                for m_id in self._bfs_to_meaningful(tgt, adjacency, meaningful_ids, ui_ids):
                    if m_id != src:
                        key = (src, m_id)
                        if key not in seen_edges:
                            seen_edges.add(key)
                            edges.append(self._make_edge(src, m_id, style))

            elif not src_m and tgt_m:
                for m_id in self._bfs_to_meaningful(src, adjacency, meaningful_ids, ui_ids):
                    if m_id != tgt:
                        key = (m_id, tgt)
                        if key not in seen_edges:
                            seen_edges.add(key)
                            edges.append(self._make_edge(m_id, tgt, style))

        return edges

    def _bfs_to_meaningful(
        self,
        start_id: str,
        adjacency: dict[str, set[str]],
        meaningful_ids: set[str],
        ui_ids: set[str],
    ) -> list[str]:
        visited: set[str] = set()
        queue = deque([start_id])
        found: list[str] = []
        while queue:
            current = queue.popleft()
            if current in visited:
                continue
            visited.add(current)
            for neighbor in adjacency.get(current, set()):
                if neighbor in visited:
                    continue
                if neighbor in meaningful_ids:
                    found.append(neighbor)
                elif neighbor in ui_ids:
                    queue.append(neighbor)
        return found

    @staticmethod
    def _make_edge(source_id: str, target_id: str, style: str) -> RoadmapEdge:
        settings = get_settings()
        rel_type = settings.edge_style_map.get(style, settings.default_relationship_type)
        return RoadmapEdge(
            source_id=source_id,
            target_id=target_id,
            edge_style=style,
            relationship_type=rel_type,
        )
