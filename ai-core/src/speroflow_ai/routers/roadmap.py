"""
Roadmap router — graph-grounded learning path generation powered by Gemma 4 31B and GraphRAG.

POST /api/roadmap/prerequisites
  Retrieves real GraphRAG knowledge, concepts, content units, and source citations
  from the Neo4j knowledge graph (incorporating roadmap.sh curriculum DAGs and CBT docs),
  topologically sorts prerequisite sequences, then uses Gemma 4 31B to synthesize a structured
  interactive learning timeline with steps, citations, direct resources, and subtasks.
"""

from __future__ import annotations

import json as json_module
import logging
import os
import re
import urllib.parse
from collections import defaultdict, deque
from pathlib import Path
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

Your task is to synthesize a personalized, structured, and highly specific 5 to 7 step learning roadmap for the requested GOAL using the RETRIEVED GRAPH KNOWLEDGE & DIRECT CITATIONS from the roadmap.sh knowledge base.

=== STRICT CURRICULUM RULES ===
1. CUSTOMIZATION & PERSONALIZATION:
   - Personalize the steps to directly match the user's specific GOAL and domain context.
   - Every step title must name exact technologies, core APIs, languages, frameworks, or architectural design patterns.
2. PREREQUISITE TOPOLOGY:
   - Maintain natural prerequisite order: environment setup/fundamentals first, moving to core abstractions, design patterns, ecosystem libraries, and capstone production deployment.
3. FOR EACH MILESTONE / STEP:
   - "topic": Clear, highly specific step title (e.g., "1. Modern C++ Toolchain, Syntax & Memory Model", "2. Pointers, References & RAII Idioms").
   - "description": 2-3 concise sentences detailing the core concepts, runtime mechanics, and practical objectives of this milestone.
   - "estimated_hours": Realistic study/practice time in hours (e.g., 6.0, 8.0, 12.0, 15.0).
   - "resources": 2-4 REAL, DIRECT documentation and tutorial links extracted from the retrieved graph knowledge (formatted as "[Type] Title - URL", e.g. "[Article] Learn C++ - https://www.learncpp.com/", "[Video] C++ Tutorial - https://youtu.be/vLnPwxZdW4Y").
   - "subtasks": 3-5 specific, hands-on coding and implementation tasks to complete this milestone.
4. FORBIDDEN:
   - NEVER generate Google search links (e.g. google.com/search). Use the exact direct URLs provided in the graph knowledge or canonical official documentation URLs.
5. Output MUST be ONLY valid JSON matching this schema:
{
  "goal": "Target Goal Title",
  "steps": [
    {
      "topic": "1. Specific Step Title",
      "description": "Concise overview explaining key concepts and objectives for this milestone.",
      "estimated_hours": 8.0,
      "resources": [
        "[Article] Official Documentation - https://...",
        "[Video] Hands-on Video Tutorial - https://..."
      ],
      "subtasks": [
        "Hands-on coding work item 1",
        "Hands-on coding work item 2",
        "Hands-on coding work item 3"
      ]
    }
  ],
  "total_estimated_hours": 45.0,
  "motivational_summary": "Inspiring summary for the goal..."
}
"""

# ── Resource Extraction Utilities ─────────────────────────────────────────────

def _clean_topic_name(raw: str) -> str:
    """Strip action phrases like 'i want to learn', 'how to', 'master'."""
    text = raw.strip()
    pattern = r"^(?:i\s+want\s+to\s+learn|how\s+to\s+learn|how\s+to|learn\s+about|learn|master|study|guide\s+to)\s+"
    cleaned = re.sub(pattern, "", text, flags=re.IGNORECASE).strip()
    return cleaned if len(cleaned) >= 2 else text


def _extract_markdown_resources(content: str) -> list[str]:
    """Extract categorized markdown resources into formatted strings: '[Type] Label - URL'."""
    resources: list[str] = []
    if not content:
        return resources

    # Pattern 1: [@type@Title](url)
    p1 = r'\[@([a-zA-Z0-9_\-]+)@([^\]]+)\]\((https?://[^\)\s]+)\)'
    for rtype, title, url in re.findall(p1, content):
        clean_type = rtype.strip().capitalize()
        clean_title = title.strip()
        clean_url = url.strip()
        resources.append(f"[{clean_type}] {clean_title} - {clean_url}")

    # Pattern 2: [Title](url) if not already matched
    p2 = r'\[([^@\]]+)\]\((https?://[^\)\s]+)\)'
    for title, url in re.findall(p2, content):
        clean_title = title.strip()
        clean_url = url.strip()
        if not any(clean_url in r for r in resources):
            resources.append(f"[Article] {clean_title} - {clean_url}")

    # Pattern 3: Raw URLs on list lines
    p3 = r'-\s*(https?://[^\s\)]+)'
    for url in re.findall(p3, content):
        clean_url = url.strip()
        if not any(clean_url in r for r in resources):
            resources.append(f"[Docs] Official Reference - {clean_url}")

    return resources


def _extract_topic_overview(content: str) -> str:
    """Extract clean 1-2 sentence overview paragraph from markdown content."""
    if not content:
        return ""
    lines = [line.strip() for line in content.split("\n") if line.strip()]
    paragraphs: list[str] = []
    for line in lines:
        if line.startswith("#") or line.startswith("-") or line.startswith("*") or "visit the following" in line.lower():
            continue
        if len(line) > 20:
            paragraphs.append(line)
            if len(paragraphs) >= 2:
                break
    return " ".join(paragraphs)


def _topological_sort(nodes: list[str], edges: list[tuple[str, str]]) -> list[str]:
    """Kahn's algorithm — guarantees prerequisites appear before dependents."""
    in_degree: dict[str, int] = {n: 0 for n in nodes}
    adjacency: dict[str, list[str]] = defaultdict(list)

    for src, tgt in edges:
        if src in in_degree and tgt in in_degree:
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


TOPIC_ALIASES: dict[str, str] = {
    "c++": "cpp",
    "cpp": "cpp",
    "golang": "golang",
    "go": "golang",
    "c#": "aspnet-core",
    "csharp": "aspnet-core",
    ".net": "aspnet-core",
    "dotnet": "aspnet-core",
    "reactjs": "react",
    "next.js": "nextjs",
    "node.js": "nodejs",
    "node": "nodejs",
    "vue.js": "vue",
    "dsa": "datastructures-and-algorithms",
    "algorithms": "datastructures-and-algorithms",
    "data structures": "datastructures-and-algorithms",
    "game dev": "game-developer",
    "game development": "game-developer",
    "game programming": "game-developer",
    "ml": "machine-learning",
    "machine learning": "machine-learning",
    "ai": "ai-engineer",
    "ai agents": "ai-agents",
    "sys design": "system-design",
    "system design": "system-design",
    "db": "sql",
    "postgres": "postgresql-dba",
    "postgresql": "postgresql-dba",
}


def _find_knowledge_base_root() -> Path | None:
    """Find the knowledge-base/roadmaps directory across standard deployment paths."""
    candidates = [
        Path("/knowledge-base/roadmaps"),
        Path("/opt/speroflow/knowledge-base/roadmaps"),
        Path(__file__).resolve().parents[4] / "knowledge-base" / "roadmaps",
        Path(__file__).resolve().parents[3] / "knowledge-base" / "roadmaps",
        Path("knowledge-base/roadmaps"),
        Path("../knowledge-base/roadmaps"),
    ]
    for c in candidates:
        if c.is_dir():
            return c
    return None


def _retrieve_local_roadmap_dataset(goal_name: str) -> tuple[list[dict[str, Any]], list[tuple[str, str]]]:
    """
    Search curated local roadmap dataset directories and extract topics, descriptions, and direct URLs.
    """
    kb_root = _find_knowledge_base_root()
    if not kb_root:
        return [], []

    topic_clean = _clean_topic_name(goal_name).lower()
    topic_tokens = set(re.findall(r"[a-z0-9\+#]+", topic_clean))

    # Check for alias matches
    alias_target = None
    for alias_key, target in TOPIC_ALIASES.items():
        if alias_key in topic_clean or alias_key in topic_tokens:
            alias_target = target
            break

    # Match candidate roadmap directories
    best_dirs: list[tuple[int, Path]] = []
    for r_dir in kb_root.iterdir():
        if not r_dir.is_dir():
            continue
        d_name = r_dir.name.lower()
        d_name_spaces = d_name.replace("-", " ")
        score = 0

        if alias_target and (d_name == alias_target or alias_target in d_name):
            score += 200
        if d_name == topic_clean or d_name_spaces == topic_clean:
            score += 100
        elif topic_clean in d_name_spaces or d_name_spaces in topic_clean:
            score += 50
        else:
            d_tokens = set(re.findall(r"[a-z0-9\+#]+", d_name_spaces))
            overlap = topic_tokens.intersection(d_tokens)
            if overlap:
                score += len(overlap) * 15
        if score > 0:
            best_dirs.append((score, r_dir))

    best_dirs.sort(key=lambda x: x[0], reverse=True)
    if not best_dirs:
        return [], []

    matched_dir = best_dirs[0][1]
    content_dir = matched_dir / "content"
    topics: list[dict[str, Any]] = []
    edges: list[tuple[str, str]] = []

    if content_dir.is_dir():
        for md_file in sorted(content_dir.glob("*.md")):
            try:
                text = md_file.read_text(encoding="utf-8").strip()
            except Exception:
                continue
            title = md_file.stem.split("@")[0].replace("--", " - ").replace("-", " ").title()
            h1_match = re.search(r"^#\s+(.+)", text, re.MULTILINE)
            if h1_match:
                title = h1_match.group(1).strip()
            overview = _extract_topic_overview(text)
            resources = _extract_markdown_resources(text)
            topics.append({
                "title": title,
                "overview": overview,
                "resources": resources,
            })

    # Read JSON DAG edges if present
    json_path = matched_dir / f"{matched_dir.name}.json"
    if json_path.is_file():
        try:
            data = json_module.loads(json_path.read_text(encoding="utf-8"))
            node_map = {}
            for n in data.get("nodes", []):
                nid = n.get("id")
                lbl = n.get("data", {}).get("label") or n.get("data", {}).get("title")
                if nid and lbl:
                    node_map[nid] = lbl
            for e in data.get("edges", []):
                src_lbl = node_map.get(e.get("source"))
                tgt_lbl = node_map.get(e.get("target"))
                if src_lbl and tgt_lbl and src_lbl != tgt_lbl:
                    edges.append((src_lbl, tgt_lbl))
        except Exception:
            pass

    return topics, edges


# ── GraphRAG Context Retrieval ────────────────────────────────────────────────

async def _retrieve_graphrag_context(driver: AsyncDriver, goal_name: str) -> tuple[list[dict[str, Any]], list[str]]:
    """
    Retrieve real GraphRAG knowledge, roadmap.sh topics, relationships, and source citations from Neo4j & Knowledge Base.
    """
    topic = _clean_topic_name(goal_name)
    
    cypher_roadmap = """
        MATCH (r:Roadmap)
        WHERE toLower(r.roadmap_name) CONTAINS toLower($topic)
           OR toLower($topic) CONTAINS toLower(r.roadmap_name)
        MATCH (r)-[:CONTAINS]->(t:Topic)
        OPTIONAL MATCH (t)-[rel:LEADS_TO|RELATED_TO]->(next:Topic)
        RETURN t.label_text AS topic_label,
               t.url AS topic_url,
               t.content AS topic_content,
               next.label_text AS next_label
        LIMIT 60
    """

    nodes_map: dict[str, dict[str, Any]] = {}
    edges: list[tuple[str, str]] = []
    all_citations: set[str] = set()

    # 1. Query Neo4j Knowledge Graph
    try:
        async with driver.session() as session:
            res_rm = await session.run(cypher_roadmap, topic=topic)
            records_rm = await res_rm.data()
            for r in records_rm:
                lbl = (r.get("topic_label") or "").strip()
                url = (r.get("topic_url") or "").strip()
                nxt = (r.get("next_label") or "").strip()
                cnt = (r.get("topic_content") or "").strip()

                if lbl and len(lbl) > 2:
                    overview = _extract_topic_overview(cnt)
                    res_list = _extract_markdown_resources(cnt)
                    if url and url.startswith("http"):
                        res_list.append(f"[Official] {lbl} Docs - {url}")

                    nodes_map[lbl] = {
                        "title": lbl,
                        "overview": overview,
                        "resources": res_list,
                    }
                    for res in res_list:
                        all_citations.add(res)

                    if nxt and len(nxt) > 2:
                        edges.append((lbl, nxt))
    except Exception as err:
        logger.warning("Neo4j GraphRAG query notice: %s", err)

    # 2. Merge / Supplement with Local Curated Knowledge Base
    if len(nodes_map) < 4:
        local_topics, local_edges = _retrieve_local_roadmap_dataset(goal_name)
        for lt in local_topics:
            t_title = lt["title"]
            if t_title not in nodes_map:
                nodes_map[t_title] = lt
                for r in lt.get("resources", []):
                    all_citations.add(r)
        edges.extend(local_edges)

    # 3. Topological Sort of Topics
    topic_names = list(nodes_map.keys())
    sorted_names = _topological_sort(topic_names, edges) if topic_names else []

    sorted_topics = [nodes_map[name] for name in sorted_names if name in nodes_map]
    logger.info("GraphRAG retrieval for '%s': %d sorted topics, %d total direct citations.", goal_name, len(sorted_topics), len(all_citations))
    return sorted_topics, list(all_citations)


def _build_fallback_timeline(goal_name: str, structured_topics: list[dict[str, Any]], citations: list[str]) -> dict[str, Any]:
    """
    Build a dynamic, rich roadmap directly from the retrieved knowledge base topics and URLs if LLM synthesis is skipped.
    """
    cleaned = _clean_topic_name(goal_name)
    title_case = cleaned.title()

    steps = []
    
    if structured_topics and len(structured_topics) >= 3:
        total_items = len(structured_topics)
        step_count = min(6, total_items)
        chunk_size = max(1, total_items // step_count)
        
        for idx in range(step_count):
            start_idx = idx * chunk_size
            end_idx = min(total_items, (idx + 1) * chunk_size if idx < step_count - 1 else total_items)
            chunk = structured_topics[start_idx:end_idx]
            if not chunk:
                continue

            primary = chunk[0]
            step_title = f"{idx + 1}. {primary['title']}"
            overview = primary.get("overview") or f"Master fundamental concepts, mechanics, and best practices for {primary['title']} in {title_case}."
            
            step_res = []
            for c in chunk:
                for r in c.get("resources", []):
                    if r not in step_res:
                        step_res.append(r)
            if not step_res:
                step_res = citations[:3] if citations else [f"[Article] {title_case} Official Guide - https://devdocs.io/{urllib.parse.quote_plus(cleaned.lower())}"]

            subtasks = [
                f"Study core principles and syntax of {primary['title']}",
                f"Implement hands-on code examples for {primary['title']}",
                f"Verify unit tests and practical exercises for {primary['title']}",
            ]
            if len(chunk) > 1:
                subtasks.append(f"Explore advanced integration with {chunk[1]['title']}")

            steps.append({
                "topic": step_title,
                "description": overview,
                "estimated_hours": 6.0 + (idx * 2.0),
                "resources": step_res[:4],
                "subtasks": subtasks,
            })
    else:
        canonical_doc = f"https://devdocs.io/{urllib.parse.quote_plus(cleaned.lower())}"
        steps = [
            {
                "topic": f"1. {title_case} Core Syntax & Environment Toolchain",
                "description": f"Master foundational principles, compiler/runtime toolchain setup, package configuration, and core syntax of {title_case}.",
                "estimated_hours": 6.0,
                "resources": [f"[Docs] Official Reference - {canonical_doc}", f"[Guide] Getting Started with {title_case} - https://roadmap.sh"],
                "subtasks": [
                    f"Install CLI tools and environment for {cleaned}",
                    f"Write and verify initial baseline project for {cleaned}",
                    "Configure linter, formatter, and package manager",
                ],
            },
            {
                "topic": f"2. {title_case} Architecture & Core Building Blocks",
                "description": f"Understand structural paradigms, component/data models, memory & state management, and design patterns for {title_case}.",
                "estimated_hours": 8.0,
                "resources": [f"[Docs] Core Specs & Idioms - {canonical_doc}", f"[Article] {title_case} Best Practices - https://roadmap.sh"],
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
                "resources": [f"[Docs] Framework Ecosystem - {canonical_doc}", f"[Guide] Integration Architecture - https://roadmap.sh"],
                "subtasks": [
                    f"Integrate standard ecosystem packages for {cleaned}",
                    "Build async request handlers and state pipelines",
                    "Perform integration testing across services",
                ],
            },
            {
                "topic": f"4. System Design, Performance & Security",
                "description": f"Optimize execution performance, memory usage, security guardrails, caching, and multi-component system design.",
                "estimated_hours": 12.0,
                "resources": [f"[Docs] Performance & Profiling - {canonical_doc}", f"[Guide] Security Hardening - https://roadmap.sh"],
                "subtasks": [
                    f"Profile memory and CPU utilization for {cleaned}",
                    "Implement security validation and input sanitization",
                    "Benchmark high-throughput execution paths",
                ],
            },
            {
                "topic": f"5. Capstone Project Release & Production Deployment",
                "description": f"Synthesize all concepts by building, containerizing, testing, and deploying a complete production-grade {title_case} application.",
                "estimated_hours": 16.0,
                "resources": [f"[Docs] Deployment Architecture - {canonical_doc}", f"[Guide] Production Engineering - https://roadmap.sh"],
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
        "motivational_summary": f"Embark on a customized GraphRAG learning path to master {title_case} step by step!",
    }


# ── Router ────────────────────────────────────────────────────────────────────

@router.post(
    "/prerequisites",
    response_model=LearningTimeline,
    summary="Generate an interactive GraphRAG prerequisite learning path with direct resources for a goal topic",
)
async def get_prerequisites(
    request: PrerequisiteRequest,
    current_user: dict = Depends(require_verified_user),
    settings: Settings = Depends(get_app_settings),
    driver: AsyncDriver = Depends(get_neo4j),
) -> LearningTimeline:
    """
    Pipeline: Goal → Neo4j / KB GraphRAG Retrieval (roadmap.sh DAG) → Gemma 4 31B Synthesis → LearningTimeline with Direct Resources & Checklist
    """
    logger.info("GraphRAG Prerequisites request for goal: '%s'", request.goal_name)
    
    # 1. Retrieve GraphRAG Knowledge, Overviews & Direct Citations
    structured_topics, citations = [], []
    try:
        structured_topics, citations = await _retrieve_graphrag_context(driver, request.goal_name)
    except Exception as exc:
        logger.warning("GraphRAG retrieval notice: %s", exc)

    # 2. Build Rich Context Prompt for Gemma 4 31B
    context_blocks = [f"USER TARGET GOAL: {request.goal_name}"]
    
    if structured_topics:
        topic_summaries = []
        for st in structured_topics[:15]:
            title = st.get("title", "")
            overview = st.get("overview", "")
            res = st.get("resources", [])
            block = f"--- Topic: {title} ---\nOverview: {overview}"
            if res:
                block += "\nDirect Verified Resources:\n" + "\n".join(f"  * {r}" for r in res[:3])
            topic_summaries.append(block)
        context_blocks.append("RETRIEVED GRAPH TOPOLOGY & DIRECT RESOURCES:\n" + "\n\n".join(topic_summaries))
    elif citations:
        context_blocks.append("RETRIEVED SOURCE CITATIONS:\n" + "\n".join(f"- {c}" for c in citations[:10]))

    full_context = "\n\n".join(context_blocks)

    parsed = None
    
    # 3. Invoke Gemma 4 31B via invoke_bedrock or create_chat_model
    try:
        raw_text = await invoke_bedrock(
            model_id=settings.llm_model,
            system_prompt=SYSTEM_PROMPT,
            user_text=full_context,
            settings=settings,
            max_tokens=2500,
            temperature=0.2,
        )
        clean = raw_text.strip()
        json_match = re.search(r'```(?:json)?\s*\n(.*?)```', clean, re.DOTALL)
        if json_match:
            clean = json_match.group(1)
        parsed = json_module.loads(clean.strip())
        logger.info("Gemma 4 31B GraphRAG roadmap synthesis successful for '%s'.", request.goal_name)
    except Exception as exc1:
        logger.warning("Primary invoke_bedrock (%s) notice: %s. Trying chat model fallback...", settings.llm_model, exc1)
        try:
            from speroflow_ai.services.chat_model import create_chat_model
            llm = create_chat_model(
                provider=settings.llm_provider,
                model=settings.llm_model,
                api_base=settings.llm_api_base,
                api_key=settings.llm_api_key,
                temperature=0.2,
                bedrock_region=settings.bedrock_region,
                max_tokens=2000,
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
            logger.warning("Secondary LLM synthesis notice: %s", exc2)

    if not parsed or not isinstance(parsed.get("steps"), list) or len(parsed.get("steps", [])) == 0:
        parsed = _build_fallback_timeline(request.goal_name, structured_topics, citations)

    steps_res = []
    for idx, s in enumerate(parsed.get("steps", [])):
        res_list = s.get("resources")
        if not isinstance(res_list, list) or not res_list:
            t_name = s.get("topic", f"Step {idx + 1}")
            res_list = citations[:3] if citations else [
                f"[Docs] Official Reference - https://devdocs.io/{urllib.parse.quote_plus(request.goal_name.lower())}",
            ]
        
        desc = str(s.get("description", ""))
        subtasks = s.get("subtasks")
        if isinstance(subtasks, list) and subtasks:
            if "Key Work Items:" not in desc and "Subtasks:" not in desc:
                desc = desc.strip() + "\n\nKey Work Items:\n" + "\n".join(f"• {st}" for st in subtasks)

        steps_res.append(
            LearningStep(
                topic=str(s.get("topic", f"Step {idx + 1}")),
                description=desc,
                estimated_hours=float(s.get("estimated_hours", 6.0)),
                resources=[str(r) for r in res_list],
            )
        )

    return LearningTimeline(
        goal=str(parsed.get("goal", request.goal_name)),
        steps=steps_res,
        total_estimated_hours=sum(step.estimated_hours for step in steps_res),
        motivational_summary=str(parsed.get("motivational_summary", f"A personalized GraphRAG learning path for {request.goal_name}.")),
    )
