"""
Roadmap router — graph-grounded learning path generation.

POST /api/roadmap/prerequisites
  Traverses the Neo4j prerequisite subgraph for a goal topic,
  topologically sorts it, then uses the LLM to synthesize a
  structured learning timeline with steps and curated resources.
"""

from __future__ import annotations

import logging
import re
from collections import defaultdict, deque
from typing import Any

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

SYSTEM_PROMPT = """You are an expert curriculum designer and AI Software Architect for SperoFlow.

Your task is to create a dynamic, highly specific, step-by-step learning roadmap tailored precisely to the user's GOAL and any GRAPH GROUNDED TOPICS provided.

CRITICAL INSTRUCTIONS:
1. NEVER output generic or hardcoded template steps.
2. Generate 5 to 7 progressive, highly technical steps specific to the exact requested GOAL (e.g., for "frontend": HTML5/CSS3/JavaScript ES6+, React.js & Component State, Next.js App Router & SSR, State Management & Tailwind CSS, Web Performance & Deployment).
3. For each step, provide:
   - "topic": Clear, highly specific step title naming the exact tools, languages, or architectures.
   - "description": Detailed objective outlining key concepts, APIs, and design patterns to master.
   - "estimated_hours": Realistic study and practice time in hours (e.g., 6.0, 10.0, 14.0).
   - "resources": A list of 3-4 real, actionable HTTPS documentation URLs or guide links (e.g., "https://developer.mozilla.org/ - MDN Web Docs for HTML/CSS", "https://react.dev/ - React Official Documentation & Hooks Guide").
   - "subtasks": A list of 3-4 specific hands-on practice tasks for this step.
4. Write an inspiring "motivational_summary" summarizing the journey.

Respond ONLY as a valid JSON object matching this schema:
{
  "goal": "Target Goal Title",
  "steps": [
    {
      "topic": "1. Specific Step Title",
      "description": "Comprehensive objective and key concepts.",
      "estimated_hours": 8.0,
      "resources": [
        "https://official-docs-url.org/ - Official Documentation Name",
        "https://guide-url.org/ - Core Interactive Tutorial"
      ],
      "subtasks": [
        "Specific hands-on work item 1",
        "Specific hands-on work item 2"
      ]
    }
  ],
  "total_estimated_hours": 45.0,
  "motivational_summary": "Inspiring summary for target goal..."
}
"""

def _clean_topic_name(raw: str) -> str:
    """Strip action phrases like 'i want to learn', 'how to', 'master'."""
    text = raw.strip()
    pattern = r"^(?:i\s+want\s+to\s+learn|how\s+to\s+learn|how\s+to|learn\s+about|learn|master|study|guide\s+to)\s+"
    cleaned = re.sub(pattern, "", text, flags=re.IGNORECASE).strip()
    return cleaned if len(cleaned) >= 2 else text

# ── Cypher GraphRAG Traversal ──────────────────────────────────────────────────

async def _fetch_prerequisite_subgraph(
    driver: AsyncDriver,
    goal_name: str,
) -> tuple[list[str], list[tuple[str, str]]]:
    topic = _clean_topic_name(goal_name)
    cypher = """
        MATCH (n)
        WHERE (n:Entity OR n:CBTConcept OR n:CBTDocument OR n:CBTSection OR n:Topic OR n:Subtopic OR n:Roadmap OR n:Node)
          AND (
            toLower(coalesce(n.name, n.title, n.label_text, '')) CONTAINS toLower($topic)
            OR toLower($topic) CONTAINS toLower(coalesce(n.name, n.title, n.label_text, ''))
          )
        OPTIONAL MATCH (n)-[r:LEADS_TO|DEPENDS_ON|CONTAINS|MENTIONS|TEACHES|HAS_PREREQUISITE*1..2]-(related)
        WITH coalesce(n.name, n.title, n.label_text, '') AS main_node,
             coalesce(related.name, related.title, related.label_text, '') AS related_node
        WHERE size(trim(main_node)) > 2
        RETURN main_node AS source, related_node AS target
        LIMIT 40
    """
    nodes: set[str] = set()
    edges: list[tuple[str, str]] = []

    async with driver.session() as session:
        result = await session.run(cypher, topic=topic)
        records = await result.data()
        for record in records:
            src = record.get("source", "").strip()
            tgt = record.get("target", "").strip()
            if len(src) > 2:
                nodes.add(src)
            if len(tgt) > 2:
                nodes.add(tgt)
                edges.append((src, tgt))

    filtered_nodes = [n for n in nodes if len(n.strip()) > 2]
    logger.info("GraphRAG Subgraph for '%s' (cleaned: '%s'): %d nodes, %d edges.", goal_name, topic, len(filtered_nodes), len(edges))
    return filtered_nodes, edges


def _topological_sort(nodes: list[str], edges: list[tuple[str, str]]) -> list[str]:
    """Kahn's algorithm — guarantees prerequisites appear before dependents."""
    in_degree: dict[str, int] = {n: 0 for n in nodes}
    adjacency: dict[str, list[str]] = defaultdict(list)

    for src, tgt in edges:
        if src not in in_degree or tgt not in in_degree:
            continue
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
        result.extend(n for n in nodes if n not in set(result))

    return result


def _build_fallback_timeline(goal_name: str, topics: list[str]) -> dict[str, Any]:
    cleaned = _clean_topic_name(goal_name)
    title_case = cleaned.title()

    # Dynamic fallback step generation tailored specifically to $goal_name
    steps = [
        {
            "topic": f"1. {title_case} Core Syntax & Environment Toolchain",
            "description": f"Master foundational principles, runtime/compiler toolchain setup, package configuration, and core syntax of {cleaned}.",
            "estimated_hours": 6.0,
            "resources": [
                f"https://www.google.com/search?q=official+{encode_param(cleaned)}+documentation - Official {title_case} Documentation & API Reference",
                f"https://www.google.com/search?q={encode_param(cleaned)}+getting+started+guide - Hands-on {title_case} Getting Started Guide",
            ],
            "subtasks": [
                f"Install CLI tools and environment for {cleaned}",
                f"Write and verify initial baseline project for {cleaned}",
                "Configure linter, formatter, and package manager",
            ],
        },
        {
            "topic": f"2. {title_case} Architecture & Core Building Blocks",
            "description": f"Understand structural paradigms, component/data models, memory & state management, and design patterns for {cleaned}.",
            "estimated_hours": 8.0,
            "resources": [
                f"https://www.google.com/search?q={encode_param(cleaned)}+architecture+design+patterns - {title_case} Architecture Specs & Design Patterns",
                f"https://www.google.com/search?q={encode_param(cleaned)}+best+practices+guide - Core Building Blocks & Best Practices",
            ],
            "subtasks": [
                f"Implement core data structures and modular components for {cleaned}",
                f"Apply key design patterns and error handling in {cleaned}",
                "Write unit tests covering baseline modules",
            ],
        },
        {
            "topic": f"3. Advanced Framework Ecosystem & Integration",
            "description": f"Explore ecosystem libraries, asynchronous operations, state workflows, API integrations, and database schemas.",
            "estimated_hours": 10.0,
            "resources": [
                f"https://www.google.com/search?q={encode_param(cleaned)}+ecosystem+libraries - Top Ecosystem Libraries & Tools for {title_case}",
                f"https://www.google.com/search?q={encode_param(cleaned)}+api+integration - API & Service Integration Guide",
            ],
            "subtasks": [
                f"Integrate standard ecosystem packages for {cleaned}",
                "Build async request handlers and state pipelines",
                "Perform integration testing across services",
            ],
        },
        {
            "topic": f"4. System Design, Performance & Security",
            "description": f"Optimize execution performance, memory usage, security guardrails, caching, and multi-component system design.",
            "estimated_hours": 10.0,
            "resources": [
                f"https://www.google.com/search?q={encode_param(cleaned)}+performance+optimization - Performance Benchmarking & Profiling Guide",
                f"https://www.google.com/search?q={encode_param(cleaned)}+security+hardening - Security Hardening & Audit Checklist",
            ],
            "subtasks": [
                f"Profile memory and CPU utilization for {cleaned}",
                "Implement security validation and input sanitization",
                "Benchmark high-throughput execution paths",
            ],
        },
        {
            "topic": f"5. Capstone Project Release & Production Deployment",
            "description": f"Synthesize all concepts by building, containerizing, testing, and deploying a complete production-grade {cleaned} application.",
            "estimated_hours": 15.0,
            "resources": [
                f"https://www.google.com/search?q={encode_param(cleaned)}+production+deployment - Production Deployment Documentation",
                f"https://www.google.com/search?q={encode_param(cleaned)}+capstone+project+spec - Capstone Portfolio Project Specification",
            ],
            "subtasks": [
                f"Build full-featured capstone application for {cleaned}",
                "Configure CI/CD automated deployment pipeline",
                "Publish project repository and documentation",
            ],
        },
    ]

    total_h = sum(float(s["estimated_hours"]) for s in steps)
    return {
        "goal": goal_name,
        "steps": steps,
        "total_estimated_hours": total_h,
        "motivational_summary": f"Embark on a structured 5-stage learning path to master {title_case} step by step!",
    }


def encode_param(text: str) -> str:
    import urllib.parse
    return urllib.parse.quote_plus(text)

    total_h = sum(float(s["estimated_hours"]) for s in steps)
    return {
        "goal": goal_name,
        "steps": steps,
        "total_estimated_hours": total_h,
        "motivational_summary": f"Embark on a structured 5-stage learning path to master {title_case} step by step!",
    }


# ── Router ────────────────────────────────────────────────────────────────────

@router.post(
    "/prerequisites",
    response_model=LearningTimeline,
    summary="Generate an interactive prerequisite learning path with resources for a goal topic",
)
async def get_prerequisites(
    request: PrerequisiteRequest,
    current_user: dict = Depends(require_verified_user),
    settings: Settings = Depends(get_app_settings),
    driver: AsyncDriver = Depends(get_neo4j),
) -> LearningTimeline:
    """
    Pipeline: Goal → Neo4j Traversal → Topological Sort → LLM Synthesis → LearningTimeline with Step Resources
    """
    logger.info("Prerequisites request for goal: '%s'", request.goal_name)
    nodes, edges = [], []
    try:
        nodes, edges = await _fetch_prerequisite_subgraph(driver, request.goal_name)
    except Exception as exc:
        logger.warning("Neo4j graph fetch encountered an issue: %s", exc)

    ordered = _topological_sort(nodes, edges) if nodes else []

    context = f"GOAL: {request.goal_name}\n"
    if ordered:
        context += "GRAPH GROUNDED TOPICS:\n" + "\n".join(f"- {t}" for t in ordered)

    parsed = None
    import json as json_module
    from speroflow_ai.services.chat_model import create_chat_model

    messages = [
        {"role": "system", "content": SYSTEM_PROMPT},
        {"role": "user", "content": context},
    ]

    # Attempt 1: Configured LLM provider & model
    try:
        llm = create_chat_model(
            provider=settings.llm_provider,
            model=settings.llm_model,
            api_base=settings.llm_api_base,
            api_key=settings.llm_api_key,
            temperature=0.2,
            bedrock_region=settings.bedrock_region,
            max_tokens=1_500,
        )
        response = await llm.ainvoke(messages)
        raw = response.content if hasattr(response, "content") else str(response)
        clean = raw.strip()
        json_match = re.search(r'```(?:json)?\s*\n(.*?)```', clean, re.DOTALL)
        if json_match:
            clean = json_match.group(1)
        parsed = json_module.loads(clean.strip())
    except Exception as exc1:
        logger.warning("Primary LLM synthesis (%s/%s) failed: %s. Trying secondary LLM fallback...", settings.llm_provider, settings.llm_model, exc1)
        
        # Attempt 2: Bedrock native model identifier fallback if provider is bedrock
        if settings.llm_provider == "bedrock":
            try:
                fallback_llm = create_chat_model(
                    provider="bedrock",
                    model="us.amazon.nova-pro-v1:0",
                    api_base="",
                    api_key="",
                    temperature=0.2,
                    bedrock_region=settings.bedrock_region,
                    max_tokens=1_500,
                )
                response = await fallback_llm.ainvoke(messages)
                raw = response.content if hasattr(response, "content") else str(response)
                clean = raw.strip()
                json_match = re.search(r'```(?:json)?\s*\n(.*?)```', clean, re.DOTALL)
                if json_match:
                    clean = json_match.group(1)
                parsed = json_module.loads(clean.strip())
            except Exception as exc2:
                logger.warning("Secondary Bedrock LLM synthesis failed: %s", exc2)

    if not parsed or not isinstance(parsed.get("steps"), list) or len(parsed.get("steps", [])) == 0:
        parsed = _build_fallback_timeline(request.goal_name, ordered)

    steps_res = []
    for idx, s in enumerate(parsed.get("steps", [])):
        res_list = s.get("resources")
        if not isinstance(res_list, list) or not res_list:
            t_name = s.get("topic", f"Step {idx + 1}")
            res_list = [
                f"Core {t_name} Documentation & Reference",
                f"Practical Exercise: {t_name} implementation",
            ]
        steps_res.append(
            LearningStep(
                topic=str(s.get("topic", f"Step {idx + 1}")),
                description=str(s.get("description", "")),
                estimated_hours=float(s.get("estimated_hours", 3.0)),
                resources=[str(r) for r in res_list],
            )
        )

    return LearningTimeline(
        goal=str(parsed.get("goal", request.goal_name)),
        steps=steps_res,
        total_estimated_hours=sum(step.estimated_hours for step in steps_res),
        motivational_summary=str(parsed.get("motivational_summary", f"A graph-grounded roadmap for {request.goal_name}.")),
    )

