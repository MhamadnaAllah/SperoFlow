"""CBT Graph RAG Pipeline — LLM-powered advice and habit generation.

Retrieves CBT educational content from the Neo4j knowledge graph
(documents, sections, distortions, techniques, micro-habits) and uses
Gemma4 via Amazon Bedrock to synthesize personalized advice and
actionable habits grounded in the retrieved source material.

This module IS an LLM synthesis layer (unlike cbt_rag.py which is
purely retrieval-only). It adds clinical safety guards and
source-grounding requirements.
"""

from __future__ import annotations

import asyncio
import json
import logging
import re
from dataclasses import dataclass, field
from typing import Any, Optional

from speroflow_ai.config import Settings
from speroflow_ai.services.bedrock_client import invoke_bedrock
from speroflow_ai.services.cbt_safety import evaluate_urgent_support_signal

logger = logging.getLogger("speroflow.services.cbt_graph_rag")


# ── Data Models ───────────────────────────────────────────────────────────────

@dataclass(frozen=True)
class CBTAdviceSource:
    """A source reference backing a piece of generated advice."""

    node_id: str
    title: str
    domain: str
    document_type: str
    score: float
    content_excerpt: str = ""


@dataclass(frozen=True)
class CBTHabit:
    """A single actionable micro-habit recommendation."""

    title: str
    description: str
    frequency: str
    duration_minutes: int
    domain: str
    source_title: str


@dataclass
class CBTGraphRAGResult:
    """Complete result from the CBT Graph RAG pipeline."""

    status: str  # "ok", "urgent_support", "no_results", "error"
    advice: str = ""
    habits: list[CBTHabit] = field(default_factory=list)
    detected_themes: list[str] = field(default_factory=list)
    sources: list[CBTAdviceSource] = field(default_factory=list)
    model_used: str = ""
    disclaimer: str = ""
    urgent_support: bool = False
    urgent_support_message: str = ""
    error: Optional[str] = None


EDUCATIONAL_DISCLAIMER = (
    "This advice is based on evidence-based CBT educational materials and is for "
    "informational purposes only. It is not a diagnosis, treatment plan, or substitute "
    "for a qualified mental-health professional. If you are in crisis, please contact "
    "local emergency services."
)

# ── Prompt Templates ──────────────────────────────────────────────────────────

CBT_ADVICE_SYSTEM_PROMPT = """\
You are SperoFlow's CBT Wellness Advisor — a compassionate, evidence-based \
mental health education assistant. Your role is to provide supportive, \
actionable advice and micro-habit recommendations grounded EXCLUSIVELY in \
the retrieved CBT educational materials provided below.

=== STRICT RULES ===
1. ONLY use information from the retrieved context below. Do NOT invent facts.
2. Be warm, empathetic, and encouraging — never clinical or diagnostic.
3. NEVER diagnose conditions or suggest medication.
4. ALWAYS recommend consulting a mental-health professional for persistent concerns.
5. Frame advice as educational suggestions, not prescriptions.
6. Include specific, small, achievable daily habits the user can start today.
7. Reference the source material when giving advice (e.g. "According to the CBT materials on...").

=== OUTPUT FORMAT ===
Return a JSON object with exactly these keys:
{
  "themes": ["list of 1-3 identified emotional/behavioral themes"],
  "advice": "A warm, structured response with empathetic acknowledgment, CBT-grounded insights, and actionable steps. Use markdown formatting for readability.",
  "habits": [
    {
      "title": "Short habit name",
      "description": "Clear description of what to do",
      "frequency": "daily|weekly|as-needed",
      "duration_minutes": 10,
      "domain": "the CBT domain this comes from",
      "source_title": "title of the source document"
    }
  ]
}

Provide 2-4 concrete habits. Keep the advice under 500 words.\
"""

CBT_ADVICE_USER_TEMPLATE = """\
=== USER'S CONCERN ===
{user_text}

=== DETECTED EMOTIONS ===
{emotions}

=== RETRIEVED CBT EDUCATIONAL MATERIALS ===
{context}

Based on the above materials, provide empathetic, evidence-based advice and \
actionable micro-habits for this person.\
"""


# ── Graph Retrieval Queries ───────────────────────────────────────────────────

RETRIEVE_CBT_CONTENT_QUERY = """\
CALL db.index.vector.queryNodes($index_name, $candidate_count, $embedding)
YIELD node, score
WHERE score >= $min_similarity

// Try matching as CBTSection first (most specific)
OPTIONAL MATCH (document:CBTDocument)-[:CONTAINS]->(node)
WHERE node:CBTSection
OPTIONAL MATCH (domain:CBTDomain)-[:CONTAINS]->(document)

// Also try as CBTDocument
OPTIONAL MATCH (domain2:CBTDomain)-[:CONTAINS]->(node)
WHERE node:CBTDocument

// Resolve concepts linked to sections
OPTIONAL MATCH (node)-[:MENTIONS|TEACHES|PRACTICES]->(concept:CBTConcept)

WITH node, score,
     coalesce(document, node) AS resolved_doc,
     coalesce(domain, domain2) AS resolved_domain,
     collect(DISTINCT concept.name) AS concepts

RETURN
    resolved_doc.node_id AS node_id,
    resolved_doc.title AS title,
    coalesce(resolved_domain.name, 'General') AS domain,
    resolved_doc.document_type AS document_type,
    left(coalesce(node.content, resolved_doc.content, ''), 1500) AS content,
    concepts,
    score
ORDER BY score DESC
LIMIT $top_k
"""

RETRIEVE_TECHNIQUES_FOR_THEME_QUERY = """\
MATCH (concept:CBTConcept)
WHERE concept.concept_type = 'technique'
  AND (toLower(concept.name) CONTAINS toLower($theme)
       OR ANY(alias IN concept.aliases WHERE toLower(alias) CONTAINS toLower($theme)))
OPTIONAL MATCH (concept)<-[:TEACHES]-(doc:CBTDocument)
OPTIONAL MATCH (domain:CBTDomain)-[:CONTAINS]->(doc)
RETURN
    concept.node_id AS node_id,
    concept.name AS name,
    concept.description AS description,
    concept.steps AS steps,
    coalesce(domain.name, 'General') AS domain,
    coalesce(doc.title, '') AS source_title
LIMIT 5
"""

RETRIEVE_HABITS_FOR_DOMAIN_QUERY = """\
MATCH (concept:CBTConcept)
WHERE concept.concept_type = 'micro_habit'
  AND ($domain_id = '' OR ANY(doc_id IN concept.source_document_ids
       WHERE doc_id STARTS WITH $domain_id))
RETURN
    concept.node_id AS node_id,
    concept.name AS name,
    concept.description AS description,
    concept.frequency AS frequency,
    concept.duration_minutes AS duration_minutes
LIMIT 5
"""

RETRIEVE_DISTORTIONS_QUERY = """\
MATCH (concept:CBTConcept)
WHERE concept.concept_type = 'distortion'
RETURN concept.name AS name, concept.description AS description
LIMIT 15
"""


# ── Pipeline Class ────────────────────────────────────────────────────────────

class CBTGraphRAGPipeline:
    """Hybrid retrieval + LLM synthesis for CBT advice and habits.

    Pipeline stages:
      1. Safety check — urgent support routing
      2. Vector retrieval — find relevant CBT sections/documents
      3. Graph traversal — fetch linked techniques, habits, distortions
      4. LLM synthesis — Gemma4 via Bedrock generates grounded advice
    """

    def __init__(self, settings: Settings) -> None:
        self._settings = settings
        self._embedding_model = settings.embedding_model
        self._vector_index = settings.cbt_vector_index
        self._document_index = settings.cbt_document_vector_index
        self._min_similarity = settings.cbt_min_similarity
        self._top_k = settings.cbt_resource_top_k
        self._gemma_model_id = settings.cbt_gemma_model_id

    async def generate_advice(
        self,
        *,
        user_text: str,
        emotions: list[dict[str, float]] | None = None,
        domain_filter: list[str] | None = None,
        top_k: int | None = None,
    ) -> CBTGraphRAGResult:
        """Full pipeline: safety → retrieve → synthesize → return."""

        # Stage 1: Safety check
        safety = evaluate_urgent_support_signal(user_text)
        if safety.should_escalate:
            return CBTGraphRAGResult(
                status="urgent_support",
                disclaimer=EDUCATIONAL_DISCLAIMER,
                urgent_support=True,
                urgent_support_message=safety.message,
            )

        # Stage 2: Vector retrieval from Neo4j
        try:
            from speroflow_ai.db.neo4j_client import get_driver

            driver = await get_driver(self._settings)
            embedding = await self._generate_embedding(user_text)

            effective_top_k = top_k or self._top_k
            context_results = await self._retrieve_context(
                driver, embedding, effective_top_k
            )

            if not context_results:
                return CBTGraphRAGResult(
                    status="no_results",
                    advice="I couldn't find specific CBT resources matching your concern. "
                           "Please try rephrasing, or explore our general wellbeing resources.",
                    disclaimer=EDUCATIONAL_DISCLAIMER,
                )

            # Stage 3: Build context from retrieved content
            context_text = self._format_context(context_results)
            sources = [
                CBTAdviceSource(
                    node_id=r.get("node_id", ""),
                    title=r.get("title", ""),
                    domain=r.get("domain", ""),
                    document_type=r.get("document_type", ""),
                    score=float(r.get("score", 0)),
                    content_excerpt=r.get("content", "")[:300],
                )
                for r in context_results
            ]

            # Stage 4: LLM synthesis via Gemma4 on Bedrock
            emotions_text = self._format_emotions(emotions)
            user_prompt = CBT_ADVICE_USER_TEMPLATE.format(
                user_text=user_text,
                emotions=emotions_text,
                context=context_text,
            )

            raw_response = await invoke_bedrock(
                model_id=self._gemma_model_id,
                system_prompt=CBT_ADVICE_SYSTEM_PROMPT,
                user_text=user_prompt,
                settings=self._settings,
                max_tokens=2048,
                temperature=0.4,
            )

            result = self._parse_llm_response(raw_response, sources)
            return result

        except Exception as exc:
            logger.error("CBT Graph RAG pipeline failed: %s", exc, exc_info=True)
            return CBTGraphRAGResult(
                status="error",
                advice="I encountered an issue retrieving CBT resources. "
                       "Please try again shortly.",
                disclaimer=EDUCATIONAL_DISCLAIMER,
                error=str(exc),
            )

    async def _generate_embedding(self, text: str) -> list[float]:
        """Generate query embedding using the configured model."""
        from speroflow_ai.services.embedding import generate_embedding

        return await generate_embedding(text, self._embedding_model)

    async def _retrieve_context(
        self,
        driver,
        embedding: list[float],
        top_k: int,
    ) -> list[dict]:
        """Run vector search against CBT content in Neo4j."""
        candidate_count = min(top_k * 4, 40)

        # Try section index first, fall back to document index
        for index_name in (self._vector_index, self._document_index):
            try:
                async with driver.session(
                    database=self._settings.neo4j_database
                ) as session:
                    result = await session.run(
                        RETRIEVE_CBT_CONTENT_QUERY,
                        index_name=index_name,
                        candidate_count=candidate_count,
                        embedding=embedding,
                        min_similarity=self._min_similarity,
                        top_k=top_k,
                    )
                    rows = await result.data()
                    if rows:
                        logger.info(
                            "CBT retrieval via '%s': %d results",
                            index_name, len(rows),
                        )
                        return rows
            except Exception as exc:
                logger.warning(
                    "CBT vector search with index '%s' failed: %s",
                    index_name, exc,
                )
                continue

        return []

    @staticmethod
    def _format_context(results: list[dict]) -> str:
        """Format retrieved CBT content for the LLM prompt."""
        parts = []
        for i, r in enumerate(results, 1):
            domain = r.get("domain", "General")
            title = r.get("title", "Unknown")
            content = r.get("content", "")
            concepts = r.get("concepts", [])
            doc_type = r.get("document_type", "")
            score = r.get("score", 0)

            part = (
                f"--- Source {i}: {title} ---\n"
                f"Domain: {domain} | Type: {doc_type} | Relevance: {score:.3f}\n"
            )
            if concepts:
                part += f"Related CBT Concepts: {', '.join(concepts)}\n"
            part += f"\n{content}\n"
            parts.append(part)

        return "\n".join(parts)

    @staticmethod
    def _format_emotions(emotions: list[dict[str, float]] | None) -> str:
        """Format detected emotions for the prompt."""
        if not emotions:
            return "No specific emotions detected."
        parts = []
        for e in emotions:
            label = e.get("label", "unknown")
            score = e.get("score", 0)
            parts.append(f"- {label}: {score:.2%}")
        return "\n".join(parts)

    def _parse_llm_response(
        self, raw: str, sources: list[CBTAdviceSource]
    ) -> CBTGraphRAGResult:
        """Parse the Gemma4 JSON response into a structured result."""
        try:
            # Extract JSON from potential markdown code blocks
            clean = raw.strip()
            code_block = re.search(
                r"```(?:json)?\s*(.*?)```", clean, re.DOTALL | re.IGNORECASE
            )
            if code_block:
                clean = code_block.group(1).strip()
            # Also try extracting bare JSON object
            object_match = re.search(r"\{.*\}", clean, re.DOTALL)
            if object_match:
                clean = object_match.group(0)

            parsed = json.loads(clean)

            themes = parsed.get("themes", [])
            advice = parsed.get("advice", "")
            habits_raw = parsed.get("habits", [])

            habits = []
            for h in habits_raw:
                habits.append(CBTHabit(
                    title=h.get("title", ""),
                    description=h.get("description", ""),
                    frequency=h.get("frequency", "daily"),
                    duration_minutes=int(h.get("duration_minutes", 10)),
                    domain=h.get("domain", ""),
                    source_title=h.get("source_title", ""),
                ))

            return CBTGraphRAGResult(
                status="ok",
                advice=advice,
                habits=habits,
                detected_themes=themes,
                sources=sources,
                model_used=self._gemma_model_id,
                disclaimer=EDUCATIONAL_DISCLAIMER,
            )

        except (json.JSONDecodeError, KeyError, TypeError) as exc:
            logger.warning("Failed to parse LLM JSON response: %s", exc)
            # Fall back to using the raw text as advice
            return CBTGraphRAGResult(
                status="ok",
                advice=raw.strip(),
                habits=[],
                detected_themes=[],
                sources=sources,
                model_used=self._gemma_model_id,
                disclaimer=EDUCATIONAL_DISCLAIMER,
            )
