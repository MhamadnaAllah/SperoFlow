"""
Data models — single source of truth for all pipeline data structures.

Groups:
  1. Knowledge Graph Models  — RoadmapNode, RoadmapEdge, Roadmap
  2. Content Parsing Models  — ContentEntry
  3. RAG Pipeline Models     — VectorMatch, RAGResult
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, NamedTuple, Optional


# ─────────────────────────────────────────────────────────────────────────────
# 1. KNOWLEDGE GRAPH MODELS
# ─────────────────────────────────────────────────────────────────────────────

@dataclass
class RoadmapNode:
    """A single topic or subtopic extracted from a roadmap JSON file."""

    node_id: str
    node_type: str          # "topic" or "subtopic"
    label_text: str
    roadmap_name: str
    content: Optional[str] = None
    url: Optional[str] = None

    @property
    def neo4j_label(self) -> str:
        return self.node_type.capitalize()

    def to_dict(self) -> dict:
        return {
            "node_id": self.node_id,
            "label_text": self.label_text,
            "roadmap_name": self.roadmap_name,
            "content": self.content or "",
            "url": self.url or "",
        }


@dataclass
class RoadmapEdge:
    """A directed relationship between two RoadmapNodes."""

    source_id: str
    target_id: str
    edge_style: str           # "solid" or "dashed"
    relationship_type: str    # "LEADS_TO" or "RELATED_TO"

    def to_dict(self) -> dict:
        return {
            "source_id": self.source_id,
            "target_id": self.target_id,
            "relationship_type": self.relationship_type,
        }


@dataclass
class Roadmap:
    """An entire roadmap: its name, all nodes, and all edges."""

    name: str
    nodes: list[RoadmapNode] = field(default_factory=list)
    edges: list[RoadmapEdge] = field(default_factory=list)

    @property
    def topic_count(self) -> int:
        return sum(1 for n in self.nodes if n.node_type == "topic")

    @property
    def subtopic_count(self) -> int:
        return sum(1 for n in self.nodes if n.node_type == "subtopic")

    @property
    def content_count(self) -> int:
        return sum(1 for n in self.nodes if n.content)

    def summary(self) -> str:
        return (
            f"[{self.name}] "
            f"Topics: {self.topic_count}, "
            f"Subtopics: {self.subtopic_count}, "
            f"Edges: {len(self.edges)}, "
            f"Content-matched: {self.content_count}/{len(self.nodes)}"
        )


# ─────────────────────────────────────────────────────────────────────────────
# 2. CONTENT PARSING MODELS
# ─────────────────────────────────────────────────────────────────────────────

class ContentEntry(NamedTuple):
    """One parsed markdown file ready to be matched to a RoadmapNode."""

    node_id: str
    label: str
    content: str


# ─────────────────────────────────────────────────────────────────────────────
# 3. RAG PIPELINE MODELS
# ─────────────────────────────────────────────────────────────────────────────

@dataclass
class VectorMatch:
    """A single hit returned by the vector similarity search."""

    node_id: str
    label_text: str
    roadmap_name: str
    score: float
    content_snippet: str = ""
    neighbors: list[dict[str, Any]] = field(default_factory=list)

    def __repr__(self) -> str:
        return (
            f"VectorMatch(label='{self.label_text}', "
            f"roadmap='{self.roadmap_name}', score={self.score:.4f})"
        )


@dataclass
class RAGResult:
    """The final output of a Hybrid RAG query — answer plus full diagnostics."""

    answer: str
    strategy_used: str = "hybrid"
    sources: list[str] = field(default_factory=list)
    vector_matches: list[VectorMatch] = field(default_factory=list)
    generated_cypher: Optional[str] = None
    cypher_results: list[dict[str, Any]] = field(default_factory=list)
    intermediate_steps: list[Any] = field(default_factory=list)
    error: Optional[str] = None

    @property
    def success(self) -> bool:
        return self.error is None and bool(self.answer)
