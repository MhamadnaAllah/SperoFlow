"""Deterministic orchestration for CBT educational-resource retrieval.

This service deliberately does not use an LLM. It is not a clinical assessor,
diagnostic system, or treatment recommender.
"""

from __future__ import annotations

from dataclasses import dataclass

from speroflow_ai.services.cbt_retrieval import CBTResource, CBTResourceRetriever
from speroflow_ai.services.cbt_safety import evaluate_urgent_support_signal


EDUCATIONAL_DISCLAIMER = (
    "These are source-grounded educational materials, not a diagnosis, treatment plan, "
    "or substitute for a qualified mental-health professional."
)


@dataclass(frozen=True)
class CBTEducationalRAGResult:
    """Outcome from a bounded educational retrieval request."""

    status: str
    disclaimer: str
    resources: list[CBTResource]
    urgent_support: bool = False
    urgent_support_message: str = ""


class CBTEducationalRAG:
    """Retrieve cited CBT resources without making claims about the user."""

    def __init__(self, retriever: CBTResourceRetriever) -> None:
        self._retriever = retriever

    async def retrieve(
        self,
        *,
        query: str,
        top_k: int,
        domain_ids: list[str] | None = None,
        excerpt_characters: int = 0,
    ) -> CBTEducationalRAGResult:
        """Run explicit safety routing before the source-grounded retrieval path."""
        safety = evaluate_urgent_support_signal(query)
        if safety.should_escalate:
            return CBTEducationalRAGResult(
                status="urgent_support",
                disclaimer=EDUCATIONAL_DISCLAIMER,
                urgent_support=True,
                urgent_support_message=safety.message,
                resources=[],
            )

        resources = await self._retriever.search(
            query=query,
            top_k=top_k,
            domain_ids=domain_ids,
            excerpt_characters=excerpt_characters,
        )
        return CBTEducationalRAGResult(
            status="ok",
            disclaimer=EDUCATIONAL_DISCLAIMER,
            resources=resources,
        )
