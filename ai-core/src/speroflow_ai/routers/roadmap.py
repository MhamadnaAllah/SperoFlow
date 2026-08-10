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

Your task is to create a comprehensive, highly detailed, step-by-step learning roadmap tailored specifically to a GOAL.

Guidelines:
1. Divide the learning path into 5 to 8 logical, progressive, and highly specific steps for the target goal.
2. Ensure every step topic is rich, detailed, and directly names core technologies, tools, design patterns, and architectures relevant to the goal.
3. For each step, provide:
   - "topic": Clear, highly specific step title (e.g., "1. Game Loop Architecture & Math Fundamentals", "2. Physics Engines & Collision Systems")
   - "description": Comprehensive objective detailing what key concepts to master and what to build
   - "estimated_hours": Realistic study and hands-on practice time in hours (e.g., 5.0, 8.0, 12.0)
   - "resources": A list of 3-4 actionable official docs, textbooks, interactive tutorials, or hands-on practice project specs
4. Write an inspiring "motivational_summary" summarizing the learning journey.

Respond ONLY as a valid JSON object matching this schema:
{
  "goal": "Goal Name",
  "steps": [
    {
      "topic": "Step Title",
      "description": "Clear, detailed step objective and overview.",
      "estimated_hours": 6.0,
      "resources": ["Official Documentation / Guide", "Key Practice Project", "Core Architecture Spec"]
    }
  ],
  "total_estimated_hours": 35.0,
  "motivational_summary": "Motivational summary text..."
}
"""

def _clean_topic_name(raw: str) -> str:
    """Strip action phrases like 'i want to learn', 'how to', 'master'."""
    text = raw.strip()
    pattern = r"^(?:i\s+want\s+to\s+learn|how\s+to\s+learn|how\s+to|learn\s+about|learn|master|study|guide\s+to)\s+"
    cleaned = re.sub(pattern, "", text, flags=re.IGNORECASE).strip()
    return cleaned if len(cleaned) >= 2 else text

# ── Cypher Traversal ──────────────────────────────────────────────────────────

async def _fetch_prerequisite_subgraph(
    driver: AsyncDriver,
    goal_name: str,
) -> tuple[list[str], list[tuple[str, str]]]:
    topic = _clean_topic_name(goal_name)
    cypher = """
        MATCH path = (prereq)-[:LEADS_TO|DEPENDS_ON|CONTAINS*1..10]->(goal)
        WHERE (goal:Topic OR goal:Subtopic OR goal:Roadmap)
          AND (toLower(goal.label_text) CONTAINS toLower($topic) OR toLower($topic) CONTAINS toLower(goal.label_text))
          AND (prereq:Topic OR prereq:Subtopic OR prereq:Roadmap)
          AND size(toLower(prereq.label_text)) > 3
        UNWIND relationships(path) AS rel
        WITH DISTINCT
            startNode(rel).label_text AS source,
            endNode(rel).label_text AS target
        WHERE size(trim(source)) > 3 AND size(trim(target)) > 3
        RETURN source, target
    """
    nodes: set[str] = set()
    edges: list[tuple[str, str]] = []

    async with driver.session() as session:
        result = await session.run(cypher, topic=topic)
        records = await result.data()
        for record in records:
            src, tgt = record["source"], record["target"]
            if len(src.strip()) > 3 and len(tgt.strip()) > 3:
                nodes.add(src)
                nodes.add(tgt)
                edges.append((src, tgt))

    # Filtered topics list (exclude noise like 'R', 'P', 'MIN')
    filtered_nodes = [n for n in nodes if len(n.strip()) > 3]

    logger.info("Subgraph for '%s' (cleaned: '%s'): %d nodes, %d edges.", goal_name, topic, len(filtered_nodes), len(edges))
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
    cleaned = _clean_topic_name(goal_name).lower()
    title_case = _clean_topic_name(goal_name).title()

    if "game" in cleaned:
        steps = [
            {
                "topic": "1. C++ & C# Fundamentals for Game Engines",
                "description": "Master memory management, pointers, object-oriented design patterns, and CLI toolchains essential for game development.",
                "estimated_hours": 8.0,
                "resources": [
                    "https://learn.microsoft.com/en-us/dotnet/csharp/ - C# Official Language & .NET Guide",
                    "https://en.cppreference.com/w/ - Modern C++ Language & STL Reference",
                    "https://learncpp.com - Interactive C++ Game Programming Tutorials",
                ],
            },
            {
                "topic": "2. Game Loop, Mathematics & Vector Physics",
                "description": "Implement frame timing, delta time, 2D/3D linear algebra, vector dot/cross products, matrices, and basic collision detection.",
                "estimated_hours": 10.0,
                "resources": [
                    "https://gameprogrammingpatterns.com - Game Programming Patterns & Game Loop Architecture",
                    "https://khanacademy.org/math/linear-algebra - Linear Algebra & Vectors for Game Physics",
                    "https://gafferongames.com/post/fix_your_timestep/ - Game Loop & Physics Timestep Integration",
                ],
            },
            {
                "topic": "3. Game Engines: Unity & Unreal Engine Setup",
                "description": "Explore GameObjects, Prefabs, Scene Graphs, Component-Based Architecture, Blueprinting, and Physics Controllers.",
                "estimated_hours": 12.0,
                "resources": [
                    "https://docs.unity3d.com/Manual/ - Unity Engine Developer Manual & Scripting Reference",
                    "https://dev.epicgames.com/documentation/en-us/unreal-engine/ - Unreal Engine C++ & Blueprint Guide",
                    "https://learn.unity.com - Unity Learn Pathways & Hands-on Projects",
                ],
            },
            {
                "topic": "4. Shaders, Graphics Pipeline & Audio Systems",
                "description": "Understand HLSL/GLSL shaders, vertex/fragment processing, lighting models, material graphs, and spatial 3D audio.",
                "estimated_hours": 10.0,
                "resources": [
                    "https://learnopengl.com - Learn OpenGL & Graphics Pipeline Foundations",
                    "https://thebookofshaders.com - The Book of Shaders & Fragment Processing Guide",
                    "https://fmod.com/docs/ - FMOD & Spatial Audio Integration for Games",
                ],
            },
            {
                "topic": "5. AI Behaviors, Pathfinding & Capstone Game Release",
                "description": "Build finite state machines, A* pathfinding, enemy AI behaviors, dynamic UI, and publish a complete playable game build.",
                "estimated_hours": 15.0,
                "resources": [
                    "https://redblobgames.com/pathfinding/a-star/introduction.html - A* Pathfinding & Grid Algorithms",
                    "https://itch.io/docs/creators - Itch.io Game Publishing & Build Distribution",
                    "https://unity.com/resources/capstone - Complete 2D/3D Game Capstone Specification",
                ],
            },
        ]
    elif "ai" in cleaned or "machine" in cleaned or "model" in cleaned or "data" in cleaned:
        steps = [
            {
                "topic": "1. Python, Math & Data Engineering Foundations",
                "description": "Master Python data science stack (NumPy, Pandas), linear algebra, probability, calculus, and matrix operations.",
                "estimated_hours": 8.0,
                "resources": [
                    "https://numpy.org/doc/stable/ - NumPy Fundamental Array & Matrix Operations",
                    "https://pandas.pydata.org/docs/ - Pandas Data Manipulation & Feature Engineering",
                    "https://scikit-learn.org/stable/ - Scikit-Learn Machine Learning Architecture",
                ],
            },
            {
                "topic": "2. Deep Learning Frameworks (PyTorch & CUDA)",
                "description": "Build neural networks, backpropagation, gradient descent, loss functions, CNNs, RNNs, and GPU acceleration in PyTorch.",
                "estimated_hours": 12.0,
                "resources": [
                    "https://pytorch.org/tutorials/ - PyTorch Official Deep Learning Tutorials",
                    "https://deeplearning.ai - Deep Learning Specialization & Neural Networks",
                    "https://huggingface.co/docs - HuggingFace Model Hub & Transformer Pipeline",
                ],
            },
            {
                "topic": "3. Large Language Models, RAG & Vector Databases",
                "description": "Implement vector embeddings, Neo4j GraphRAG, hybrid retrieval, prompt engineering, and LLM orchestration with LangChain.",
                "estimated_hours": 12.0,
                "resources": [
                    "https://python.langchain.com/docs/ - LangChain LLM Application Framework",
                    "https://neo4j.com/docs/graph-data-science/ - Neo4j Knowledge Graph & Vector Search",
                    "https://pinecone.io/learn/ - Vector Indexing & Hybrid RAG Architecture",
                ],
            },
            {
                "topic": "4. Model Fine-Tuning & Quantization (LoRA / QLoRA)",
                "description": "Fine-tune open-weight models (Llama, Gemma) using PEFT, LoRA, Unsloth, DPO, and GGML/GGUF quantization for local deployment.",
                "estimated_hours": 10.0,
                "resources": [
                    "https://huggingface.co/docs/peft/ - Parameter-Efficient Fine-Tuning (PEFT/LoRA)",
                    "https://unsloth.ai/docs - Unsloth Fast Model Fine-Tuning & Quantization",
                    "https://ollama.com/blog - Local LLM Inference & GGUF Model Deployment",
                ],
            },
            {
                "topic": "5. MLOps, Async API & Production Deployment",
                "description": "Deploy AI services with FastAPI, Docker, Ray Serve, monitoring, drift detection, and CI/CD pipelines.",
                "estimated_hours": 15.0,
                "resources": [
                    "https://fastapi.tiangolo.com - FastAPI Production Async Web Services",
                    "https://mlflow.org/docs/latest/ - MLflow Model Lifecycle & Experiment Tracking",
                    "https://docker.com/get-started/ - Containerizing AI & LLM Worker Pipelines",
                ],
            },
        ]
    else:
        steps = [
            {
                "topic": f"1. Core Foundations & Toolchain Setup for {title_case}",
                "description": f"Master foundational principles, CLI configuration, syntax, and essential concepts of {cleaned}.",
                "estimated_hours": 5.0,
                "resources": [
                    f"https://www.google.com/search?q=official+{cleaned}+documentation - Official {title_case} Documentation",
                    f"https://www.google.com/search?q={cleaned}+cli+setup - CLI & Environment Configuration",
                    f"https://www.google.com/search?q={cleaned}+getting+started - Hands-on Getting Started Exercises",
                ],
            },
            {
                "topic": f"2. Essential Architecture & Core Patterns",
                "description": f"Understand structural paradigms, data models, memory management, and design patterns for {cleaned}.",
                "estimated_hours": 8.0,
                "resources": [
                    f"https://www.google.com/search?q={cleaned}+architecture+guide - {title_case} Architecture & API Specs",
                    f"https://www.google.com/search?q={cleaned}+design+patterns - Deep-dive Code Examples & Patterns",
                    f"https://www.google.com/search?q={cleaned}+unit+testing - Unit Testing & Error Handling",
                ],
            },
            {
                "topic": f"3. Advanced Framework Ecosystem & Optimization",
                "description": f"Explore ecosystem packages, state management, asynchronous operations, and performance profiling.",
                "estimated_hours": 10.0,
                "resources": [
                    f"https://www.google.com/search?q={cleaned}+packages+libraries - Popular Packages & Ecosystem Tools",
                    f"https://www.google.com/search?q={cleaned}+performance+optimization - Performance Benchmarking",
                    f"https://www.google.com/search?q={cleaned}+security+best+practices - Security & Best Practice Checklist",
                ],
            },
            {
                "topic": f"4. System Design, Integration & Testing",
                "description": f"Implement complex workflows, API integrations, database schemas, and multi-service architectures.",
                "estimated_hours": 10.0,
                "resources": [
                    f"https://www.google.com/search?q={cleaned}+system+design - System Design Specification for {title_case}",
                    f"https://www.google.com/search?q={cleaned}+integration+testing - Automated Test Suites",
                    f"https://www.google.com/search?q={cleaned}+cicd+pipeline - CI/CD Pipeline & Build System Setup",
                ],
            },
            {
                "topic": f"5. Capstone Portfolio Application & Production Deployment",
                "description": f"Synthesize all concepts by building, testing, and deploying a complete production-grade {cleaned} project.",
                "estimated_hours": 15.0,
                "resources": [
                    f"https://www.google.com/search?q={cleaned}+capstone+project - Capstone Project Specification",
                    f"https://www.google.com/search?q={cleaned}+deployment+guide - Production Deployment Documentation",
                    f"https://www.google.com/search?q={cleaned}+code+review - Code Review & Portfolio Checklist",
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
    try:
        import json as json_module
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
            {"role": "user", "content": context},
        ]
        response = await llm.ainvoke(messages)
        raw = response.content if hasattr(response, "content") else str(response)

        clean = raw.strip()
        json_match = re.search(r'```(?:json)?\s*\n(.*?)```', clean, re.DOTALL)
        if json_match:
            clean = json_match.group(1)
        parsed = json_module.loads(clean.strip())
    except Exception as exc:
        logger.warning("LLM roadmap synthesis unavailable or failed (%s); using fallback generator.", exc)

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

