"""
Roadmap router — graph-grounded learning path generation powered by Gemma 4 31B and GraphRAG.

POST /api/roadmap/prerequisites
  Retrieves real GraphRAG knowledge, concepts, content units, and source citations
  from the Neo4j knowledge graph, then uses Gemma 4 31B to synthesize a structured
  interactive learning timeline with steps, citations, resources, and subtasks.
"""

from __future__ import annotations

import json as json_module
import logging
import re
import urllib.parse
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
from speroflow_ai.services.bedrock_client import invoke_bedrock

logger = logging.getLogger("speroflow.routers.roadmap")

router = APIRouter(prefix="/api/roadmap", tags=["Roadmap Graph RAG"])

# ── System Prompt for Gemma 4 31B ─────────────────────────────────────────────

SYSTEM_PROMPT = """You are SperoFlow's AI Software Architect and Curriculum Designer powering GraphRAG Roadmaps.

Your task is to synthesize a structured, highly specific 5 to 7 step learning roadmap for a requested GOAL using the RETRIEVED GRAPH KNOWLEDGE & CITATIONS provided.

=== STRICT RULES ===
1. Every step topic MUST name specific technologies, core APIs, languages, frameworks, or design patterns for the target GOAL.
2. Do NOT output generic filler or static steps.
3. For each step, provide:
   - "topic": Clear, highly specific step title (e.g. "1. Modern Rust Syntax, Ownership & Memory Safety", "2. Traits, Generics & Lifetime Annotations")
   - "description": Comprehensive objective detailing exact concepts to master, what to build, and key design patterns.
   - "estimated_hours": Realistic study/practice time in hours (e.g., 6.0, 10.0, 14.0).
   - "resources": 3-4 real, actionable resource links or source citations from the retrieved graph knowledge (e.g., "https://doc.rust-lang.org/book/ - Rust Official Manual", "The Rust Programming Language: Ch 4 Ownership").
   - "subtasks": 3-4 specific hands-on coding tasks for this step.
4. Output MUST be ONLY valid JSON matching this schema:
{
  "goal": "Target Goal Title",
  "steps": [
    {
      "topic": "1. Specific Step Title",
      "description": "Detailed objective and overview.",
      "estimated_hours": 8.0,
      "resources": [
        "https://doc.rust-lang.org/book/ - Rust Official Manual",
        "Rust Ownership & Lifetime Exercises"
      ],
      "subtasks": [
        "Hands-on coding work item 1",
        "Hands-on coding work item 2"
      ]
    }
  ],
  "total_estimated_hours": 45.0,
  "motivational_summary": "Inspiring summary for the goal..."
}
"""

def _clean_topic_name(raw: str) -> str:
    """Strip action phrases like 'i want to learn', 'how to', 'master'."""
    text = raw.strip()
    pattern = r"^(?:i\s+want\s+to\s+learn|how\s+to\s+learn|how\s+to|learn\s+about|learn|master|study|guide\s+to)\s+"
    cleaned = re.sub(pattern, "", text, flags=re.IGNORECASE).strip()
    return cleaned if len(cleaned) >= 2 else text


# ── GraphRAG Context Retrieval ────────────────────────────────────────────────

async def _retrieve_graphrag_context(driver: AsyncDriver, goal_name: str) -> tuple[list[str], list[str]]:
    """Retrieve real GraphRAG knowledge, concepts, content units, and source citations from Neo4j."""
    topic = _clean_topic_name(goal_name)
    
    cypher_concepts = """
        MATCH (n)
        WHERE (n:CBTConcept OR n:CBTSection OR n:CBTDocument OR n:ContentUnit OR n:Entity OR n:Topic OR n:Subtopic OR n:Roadmap OR n:Node)
          AND (
            toLower(coalesce(n.name, n.title, n.label_text, n.text, '')) CONTAINS toLower($topic)
            OR toLower($topic) CONTAINS toLower(coalesce(n.name, n.title, n.label_text, ''))
          )
        OPTIONAL MATCH (n)-[:MENTIONS|TEACHES|PRACTICES|ASSERTS|CONTAINS|LEADS_TO|DEPENDS_ON*1..2]-(related)
        RETURN coalesce(n.name, n.title, n.label_text, '') AS concept,
               coalesce(n.citation, n.source, n.title, '') AS citation,
               coalesce(related.name, related.title, related.label_text, '') AS related_title
        LIMIT 30
    """
    
    cypher_units = """
        MATCH (unit:ContentUnit)
        WHERE unit.active = true AND toLower(unit.text) CONTAINS toLower($topic)
        RETURN unit.text AS text, unit.citation AS citation
        LIMIT 10
    """
    
    concepts: set[str] = set()
    citations: set[str] = set()
    
    async with driver.session() as session:
        try:
            res1 = await session.run(cypher_concepts, topic=topic)
            records1 = await res1.data()
            for r in records1:
                c = r.get("concept", "").strip()
                cit = r.get("citation", "").strip()
                rel = r.get("related_title", "").strip()
                if c and len(c) > 2:
                    concepts.add(c[:300])
                if rel and len(rel) > 2:
                    concepts.add(rel[:300])
                if cit and len(cit) > 2:
                    citations.add(cit[:300])
        except Exception as err:
            logger.warning("GraphRAG concepts query notice: %s", err)
            
        try:
            res2 = await session.run(cypher_units, topic=topic)
            records2 = await res2.data()
            for r in records2:
                txt = r.get("text", "").strip()
                cit = r.get("citation", "").strip()
                if txt:
                    concepts.add(txt[:400])
                if cit:
                    citations.add(cit[:300])
        except Exception as err:
            logger.warning("GraphRAG content units query notice: %s", err)

    logger.info("GraphRAG retrieval for '%s': %d concepts, %d citations.", goal_name, len(concepts), len(citations))
    return list(concepts), list(citations)


def _build_fallback_timeline(goal_name: str, concepts: list[str], citations: list[str]) -> dict[str, Any]:
    cleaned = _clean_topic_name(goal_name)
    title_case = cleaned.title()

    res_1 = [f"{cit} - {title_case} Source" for cit in citations[:2]] if citations else [
        f"https://www.google.com/search?q=official+{urllib.parse.quote_plus(cleaned)}+documentation - Official {title_case} Reference",
        f"https://www.google.com/search?q={urllib.parse.quote_plus(cleaned)}+getting+started - Hands-on {title_case} Guide"
    ]

    steps = [
        {
            "topic": f"1. {title_case} Core Syntax & Environment Toolchain",
            "description": f"Master foundational principles, runtime/compiler toolchain setup, package configuration, and core syntax of {cleaned}.",
            "estimated_hours": 6.0,
            "resources": res_1,
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
                f"https://www.google.com/search?q={urllib.parse.quote_plus(cleaned)}+architecture+design+patterns - {title_case} Architecture Specs",
                f"https://www.google.com/search?q={urllib.parse.quote_plus(cleaned)}+best+practices - Core Building Blocks & Best Practices",
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
                f"https://www.google.com/search?q={urllib.parse.quote_plus(cleaned)}+ecosystem+libraries - Top Ecosystem Tools for {title_case}",
                f"https://www.google.com/search?q={urllib.parse.quote_plus(cleaned)}+api+integration - API Integration Guide",
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
                f"https://www.google.com/search?q={urllib.parse.quote_plus(cleaned)}+performance+optimization - Performance Profiling Guide",
                f"https://www.google.com/search?q={urllib.parse.quote_plus(cleaned)}+security+hardening - Security Audit Checklist",
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
                f"https://www.google.com/search?q={urllib.parse.quote_plus(cleaned)}+production+deployment - Production Deployment Guide",
                f"https://www.google.com/search?q={urllib.parse.quote_plus(cleaned)}+capstone+project+spec - Capstone Project Spec",
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


# ── Router ────────────────────────────────────────────────────────────────────

@router.post(
    "/prerequisites",
    response_model=LearningTimeline,
    summary="Generate an interactive GraphRAG prerequisite learning path with resources for a goal topic",
)
async def get_prerequisites(
    request: PrerequisiteRequest,
    current_user: dict = Depends(require_verified_user),
    settings: Settings = Depends(get_app_settings),
    driver: AsyncDriver = Depends(get_neo4j),
) -> LearningTimeline:
    """
    Pipeline: Goal → Neo4j GraphRAG Retrieval → Gemma 4 31B Synthesis → LearningTimeline with Step Resources
    """
    logger.info("GraphRAG Prerequisites request for goal: '%s'", request.goal_name)
    
    # 1. Retrieve GraphRAG Knowledge & Citations from Neo4j
    concepts, citations = [], []
    try:
        concepts, citations = await _retrieve_graphrag_context(driver, request.goal_name)
    except Exception as exc:
        logger.warning("GraphRAG retrieval notice: %s", exc)

    # 2. Build Context Prompt
    context_lines = [f"GOAL: {request.goal_name}"]
    if concepts:
        context_lines.append("RETRIEVED GRAPH CONCEPTS & KNOWLEDGE:\n" + "\n".join(f"- {c}" for c in concepts[:20]))
    if citations:
        context_lines.append("RETRIEVED SOURCE CITATIONS & DOCUMENTS:\n" + "\n".join(f"- {c}" for c in citations[:10]))
    
    full_context = "\n\n".join(context_lines)

    parsed = None
    
    # 3. Invoke Gemma 4 31B via invoke_bedrock or create_chat_model
    try:
        raw_text = await invoke_bedrock(
            model_id=settings.llm_model,
            system_prompt=SYSTEM_PROMPT,
            user_text=full_context,
            settings=settings,
            max_tokens=2048,
            temperature=0.2,
        )
        clean = raw_text.strip()
        json_match = re.search(r'```(?:json)?\s*\n(.*?)```', clean, re.DOTALL)
        if json_match:
            clean = json_match.group(1)
        parsed = json_module.loads(clean.strip())
        logger.info("Gemma 4 31B GraphRAG roadmap synthesis successful for '%s'.", request.goal_name)
    except Exception as exc1:
        logger.warning("Primary invoke_bedrock (%s) failed: %s. Trying chat model fallback...", settings.llm_model, exc1)
        try:
            from speroflow_ai.services.chat_model import create_chat_model
            llm = create_chat_model(
                provider=settings.llm_provider,
                model=settings.llm_model,
                api_base=settings.llm_api_base,
                api_key=settings.llm_api_key,
                temperature=0.2,
                bedrock_region=settings.bedrock_region,
                max_tokens=1_500,
            )
            messages = [
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": full_context},
            ]
            response = await llm.ainvoke(messages)
            raw = response.content if hasattr(response, "content") else str(response)
            clean = raw.strip()
            json_match = re.search(r'```(?:json)?\s*\n(.*?)```', clean, re.DOTALL)
            if json_match:
                clean = json_match.group(1)
            parsed = json_module.loads(clean.strip())
        except Exception as exc2:
            logger.warning("Secondary LLM synthesis failed: %s", exc2)

    if not parsed or not isinstance(parsed.get("steps"), list) or len(parsed.get("steps", [])) == 0:
        parsed = _build_fallback_timeline(request.goal_name, concepts, citations)

    steps_res = []
    for idx, s in enumerate(parsed.get("steps", [])):
        res_list = s.get("resources")
        if not isinstance(res_list, list) or not res_list:
            t_name = s.get("topic", f"Step {idx + 1}")
            res_list = citations[:2] if citations else [
                f"https://www.google.com/search?q={urllib.parse.quote_plus(t_name)} - Official Documentation",
                f"Practical Exercise: {t_name} implementation",
            ]
        steps_res.append(
            LearningStep(
                topic=str(s.get("topic", f"Step {idx + 1}")),
                description=str(s.get("description", "")),
                estimated_hours=float(s.get("estimated_hours", 4.0)),
                resources=[str(r) for r in res_list],
            )
        )

    return LearningTimeline(
        goal=str(parsed.get("goal", request.goal_name)),
        steps=steps_res,
        total_estimated_hours=sum(step.estimated_hours for step in steps_res),
        motivational_summary=str(parsed.get("motivational_summary", f"A GraphRAG-grounded roadmap for {request.goal_name}.")),
    )
