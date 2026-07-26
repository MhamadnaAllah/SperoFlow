# Project: SperoFlow AI Full-Stack Team Testing & Architectural Audit Charter

## Overview
Comprehensive audit, testing, and verification of the SperoFlow AI platform across five senior specialist personas (QA Guardian, Architect Sentinel, Builder Engineer, AI Oracle, DevOps Forge) covering requirements R1 through R5 and all core product invariants.

## Architecture & Subsystems
- **Frontend**: Next.js 15 App Router (`frontend/`)
- **Backend API**: .NET 10 ASP.NET Core API (`backend/`)
- **AI Core & API**: Python FastAPI services & AI Core (`ai-api/`, `ai-core/`, `ai-worker/`)
- **Knowledge Platform**: Knowledge API, Worker, Portal, Neo4j, MinIO, Postgres (`knowledge-platform/`)
- **Infrastructure & Security**: Caddy, Redis, Docker Compose stacks, network segmentation, secret mounts.

## Milestones
| # | Specialist Persona / Milestone | Scope | Dependencies | Status |
|---|--------------------------------|-------|-------------|--------|
| 1 | R1: Senior QA Guardian | Unit & Integration Test Suite, 9 approval-gated workflows, negative security tests | None | DONE |
| 2 | R2: Architect Sentinel | Service Topology, 4 network segmentations, API contracts, secrets, DB schemas, OIDC audit | None | DONE |
| 3 | R3: Builder Engineer | Code Quality, ASP.NET build/test, Next.js build, FastAPI compile, Redis stream worker logic | None | DONE |
| 4 | R4: AI Oracle | Neo4j graph schema, GraphRAG retrieval accuracy, Bedrock LLM synthesis, prompt injection safety, fallback | None | DONE |
| 5 | R5: DevOps Forge | Docker Compose parsing/validation, container security hardening, health checks, network scripts, Git reset script | None | DONE |
| 6 | Synthesis & Forensic Audit | Consolidated Health Report, Domain Audit Matrix, Prioritized Remediation Backlog, Forensic Integrity Audit | M1-M5 | DONE |

## Core Product Invariants
1. AI can suggest, but only the user can apply a change (`AiActionProposal` approval queue gating).
2. Browser clients never choose a proposal owner or call a private AI service.
3. AI services never write the primary application database (`speroflow`).
4. Every proposal is owner-scoped, optimistic-concurrency protected, and audited.
5. Proposals are re-validated by ASP.NET before database mutation.
6. Agents return recommendations/plans, never unreviewed direct mutations.

