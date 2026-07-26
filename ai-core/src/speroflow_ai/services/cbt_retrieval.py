"""Bounded, source-grounded retrieval for reviewed CBT educational resources."""

from __future__ import annotations

import logging
from collections.abc import Awaitable, Callable
from dataclasses import dataclass

from neo4j import AsyncDriver


logger = logging.getLogger("speroflow.services.cbt_retrieval")

MAX_TOP_K = 10
MAX_EXCERPT_CHARACTERS = 1200
EmbeddingGenerator = Callable[[str, str], Awaitable[list[float]]]


@dataclass(frozen=True)
class CBTResource:
    """A cited educational resource returned without clinical synthesis."""

    node_id: str
    title: str
    domain_id: str
    domain_name: str
    document_type: str
    source_relpath: str
    source_url: str
    score: float
    retrieval_scope: str = "document"
    section_id: str = ""
    section_title: str = ""
    parent_section_title: str = ""
    source_anchor: str = ""
    content_excerpt: str = ""
    reviewed_concepts: tuple[str, ...] = ()


class CBTResourceRetriever:
    """Retrieve source sections first, with a document-level compatibility fallback.

    The graph traversal is fixed and parameterized. It may expose only
    clinician-approved concepts that are explicitly linked to a retrieved
    source section; it never infers a fact about the querying user.
    """

    def __init__(
        self,
        driver: AsyncDriver,
        embedding_model: str,
        index_name: str,
        database: str,
        min_similarity: float,
        document_index_name: str = "cbtdocument_embedding_index",
        embedding_generator: EmbeddingGenerator | None = None,
    ) -> None:
        self._driver = driver
        self._embedding_model = embedding_model
        self._section_index_name = index_name
        self._document_index_name = document_index_name
        self._database = database
        self._min_similarity = min_similarity
        self._embedding_generator = embedding_generator

    async def search(
        self,
        query: str,
        top_k: int,
        domain_ids: list[str] | None = None,
        excerpt_characters: int = 0,
    ) -> list[CBTResource]:
        """Return cited source records for a non-diagnostic educational query."""
        normalized_query = query.strip()
        if not normalized_query:
            raise ValueError("CBT query must not be empty")
        if not 1 <= top_k <= MAX_TOP_K:
            raise ValueError(f"CBT top_k must be between 1 and {MAX_TOP_K}")
        if not 0 <= excerpt_characters <= MAX_EXCERPT_CHARACTERS:
            raise ValueError(
                f"CBT excerpt_characters must be between 0 and {MAX_EXCERPT_CHARACTERS}"
            )

        embedding = await self._generate_query_embedding(normalized_query)
        candidate_count = min(top_k * 4, 40)
        normalized_domains = domain_ids or []

        section_error: Exception | None = None
        try:
            section_rows = await self._search_sections(
                embedding=embedding,
                candidate_count=candidate_count,
                top_k=top_k,
                domain_ids=normalized_domains,
                excerpt_characters=excerpt_characters,
            )
        except Exception as exc:
            # A deployment can be upgraded before section embeddings finish.
            # Document retrieval remains a compatibility fallback in that state.
            logger.warning("CBT section retrieval unavailable; trying document fallback: %s", exc)
            section_error = exc
            section_rows = []

        if section_rows:
            return self._to_section_resources(section_rows)

        try:
            document_rows = await self._search_documents(
                embedding=embedding,
                candidate_count=candidate_count,
                top_k=top_k,
                domain_ids=normalized_domains,
            )
        except Exception as exc:
            if section_error is not None:
                raise RuntimeError("CBT vector retrieval is unavailable") from exc
            logger.warning("CBT document fallback unavailable: %s", exc)
            return []

        return self._to_document_resources(document_rows)

    async def _generate_query_embedding(self, query: str) -> list[float]:
        """Generate one query vector without forcing the embedding stack in tests."""
        if self._embedding_generator is not None:
            return await self._embedding_generator(query, self._embedding_model)

        # Import the heavyweight embedding stack only when a released request
        # needs vector generation. The production function offloads CPU work.
        from speroflow_ai.services.embedding import generate_embedding

        return await generate_embedding(query, self._embedding_model)

    async def _search_sections(
        self,
        *,
        embedding: list[float],
        candidate_count: int,
        top_k: int,
        domain_ids: list[str],
        excerpt_characters: int,
    ) -> list[dict]:
        cypher = """
            CALL db.index.vector.queryNodes($index_name, $candidate_count, $embedding)
            YIELD node, score
            MATCH (document:CBTDocument)-[:CONTAINS]->(node:CBTSection)
            MATCH (domain:CBTDomain)-[:CONTAINS]->(document)
            WHERE score >= $min_similarity
              AND (size($domain_ids) = 0 OR domain.domain_id IN $domain_ids)
            OPTIONAL MATCH (parent:CBTSection)-[:CONTAINS]->(node)
            OPTIONAL MATCH (node)-[:MENTIONS|TEACHES|PRACTICES]->(
                concept:CBTConcept {review_status: "approved"}
            )
            WITH node, score, document, domain, parent,
                 collect(DISTINCT concept.name) AS reviewed_concepts
            RETURN
                document.node_id AS node_id,
                document.title AS title,
                document.domain_id AS domain_id,
                domain.name AS domain_name,
                document.document_type AS document_type,
                document.source_relpath AS source_relpath,
                document.source_url AS source_url,
                node.section_id AS section_id,
                node.title AS section_title,
                coalesce(parent.title, "") AS parent_section_title,
                node.source_anchor AS source_anchor,
                CASE
                    WHEN $excerpt_characters > 0
                    THEN left(coalesce(node.content, ""), $excerpt_characters)
                    ELSE ""
                END AS content_excerpt,
                reviewed_concepts,
                score AS score
            ORDER BY score DESC, section_id
            LIMIT $top_k
        """
        return await self._run_vector_query(
            cypher=cypher,
            index_name=self._section_index_name,
            embedding=embedding,
            candidate_count=candidate_count,
            top_k=top_k,
            domain_ids=domain_ids,
            excerpt_characters=excerpt_characters,
        )

    async def _search_documents(
        self,
        *,
        embedding: list[float],
        candidate_count: int,
        top_k: int,
        domain_ids: list[str],
    ) -> list[dict]:
        cypher = """
            CALL db.index.vector.queryNodes($index_name, $candidate_count, $embedding)
            YIELD node, score
            MATCH (domain:CBTDomain)-[:CONTAINS]->(node:CBTDocument)
            WHERE score >= $min_similarity
              AND (size($domain_ids) = 0 OR domain.domain_id IN $domain_ids)
            RETURN
                node.node_id AS node_id,
                node.title AS title,
                node.domain_id AS domain_id,
                domain.name AS domain_name,
                node.document_type AS document_type,
                node.source_relpath AS source_relpath,
                node.source_url AS source_url,
                score AS score
            ORDER BY score DESC, node_id
            LIMIT $top_k
        """
        return await self._run_vector_query(
            cypher=cypher,
            index_name=self._document_index_name,
            embedding=embedding,
            candidate_count=candidate_count,
            top_k=top_k,
            domain_ids=domain_ids,
            excerpt_characters=0,
        )

    async def _run_vector_query(
        self,
        *,
        cypher: str,
        index_name: str,
        embedding: list[float],
        candidate_count: int,
        top_k: int,
        domain_ids: list[str],
        excerpt_characters: int,
    ) -> list[dict]:
        async with self._driver.session(database=self._database) as session:
            result = await session.run(
                cypher,
                index_name=index_name,
                candidate_count=candidate_count,
                embedding=embedding,
                min_similarity=self._min_similarity,
                domain_ids=domain_ids,
                excerpt_characters=excerpt_characters,
                top_k=top_k,
            )
            return await result.data()

    @staticmethod
    def _to_section_resources(rows: list[dict]) -> list[CBTResource]:
        return [
            CBTResource(
                node_id=row["node_id"],
                title=row["title"],
                domain_id=row["domain_id"],
                domain_name=row["domain_name"],
                document_type=row["document_type"],
                source_relpath=row["source_relpath"],
                source_url=row["source_url"],
                score=float(row["score"]),
                retrieval_scope="section",
                section_id=row.get("section_id") or "",
                section_title=row.get("section_title") or "",
                parent_section_title=row.get("parent_section_title") or "",
                source_anchor=row.get("source_anchor") or "",
                content_excerpt=row.get("content_excerpt") or "",
                reviewed_concepts=tuple(
                    concept
                    for concept in row.get("reviewed_concepts", [])
                    if isinstance(concept, str) and concept
                ),
            )
            for row in rows
        ]

    @staticmethod
    def _to_document_resources(rows: list[dict]) -> list[CBTResource]:
        return [
            CBTResource(
                node_id=row["node_id"],
                title=row["title"],
                domain_id=row["domain_id"],
                domain_name=row["domain_name"],
                document_type=row["document_type"],
                source_relpath=row["source_relpath"],
                source_url=row["source_url"],
                score=float(row["score"]),
            )
            for row in rows
        ]
