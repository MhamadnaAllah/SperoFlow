# 🚀 SperoFlow AI Platform

> **The Digital Architect's Life Operating System**  
> An enterprise-grade, human-in-the-loop personal productivity and mental well-being platform combining GTD® workflows, Eisenhower Matrix prioritization, OKRs alignment, Affective Journaling, and isolated GraphRAG knowledge administration.

---

## 🌟 Key Architecture & Features

### 🎨 Redesigned Light-Mode Editorial Landing Page
- **Material 3 Design System**: Built with Next.js 15, Tailwind CSS 3.4, and custom glassmorphism (`backdrop-blur-xl`), ambient shadow glows, and tonal depth.
- **GPU Wave Shader**: Modular `HeroShaderCanvas.jsx` WebGL fluid wave canvas background mixing primary blue (`#0053dc`) with surface background.

### 🛡️ Human-in-the-Loop (HITL) AI Safety Invariant
- **Approval-First Execution**: The AI engine acts strictly as a **proposer**, generating `AiActionProposal` records.
- **Zero Autonomous Mutation**: The ASP.NET Core backend re-validates contracts and business logic before applying state mutations—and **only** after explicit user approval.

### 🧠 Dual Mode Emotion Classification (RoBERTa + Bedrock)
- **Local ONNX CPU Inference**: High-speed (~12ms) local emotion tagging via `j-hartmann/emotion-english-distilroberta-base` ONNX Runtime.
- **Standardized Taxonomy**: Extracts 7 canonical emotion classes (`Joy`, `Sadness`, `Anger`, `Fear`, `Surprise`, `Disgust`, `Neutral`) locally while using AWS Bedrock for warm non-clinical narrative synthesis.

### 📚 Isolated Knowledge Platform (GraphRAG)
- **Air-Gapped Data Isolation**: Complete separation of knowledge administration from the main web application.
- **Curated Datasets**: Deterministic SHA-256 source manifests tracking 320 CBT domain documents and 9,978 learning roadmaps over Neo4j and MinIO.

---

## 🏗️ System Architecture

```mermaid
flowchart TD
    subgraph Edge Layer
        Browser["User Browser / Mobile"] ──► Caddy["Caddy Reverse Proxy (TLS)"]
    end

    subgraph App Layer
        Caddy ──► Web["Next.js 15 Frontend"]
        Caddy ──► Api[".NET 10 Web API"]
        Api ──► AppDb[("PostgreSQL")]
        Api ──► AppRedis[("Redis Outbox")]
        ApiWorker[".NET Outbox Worker"] ──► AppRedis
    end

    subgraph AI Core Layer
        Api ──►|"Signed Service JWT"| AiApi["FastAPI AI Core"]
        AiWorker["Python AI Worker"] ──► AppRedis
        AiApi ──► RoBERTa["ONNX DistilRoBERTa (CPU)"]
        AiApi ──► Bedrock["AWS Bedrock (Gemma 31B / Claude 3.5)"]
    end

    subgraph Knowledge Platform
        Admin["Knowledge Admin"] ──► Caddy
        Caddy ──► Portal["Knowledge Portal"]
        Caddy ──► KnowledgeApi["Knowledge API"]
        Api ──►|"Short-lived Grant (90s)"| KnowledgeApi
        KnowledgeApi ──► KnowledgeDb[("Knowledge PostgreSQL")]
        KnowledgeApi ──► KnowledgeStore[("Private MinIO S3")]
        KnowledgeWorker["Knowledge Worker"] ──► KnowledgeGraph[("Neo4j Graph DB")]
        AiApi ──►|"Read-Only Graph Bridge"| KnowledgeGraph
    end
```

---

## 📁 Repository Layout

| Directory | Description |
|---|---|
| [`frontend/`](frontend/) | Next.js 15 App Router frontend with React 19, Tailwind CSS 3.4, and Material 3 design tokens. |
| [`backend/`](backend/) | .NET 10 C# Web API, Background Worker, EF Core migrations, and Data Protection engine. |
| [`ai-core/`](ai-core/) & [`ai-api/`](ai-api/) | FastAPI AI Core with local ONNX DistilRoBERTa emotion classifier & AWS Bedrock RAG routing. |
| [`ai-worker/`](ai-worker/) | Asynchronous Redis stream worker for AI background processing. |
| [`knowledge-platform/`](knowledge-platform/) | Isolated Knowledge Portal (Next.js), API (.NET 10), Worker (.NET 10), PostgreSQL, Redis, MinIO, & Neo4j. |
| [`knowledge-base/`](knowledge-base/) | Curated CBT resources (320 docs) and roadmaps (9,978 files) with deterministic SHA-256 manifests. |
| [`infrastructure/`](infrastructure/) | Caddy reverse proxy configurations, Dockerfiles, AWS setup guides, and secrets bootstrap scripts. |

---

## ⚡ Quickstart & Deployment

### 1. Local Development Setup

```bash
# 1. Create Docker bridge networks
pwsh ./scripts/Initialize-KnowledgePlatformNetworks.ps1

# 2. Secrets: local bootstrap for lab, OR pull from AWS Secrets Manager on a real host
#    See infrastructure/MANAGED_SECRETS.md
pwsh ./scripts/bootstrap-secrets.ps1
# pwsh ./scripts/aws-secrets-pull.ps1 -Environment prod -Region us-east-1

# 3. Create .env configuration
cp .env.example .env

# 4. Validate Compose, then start Knowledge Platform & Main App
pwsh ./scripts/validate-compose.ps1
docker compose -f knowledge-platform/compose.yaml up -d --build
docker compose up -d --build
```

Do **not** commit `secrets/`, `secrets_backup/`, or `.env`. Before pushing from a machine that generated secrets, run `pwsh ./scripts/reset-secrets-before-git.ps1`.

```bash
# Preflight (secrets + compose + tests) and post-deploy checks (no app login required)
pwsh ./scripts/preflight.ps1
pwsh ./scripts/stack-status.ps1
pwsh ./scripts/smoke-release.ps1
pwsh ./scripts/e2e-smoke.ps1
# Playwright (login / CSRF / proposals) — see e2e/README.md
#   cd e2e; npm install; npx playwright install chromium; npm run test:mocked
# optional metrics: docker compose -f compose.yaml -f compose.monitoring.yaml --profile monitoring up -d
pwsh ./scripts/backup-volumes.ps1
# restore is destructive — only with -ConfirmRestore on a drill host
# pwsh ./scripts/restore-postgres.ps1 -Target main -DumpFile .\backups\...\main-postgres.dump -ConfirmRestore
```

### 2. Single AWS EC2 Deployment (Production Mode)

To deploy on a single AWS EC2 instance (`m6i.2xlarge` / `m6i.xlarge`) with Bedrock LLM routing and host OOM resource limits:

```bash
# Start Knowledge Platform
docker compose -f knowledge-platform/compose.yaml up -d --build

# Start Main Stack with Production Override
docker compose -f compose.yaml -f compose.prod.yaml up -d --build
```

See [`infrastructure/DEPLOYMENT.md`](infrastructure/DEPLOYMENT.md) and [`infrastructure/OPERATIONS.md`](infrastructure/OPERATIONS.md) for pre-flight checks, backups, and secret rotation.

CI runs on push/PR via [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

---

## 📄 License & Verification

The SperoFlow AI Platform preserves strict license permissions (`--confirm-license-permission`) for all integrated CBT and Roadmap knowledge datasets. See [`CUTOVER.md`](knowledge-platform/CUTOVER.md) for data integrity verification protocols.
