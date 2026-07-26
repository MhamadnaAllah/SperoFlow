"""
Roadmap router — graph-grounded learning path generation.

POST /api/roadmap/prerequisites
  Traverses the Neo4j prerequisite subgraph for a goal topic,
  topologically sorts it, then uses the LLM to synthesize a
  structured learning timeline.
"""

from __future__ import annotations

import logging
import re
from collections import defaultdict, deque

from fastapi import APIRouter, Depends, HTTPException
from neo4j import AsyncDriver
from neo4j.exceptions import ServiceUnavailable

from speroflow_ai.config import Settings
from speroflow_ai.dependencies import get_app_settings, get_neo4j
from speroflow_ai.models.requests import PrerequisiteRequest
from speroflow_ai.models.responses import ErrorResponse, LearningStep, LearningTimeline
from speroflow_ai.service_auth import require_verified_user

logger = logging.getLogger("speroflow.routers.roadmap")

router = APIRouter(prefix="/api/roadmap", tags=["Roadmap Graph RAG"])

# ── System Prompt ──────────────────────────────────────────────────────────────

SYSTEM_PROMPT = """You are a precise curriculum designer for SperoFlow.

You will be given a GOAL and a list of PREREQUISITE TOPICS extracted from a
verified knowledge graph in correct topological (dependency) order.

Your task:
1. For each topic, write a brief description explaining WHY it is needed.
2. Estimate study hours for each topic (be realistic).
3. Write a short motivational summary of the overall learning journey.

STRICT RULES:
- You MUST use ONLY the topics provided in the graph context.
- You MUST NOT add, remove, or reorder any topics.
- Keep descriptions concise (under 500 characters each).
- Respond ONLY as a valid JSON object matching this schema:
  {
    "goal": "...",
    "steps": [{"topic": "...", "description": "...", "estimated_hours": 0.0}],
    "total_estimated_hours": 0.0,
    "motivational_summary": "..."
  }
"""


# ── Cypher Traversal ──────────────────────────────────────────────────────────

async def _fetch_prerequisite_subgraph(
    driver: AsyncDriver,
    goal_name: str,
) -> tuple[list[str], list[tuple[str, str]]]:
    cypher = """
        MATCH path = (prereq)-[:LEADS_TO|DEPENDS_ON*1..10]->(goal)
        WHERE (goal:Topic OR goal:Subtopic)
          AND goal.label_text = $goal_name
          AND (prereq:Topic OR prereq:Subtopic)
        UNWIND relationships(path) AS rel
        WITH DISTINCT
            startNode(rel).label_text AS source,
            endNode(rel).label_text AS target
        RETURN source, target
    """
    nodes: set[str] = set()
    edges: list[tuple[str, str]] = []

    async with driver.session() as session:
        result = await session.run(cypher, goal_name=goal_name)
        records = await result.data()
        for record in records:
            src, tgt = record["source"], record["target"]
            nodes.add(src)
            nodes.add(tgt)
            edges.append((src, tgt))

    nodes.add(goal_name)
    logger.info("Subgraph for '%s': %d nodes, %d edges.", goal_name, len(nodes), len(edges))
    return list(nodes), edges


def _topological_sort(nodes: list[str], edges: list[tuple[str, str]]) -> list[str]:
    """Kahn's algorithm — guarantees prerequisites appear before dependents."""
    in_degree: dict[str, int] = {n: 0 for n in nodes}
    adjacency: dict[str, list[str]] = defaultdict(list)

    for src, tgt in edges:
        if src not in in_degree or tgt not in in_degree:
            continue  # skip edges involving nodes outside the subgraph
        adjacency[src].append(tgt)
        in_degree[tgt] += 1

    queue = deque([n for n in nodes if in_degree[n] == 0])
    result: list[str] = []

    while queue:
        node = queue.popleft()
        result.append(node)
        for neighbor in adjacency[node]:
            in_degree[neighbor] -= 1
            if in_degree[neighbor] == 0:
                queue.append(neighbor)

    if len(result) < len(nodes):
        # Cycle detected — append remaining
        result.extend(n for n in nodes if n not in set(result))

    return result


# ── Router ────────────────────────────────────────────────────────────────────

@router.post(
    "/prerequisites",
    response_model=LearningTimeline,
    responses={
        404: {"model": ErrorResponse, "description": "Goal topic not found in graph."},
        500: {"model": ErrorResponse},
    },
    summary="Generate a prerequisite learning path for a goal topic",
)
async def get_prerequisites(
    request: PrerequisiteRequest,
    current_user: dict = Depends(require_verified_user),
    settings: Settings = Depends(get_app_settings),
    driver: AsyncDriver = Depends(get_neo4j),
) -> LearningTimeline:
    """
    Pipeline: Goal → Neo4j Traversal → Topological Sort → LLM Synthesis → LearningTimeline
    """
    logger.info("Prerequisites request for goal: '%s'", request.goal_name)

    try:
        # Step 1: Fetch prerequisite subgraph
        nodes, edges = await _fetch_prerequisite_subgraph(driver, request.goal_name)

        # Step 2: Verify goal exists
        if not edges and len(nodes) <= 1:
            async with driver.session() as session:
                result = await session.run(
                    "MATCH (t) WHERE (t:Topic OR t:Subtopic) AND t.label_text = $name RETURN t.label_text AS name",
                    name=request.goal_name,
                )
                record = await result.single()
                if not record:
                    raise HTTPException(
                        status_code=404,
                        detail=(
                            f"Topic '{request.goal_name}' not found in the knowledge graph. "
                            "Check the exact spelling or browse available roadmaps."
                        ),
                    )

        # Step 3: Topological sort
        ordered = _topological_sort(nodes, edges)
        logger.info("Order for '%s': %s", request.goal_name, " → ".join(ordered))

        # Step 4: LLM synthesis
        context = (
            f"GOAL: {request.goal_name}\n\n"
            f"PREREQUISITE TOPICS (in correct dependency order):\n"
            + "\n".join(f"{i + 1}. {t}" for i, t in enumerate(ordered))
        )

        import json as json_module

        from speroflow_ai.services.chat_model import create_chat_model

        llm = create_chat_model(
            provider=settings.llm_provider,
            model=settings.llm_model,
            api_base=settings.llm_api_base,
            api_key=settings.llm_api_key,
            temperature=0.0,
            bedrock_region=settings.bedrock_region,
            max_tokens=1_024,
        )

        messages = [
            {"role": "system", "content": SYSTEM_PROMPT},
            {"role": "user", "content": context},
        ]
        response = await llm.ainvoke(messages)
        raw = response.content if hasattr(response, "content") else str(response)

        # Parse JSON from LLM response
        try:
            # Strip markdown code blocks if present
            clean = raw.strip()
            json_match = re.search(r'```(?:json)?\s*\n(.*?)```', clean, re.DOTALL)
            if json_match:
                clean = json_match.group(1)
            parsed = json_module.loads(clean.strip())
        except Exception as exc:
            logger.warning("Failed to parse LLM JSON response: %s\nRaw: %s", exc, raw[:200])
            # Fallback: build a basic timeline from the sorted topics
            parsed = {
                "goal": request.goal_name,
                "steps": [
                    {"topic": t, "description": f"Study {t} as a prerequisite.", "estimated_hours": 2.0}
                    for t in ordered
                ],
                "total_estimated_hours": len(ordered) * 2.0,
                "motivational_summary": f"Complete this {len(ordered)}-step path to master {request.goal_name}!",
            }

        return LearningTimeline(
            goal=parsed.get("goal", request.goal_name),
            steps=[
                LearningStep(
                    topic=s["topic"],
                    description=s.get("description", ""),
                    estimated_hours=float(s.get("estimated_hours", 2.0)),
                )
                for s in parsed.get("steps", [])
            ],
            total_estimated_hours=float(parsed.get("total_estimated_hours", 0)),
            motivational_summary=parsed.get("motivational_summary", ""),
        )

    except HTTPException:
        raise
    except ServiceUnavailable:
        raise HTTPException(
            status_code=503,
            detail="Neo4j Aura is currently unavailable. Please try again later.",
        )
    except Exception as exc:
        logger.error("Roadmap generation failed: %s", exc, exc_info=True)
        raise HTTPException(status_code=500, detail=f"Failed to generate learning path: {exc}")
