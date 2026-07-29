# Product Objective Status

This file records the delivery state of the Covey-inspired product objective so
future work preserves the central invariant: AI can suggest, but only the user
can apply a change.

## Implemented Foundation

- Owner-scoped `LifeRole` records distinguish internal and external roles.
- New accounts receive the four core internal roles: Mental, Physical, Social,
  and Spiritual. Existing accounts initialize them through an idempotent,
  CSRF-protected endpoint.
- Tasks can point to a specific life role while retaining their existing life
  area for Matrix, calendar, and legacy workflows.
- The sidebar reads PostgreSQL-backed roles and tasks instead of demo data.
- `/roles` lets users create, edit, archive, restore, and review life roles.
- `AiActionProposal` is the shared durable approval queue. Its states are
  `Pending`, `Approved`, and `Cancelled`, and every resolution is audited.
- The Balance evaluator receives an owner-scoped, role-level activity aggregate.
  It can propose one Q2 task only for the exact active role it identifies; approval
  revalidates ownership and the payload before creating the role-linked task in the
  same database transaction. Cancellation makes no workspace change.
- Role discovery builds a bounded owner-scoped snapshot from unassigned tasks,
  active projects, and active habits. The private AI service returns only
  candidates; ASP.NET encrypts evidence and creates approval-gated
  `CreateLifeRole` proposals. Owners review evidence and explicitly approve
  or cancel every candidate.
- Journal reflections are generated only through the private `ai-api`, stored
  as encrypted `JournalInsight` records, and presented as a pending review.
  The shared proposal payload carries only record IDs and an entry revision,
  never journal or reflection text.
- Approving a journal reflection attaches it only to the exact journal revision
  it analyzed. Editing the entry cancels pending reviews and makes an approved
  historical insight invisible for the new revision.
- Goals, milestones, linked tasks, and GraphRAG roadmaps are owner-scoped.
  A roadmap becomes durable milestones only after the owner approves its
  proposal.
- Eisenhower classification uses an owner-bounded snapshot of active goals,
  recent journal entries, and approved insights. It creates a task-change
  proposal and never directly changes a quadrant.
- Intelligent scheduling is an approval-first Calendar workflow. ASP.NET
  assembles the owner-checked calendar, task, and role snapshot; the private
  scheduler returns a bounded focus-block suggestion; and approval rechecks
  freshness, due dates, and collisions before it writes `StartAt` and duration.
- Pending schedule suggestions are invalidated by any manual task change.
  The legacy transient `/ai/schedule` route is retired.
- Dedicated Coach conversation and observation model added (`CoachConversation`, `CoachMessage`,
  `CoachObservation`). Coach messages, habit ideas (`CreateHabit`), task suggestions (`CreateTask`),
  roadmap changes (`ApplyGoalRoadmap`), and scheduling changes (`ApplyTaskSchedule`) enter the unified
  `AiActionProposal` approval queue. User approval revalidates ownership and target payload before creating
  or updating workspace records.

## Non-Negotiable Rules

1. Browser clients never choose a proposal owner or call a private AI service.
2. AI services never write the primary application database.
3. Every proposal is owner-scoped, optimistic-concurrency protected, audited,
   and must be explicitly approved before a mutation occurs.
4. A proposal is applied only after ASP.NET validates its current payload,
   ownership links, and target state again.
5. Agents return plans or recommendations, not unreviewed mutations.

## Next Product Slices

- Matrix and Calendar intelligence: expand interactive Eisenhower & focus scheduling insights with Coach feedback loops.

## Verification Completed For This Slice

- ASP.NET Core solution build succeeds with zero warnings and zero errors.
- Domain unit test suite passes (23 tests), including CoachConversation, CoachMessage, CoachObservation, and CreateHabit proposal lifecycle.
- AI-core Coach service implementation with structured output parsing and deterministic keyword fallback.
- Frontend `/coach` workspace view, top navigation, and sidebar integration built with Next.js.
- Runtime integration still requires a PostgreSQL-backed Compose environment before production cutover.

## Phase 1–2 production hardening (landed in tree)

Completed engineering hardening toward pilot readiness (not full public production):

### Phase 1
- Secrets bootstrap no longer writes plaintext credential dumps by default; inventory is fingerprints/presence only.
- Hardcoded Bedrock API key default removed from bootstrap scripts.
- GitHub Actions CI covers compose validation, domain tests, knowledge infra tests, AI-core offline tests, frontend test/build.
- Compose validation covers main, prod, GPU profile, and knowledge stacks.
- API entrypoint canonicalized to a single `Program.cs` (OIDC + CSRF + JSON logs).
- `scripts/backup-volumes.ps1` added for logical Postgres dumps (optional Neo4j/MinIO).

### Phase 2
- Local plaintext `CREDENTIALS_SUMMARY.md` removed; prefer inventory-only summaries.
- `scripts/check-no-secrets.ps1` + CI secret-pattern guard and Gitleaks.
- `scripts/smoke-release.ps1` for post-deploy health/port audits (no app credentials).
- AI service JWT enforcement unit tests (`ai-core/tests/test_service_auth.py`).
- Caddy edge headers tightened (`X-Frame-Options`, COOP/CORP).
- CI dependency vulnerability reports (dotnet list / npm audit, informational).

### Phase 3
- `scripts/preflight.ps1` chains secret scan, compose validation, and unit tests.
- `scripts/stack-status.ps1` aggregates container health + host port audit.
- Request correlation (`X-Request-Id`) and timing logs on main API and AI API.
- AI `/health/ready` returns HTTP 503 when Neo4j is down (orchestrator-friendly).
- `compose.prod.yaml`: `LOG_FORMAT=json`, log rotation, `SERVICE_NAME` for AI services.

### Phase 4
- Caddy forwards `X-Request-Id` / `X-Correlation-ID` to app and knowledge APIs.
- `scripts/restore-postgres.ps1` guarded restore helper (`-ConfirmRestore` required).
- Smoke probe documents correlation-header behavior.

### Managed secrets (Phases A–B scaffolding — in tree)
- Design: `infrastructure/MANAGED_SECRETS.md` (SM source of truth, host materialize for Compose).
- Catalog: `infrastructure/aws/secrets-catalog.json` + `scripts/validate-secrets-catalog.ps1`.
- IAM JSON templates + **CloudFormation** `speroflow-secrets-stack.yaml` (KMS, read/admin policies, optional EC2 role).
- Scripts: `aws-secrets-push.ps1`, `aws-secrets-pull.ps1`, `aws-secrets-sync.ps1`.
- Boot helpers: `speroflow-secrets-pull.service`, `speroflow-secrets-pull.sh`.
- CI: catalog sync + push/pull dry-run + CFN template parse; preflight includes the same gates.
- SSM reserved for non-secret config examples only.

### Observability + smoke (in tree)
- Prometheus text metrics on private `/metrics` for main API and AI API (Caddy blocks public access).
- Optional `compose.monitoring.yaml` profile with Prometheus (localhost:9090 only).
- CloudWatch agent example config under `infrastructure/monitoring/`.
- Dependency-light `scripts/e2e-smoke.mjs` (+ CI syntax job) for edge health/header checks.

### Playwright browser E2E (in tree)
- `e2e/` package with mocked API + Next rewrites (`API_PROXY_TARGET`) for login, CSRF header, proposal approve.
- Live project when `E2E_BASE_URL` + credentials are set.
- CI job `playwright-e2e-mocked` runs Chromium against mock stack.

Still open before public production: HA/multi-AZ, **run CFN + first live push in the AWS account**, SM rotation Lambdas, ECS native secret injection, Grafana/alerting rules, knowledge legacy table retirement, live restore drills on real hosts, richer live Playwright coverage (full proposal lifecycle against real AI).