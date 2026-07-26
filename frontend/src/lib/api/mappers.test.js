import { describe, expect, it } from "vitest";

import { mapAiProposalDto, mapGoalDto, mapGoalRoadmapProposalDto, mapJournalAnalysisDto, mapJournalEntryDto, mapLifeRoleDto, mapProjectDto, mapRoleDiscoveryRunDto, mapTaskDto } from "./mappers";

const project = {
  id: "019f1fb5-4299-7062-a4fb-5f6cc0fcb120",
  name: "Launch",
  description: null,
  color: "indigo",
  icon: "rocket_launch",
  startAt: null,
  targetAt: "2026-08-01T17:00:00.000Z",
  state: "active",
  sortOrder: 1000,
  totalTaskCount: 4,
  completedTaskCount: 1,
  progressPercent: 25,
  concurrencyToken: "019f1fb5-4299-7062-a4fb-5f6cc0fcb121",
  createdAt: "2026-07-19T10:00:00.000Z",
  updatedAt: "2026-07-19T10:00:00.000Z",
};

const task = {
  id: "019f1fb5-4299-7062-a4fb-5f6cc0fcb122",
  title: "Draft the announcement",
  description: null,
  lifeArea: "work",
  quadrant: "q2",
  state: "inProgress",
  projectId: project.id,
  roleId: null,
  goalId: "019f1fb5-4299-7062-a4fb-5f6cc0fcb150",
  startAt: null,
  dueAt: "2026-08-01T17:00:00.000Z",
  reminderAt: null,
  completedAt: null,
  estimatedMinutes: 45,
  sortOrder: 1000,
  concurrencyToken: "019f1fb5-4299-7062-a4fb-5f6cc0fcb123",
  createdAt: "2026-07-19T10:00:00.000Z",
  updatedAt: "2026-07-19T10:00:00.000Z",
};

const goal = {
  id: "019f1fb5-4299-7062-a4fb-5f6cc0fcb150",
  title: "Complete the data engineering path",
  description: "Build practical skill through milestones.",
  lifeArea: "learning",
  roleId: null,
  targetAt: "2026-12-01T00:00:00.000Z",
  state: "active",
  sortOrder: 1000,
  roadmapSummary: null,
  totalMilestoneCount: 2,
  completedMilestoneCount: 1,
  totalTaskCount: 3,
  completedTaskCount: 1,
  progressPercent: 40,
  concurrencyToken: "019f1fb5-4299-7062-a4fb-5f6cc0fcb151",
  createdAt: "2026-07-21T10:00:00.000Z",
  updatedAt: "2026-07-21T10:00:00.000Z",
};

const lifeRole = {
  id: "019f1fb5-4299-7062-a4fb-5f6cc0fcb130",
  name: "Physical",
  category: "internal",
  defaultLifeArea: "physical",
  color: "#dc2626",
  icon: "fitness_center",
  sortOrder: 2000,
  isArchived: false,
  isSystemRole: true,
  concurrencyToken: "019f1fb5-4299-7062-a4fb-5f6cc0fcb131",
  createdAt: "2026-07-21T10:00:00.000Z",
  updatedAt: "2026-07-21T10:00:00.000Z",
};

const proposal = {
  id: "019f1fb5-4299-7062-a4fb-5f6cc0fcb132",
  kind: "createTask",
  state: "pending",
  source: "balance",
  title: "Take a brief movement break",
  description: "Choose a short walk that fits your day.",
  payload: { title: "Take a brief movement break" },
  appliedEntityId: null,
  concurrencyToken: "019f1fb5-4299-7062-a4fb-5f6cc0fcb133",
  createdAt: "2026-07-21T10:00:00.000Z",
  resolvedAt: null,
};

const journalEntry = {
  id: "019f1fb5-4299-7062-a4fb-5f6cc0fcb140",
  content: "A small reflection.",
  mood: "Calm",
  insight: {
    id: "019f1fb5-4299-7062-a4fb-5f6cc0fcb141",
    state: "approved",
    emotions: ["calm"],
    feedback: "You noticed a calm moment.",
    progressSummary: "Keep noticing what supports it.",
    sourceConcurrencyToken: "019f1fb5-4299-7062-a4fb-5f6cc0fcb142",
    createdAt: "2026-07-21T10:00:00.000Z",
    resolvedAt: "2026-07-21T10:01:00.000Z",
  },
  concurrencyToken: "019f1fb5-4299-7062-a4fb-5f6cc0fcb142",
  createdAt: "2026-07-21T10:00:00.000Z",
  updatedAt: "2026-07-21T10:01:00.000Z",
};

const journalAnalysis = {
  proposal: {
    ...proposal,
    kind: "applyJournalInsight",
    payload: {
      insightId: journalEntry.insight.id,
      journalEntryId: journalEntry.id,
      sourceConcurrencyToken: journalEntry.concurrencyToken,
    },
  },
  insight: { ...journalEntry.insight, state: "pending", resolvedAt: null },
};

const roleDiscoveryRun = {
  evidenceCount: 3,
  candidates: [{
    proposal: {
      ...proposal,
      kind: "createLifeRole",
      source: "role-discovery",
      payload: { name: "Manager", category: "external", defaultLifeArea: "work" },
    },
    evidence: ["Task: Prepare the team notes", "Project: Engineering review"],
  }],
};
const roadmapProposal = {
  proposal: {
    ...proposal,
    kind: "applyGoalRoadmap",
    source: "graphrag-roadmap",
    payload: { goalId: goal.id, sourceConcurrencyToken: goal.concurrencyToken },
  },
  goalId: goal.id,
  roadmap: {
    summary: "Build from foundations to applied practice.",
    totalEstimatedHours: 24,
    steps: [{ sortOrder: 1000, title: "Learn foundations", description: "Establish the basics.", estimatedHours: 8 }],
  },
};


const scheduleProposal = {
  ...proposal,
  kind: "applyTaskSchedule",
  source: "scheduler",
  title: "Schedule: Draft the announcement",
  payload: {
    taskId: task.id,
    sourceConcurrencyToken: task.concurrencyToken,
    startAt: "2026-08-01T09:00:00.000Z",
    endAt: "2026-08-01T09:45:00.000Z",
    durationMinutes: 45,
    targetDate: "2026-08-01",
  },
};
describe("API DTO mappers", () => {
  it("normalizes valid project and task contracts", () => {
    expect(mapProjectDto(project)).toMatchObject({ name: "Launch", progressPercent: 25, state: "active" });
    expect(mapTaskDto(task)).toMatchObject({ projectId: project.id, state: "inProgress", estimatedMinutes: 45 });
  });

  it("normalizes a goal, goal link, and GraphRAG roadmap proposal", () => {
    expect(mapGoalDto(goal)).toMatchObject({ title: "Complete the data engineering path", progressPercent: 40 });
    expect(mapTaskDto(task)).toMatchObject({ goalId: goal.id });
    expect(mapGoalRoadmapProposalDto(roadmapProposal)).toMatchObject({ goalId: goal.id, proposal: { kind: "applyGoalRoadmap" } });
  });

  it("normalizes life roles and pending AI proposals", () => {
    expect(mapLifeRoleDto(lifeRole)).toMatchObject({ category: "internal", isSystemRole: true });
    expect(mapAiProposalDto(proposal)).toMatchObject({ kind: "createTask", state: "pending" });
  });


  it("normalizes an approval-first schedule proposal", () => {
    expect(mapAiProposalDto(scheduleProposal)).toMatchObject({
      kind: "applyTaskSchedule",
      source: "scheduler",
      payload: { taskId: task.id, durationMinutes: 45 },
    });
  });
  it("normalizes approved insights and pending journal analysis", () => {
    expect(mapJournalEntryDto(journalEntry)).toMatchObject({ mood: "Calm", insight: { state: "approved" } });
    expect(mapJournalAnalysisDto(journalAnalysis)).toMatchObject({ journalEntryId: journalEntry.id, proposal: { kind: "applyJournalInsight" } });
  });

  it("normalizes role-discovery evidence separately from the proposal payload", () => {
    expect(mapRoleDiscoveryRunDto(roleDiscoveryRun)).toMatchObject({ evidenceCount: 3, candidates: [{ proposal: { source: "role-discovery" } }] });
  });

  it("rejects an unsupported persisted task state", () => {
    expect(() => mapTaskDto({ ...task, state: "waiting" })).toThrow("Task state has an unsupported value.");
  });
});
