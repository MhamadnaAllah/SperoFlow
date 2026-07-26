import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const nativeFetch = globalThis.fetch;

function jsonResponse(body, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    headers: new Headers({ "content-type": "application/json" }),
    json: async () => body,
  };
}

function taskResponse() {
  return {
    id: "019f1fb5-4299-7062-a4fb-5f6cc0fcb122",
    title: "Draft the announcement",
    description: null,
    lifeArea: "work",
    quadrant: "q2",
    state: "todo",
    projectId: null,
    startAt: null,
    dueAt: null,
    reminderAt: null,
    completedAt: null,
    estimatedMinutes: null,
    sortOrder: 1000,
    concurrencyToken: "019f1fb5-4299-7062-a4fb-5f6cc0fcb123",
    createdAt: "2026-07-19T10:00:00.000Z",
    updatedAt: "2026-07-19T10:00:00.000Z",
  };
}

let api;

beforeEach(async () => {
  vi.resetModules();
  globalThis.fetch = vi.fn();
  api = await import("./client");
});

afterEach(() => {
  globalThis.fetch = nativeFetch;
  vi.restoreAllMocks();
});

describe("same-origin API client", () => {
  it("retrieves a CSRF token before a task mutation and sends cookies", async () => {
    globalThis.fetch
      .mockResolvedValueOnce(jsonResponse({ token: "csrf-token" }))
      .mockResolvedValueOnce(jsonResponse(taskResponse(), 201));

    await expect(api.tasksApi.create({ title: "Draft the announcement" })).resolves.toMatchObject({ title: "Draft the announcement" });

    expect(globalThis.fetch).toHaveBeenNthCalledWith(1, "/api/v1/auth/csrf", expect.objectContaining({ credentials: "same-origin" }));
    const [, request] = globalThis.fetch.mock.calls[1];
    expect(globalThis.fetch.mock.calls[1][0]).toBe("/api/v1/tasks");
    expect(request.credentials).toBe("same-origin");
    expect(request.headers.get("X-CSRF-TOKEN")).toBe("csrf-token");
    expect(JSON.parse(request.body)).toEqual({ title: "Draft the announcement" });
  });

  it("retrieves a CSRF token before initializing core life roles", async () => {
    globalThis.fetch
      .mockResolvedValueOnce(jsonResponse({ token: "csrf-token" }))
      .mockResolvedValueOnce(jsonResponse([]));

    await expect(api.rolesApi.bootstrap()).resolves.toEqual([]);

    expect(globalThis.fetch).toHaveBeenNthCalledWith(1, "/api/v1/auth/csrf", expect.objectContaining({ credentials: "same-origin" }));
    expect(globalThis.fetch.mock.calls[1][0]).toBe("/api/v1/roles/bootstrap");
    expect(globalThis.fetch.mock.calls[1][1].headers.get("X-CSRF-TOKEN")).toBe("csrf-token");
  });

  it("normalizes problem details into ApiError", async () => {
    globalThis.fetch.mockResolvedValueOnce(jsonResponse({ title: "Conflict", detail: "Task changed elsewhere." }, 409));

    await expect(api.apiRequest("/tasks")).rejects.toMatchObject({ name: "ApiError", status: 409, message: "Task changed elsewhere." });
  });

  it("stops polling when an ingestion job reaches a terminal state", async () => {
    const job = { id: "job-1", state: "succeeded" };
    globalThis.fetch.mockResolvedValueOnce(jsonResponse(job));

    await expect(api.documentsApi.pollJob("job-1")).resolves.toEqual(job);
    expect(globalThis.fetch).toHaveBeenCalledWith("/api/v1/jobs/job-1", expect.objectContaining({ credentials: "same-origin" }));
  });
  it("requests a journal analysis through the same-origin API with CSRF", async () => {
    const analysis = {
      proposal: {
        id: "proposal-1",
        kind: "applyJournalInsight",
        state: "pending",
        source: "journal",
        title: "Review your journal reflection",
        description: "A reflection is ready for your review.",
        payload: { insightId: "insight-1", journalEntryId: "entry-1", sourceConcurrencyToken: "token-1" },
        appliedEntityId: null,
        concurrencyToken: "proposal-token",
        createdAt: "2026-07-21T10:00:00.000Z",
        resolvedAt: null,
      },
      insight: {
        id: "insight-1",
        state: "pending",
        emotions: ["calm"],
        feedback: "You noticed a calm moment.",
        progressSummary: "Keep noticing what supports it.",
        sourceConcurrencyToken: "token-1",
        createdAt: "2026-07-21T10:00:00.000Z",
        resolvedAt: null,
      },
    };
    globalThis.fetch
      .mockResolvedValueOnce(jsonResponse({ token: "csrf-token" }))
      .mockResolvedValueOnce(jsonResponse(analysis));

    await expect(api.journalApi.analyze("entry-1")).resolves.toMatchObject({ journalEntryId: "entry-1" });

    expect(globalThis.fetch.mock.calls[1][0]).toBe("/api/v1/ai/journal/entry-1/analyze");
    expect(globalThis.fetch.mock.calls[1][1].headers.get("X-CSRF-TOKEN")).toBe("csrf-token");
  });
  it("requests role discovery through the same-origin API with CSRF", async () => {
    const result = {
      evidenceCount: 2,
      candidates: [{
        proposal: {
          id: "proposal-role-1",
          kind: "createLifeRole",
          state: "pending",
          source: "role-discovery",
          title: "Add role: Manager",
          description: "Review the evidence.",
          payload: { name: "Manager", category: "external", defaultLifeArea: "work" },
          appliedEntityId: null,
          concurrencyToken: "proposal-role-token",
          createdAt: "2026-07-21T10:00:00.000Z",
          resolvedAt: null,
        },
        evidence: ["Task: Team notes", "Project: Team review"],
      }],
    };
    globalThis.fetch
      .mockResolvedValueOnce(jsonResponse({ token: "csrf-token" }))
      .mockResolvedValueOnce(jsonResponse(result));

    await expect(api.rolesApi.discover()).resolves.toMatchObject({ evidenceCount: 2, candidates: [{ proposal: { source: "role-discovery" } }] });

    expect(globalThis.fetch.mock.calls[1][0]).toBe("/api/v1/ai/roles/discover");
    expect(globalThis.fetch.mock.calls[1][1].headers.get("X-CSRF-TOKEN")).toBe("csrf-token");
  });

  it("requests an approval-first schedule suggestion through the same-origin API", async () => {
    const proposal = {
      id: "proposal-schedule-1",
      kind: "applyTaskSchedule",
      state: "pending",
      source: "scheduler",
      title: "Schedule: Draft the announcement",
      description: "A conflict-free focus block is ready for review.",
      payload: {
        taskId: taskResponse().id,
        sourceConcurrencyToken: taskResponse().concurrencyToken,
        startAt: "2026-08-01T09:00:00.000Z",
        endAt: "2026-08-01T09:30:00.000Z",
        durationMinutes: 30,
        targetDate: "2026-08-01",
      },
      appliedEntityId: null,
      concurrencyToken: "proposal-schedule-token",
      createdAt: "2026-07-21T10:00:00.000Z",
      resolvedAt: null,
    };
    globalThis.fetch
      .mockResolvedValueOnce(jsonResponse({ token: "csrf-token" }))
      .mockResolvedValueOnce(jsonResponse(proposal));

    await expect(api.aiApi.proposeTaskSchedule(taskResponse().id, { targetDate: "2026-08-01", durationMinutes: 30 }))
      .resolves.toMatchObject({ kind: "applyTaskSchedule", payload: { durationMinutes: 30 } });

    expect(globalThis.fetch.mock.calls[1][0]).toBe(`/api/v1/ai/tasks/${taskResponse().id}/schedule`);
    expect(globalThis.fetch.mock.calls[1][1].headers.get("X-CSRF-TOKEN")).toBe("csrf-token");
    expect(JSON.parse(globalThis.fetch.mock.calls[1][1].body)).toEqual({ targetDate: "2026-08-01", durationMinutes: 30 });
  });
});
