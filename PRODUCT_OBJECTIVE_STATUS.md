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