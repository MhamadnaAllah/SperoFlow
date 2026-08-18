import { mapAiProposalDto, mapAiProposalListDto, mapGoalDto, mapGoalListDto, mapGoalMilestoneDto, mapGoalMilestoneListDto, mapGoalRoadmapProposalDto, mapGoalRoadmapProposalListDto, mapJournalAnalysisDto, mapJournalEntryDto, mapJournalEntryListDto, mapLifeRoleDto, mapLifeRoleListDto, mapProjectDto, mapProjectListDto, mapRoleDiscoveryCandidateListDto, mapRoleDiscoveryRunDto, mapTaskDto, mapTaskListDto } from "./mappers";

const API_ROOT = "/api/v1";

export class ApiError extends Error {
  constructor(message, status, details = null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.details = details;
  }
}

let csrfToken = null;
let csrfRequest = null;

function apiPath(path) {
  return path.startsWith("/api/") ? path : `${API_ROOT}${path.startsWith("/") ? path : `/${path}`}`;
}

function isUnsafe(method) {
  return !["GET", "HEAD", "OPTIONS"].includes(method.toUpperCase());
}

async function parseResponse(response) {
  const contentType = response.headers.get("content-type") || "";
  if (!contentType.includes("application/json")) return null;
  return response.json().catch(() => null);
}

async function getCsrfToken() {
  if (csrfToken) return csrfToken;
  if (!csrfRequest) {
    csrfRequest = fetch(apiPath("/auth/csrf"), {
      credentials: "same-origin",
      cache: "no-store",
    })
      .then(async (response) => {
        const payload = await parseResponse(response);
        if (!response.ok || !payload?.token) {
          throw new ApiError("Unable to prepare a secure request.", response.status, payload);
        }
        csrfToken = payload.token;
        return csrfToken;
      })
      .finally(() => {
        csrfRequest = null;
      });
  }
  return csrfRequest;
}

function emitAuthChange() {
  if (typeof window !== "undefined") {
    window.dispatchEvent(new Event("speroflow-auth-change"));
  }
}

function formatErrorMessage(payload) {
  if (!payload) return "The request could not be completed.";

  const errObj = payload.errors || payload.Errors;
  if (errObj && typeof errObj === "object") {
    const messages = Object.values(errObj)
      .flat()
      .filter((msg) => typeof msg === "string" && msg.trim().length > 0);
    if (messages.length > 0) {
      return messages.join(" ");
    }
  }

  if (payload.detail && typeof payload.detail === "string" && payload.detail.trim().length > 0) {
    return payload.detail;
  }

  if (payload.error && typeof payload.error === "string" && payload.error.trim().length > 0) {
    return payload.error;
  }

  if (payload.message && typeof payload.message === "string" && payload.message.trim().length > 0) {
    return payload.message;
  }

  if (payload.title && typeof payload.title === "string" && payload.title.trim().length > 0 && payload.title !== "One or more validation errors occurred.") {
    return payload.title;
  }

  if (typeof payload === "string" && payload.trim().length > 0) {
    return payload;
  }

  return "The request could not be completed.";
}

export async function apiRequest(path, options = {}) {
  const method = (options.method || "GET").toUpperCase();
  const headers = new Headers(options.headers || {});
  const unsafe = isUnsafe(method);

  if (options.body !== undefined && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  if (unsafe) {
    headers.set("X-CSRF-TOKEN", await getCsrfToken());
  }

  const response = await fetch(apiPath(path), {
    ...options,
    method,
    headers,
    body: options.body === undefined || typeof options.body === "string" ? options.body : JSON.stringify(options.body),
    credentials: "same-origin",
    cache: "no-store",
  });
  const payload = await parseResponse(response);

  if (!response.ok) {
    if (unsafe && response.status === 400 && payload?.title === "Invalid CSRF token.") {
      csrfToken = null;
    }
    const detail = formatErrorMessage(payload);
    throw new ApiError(detail, response.status, payload);
  }

  return response.status === 204 ? null : payload;
}

function queryString(values = {}) {
  const params = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== "") {
      params.set(key, String(value));
    }
  });
  const query = params.toString();
  return query ? `?${query}` : "";
}

export const authApi = {
  async me() {
    return apiRequest("/auth/me");
  },
  async login(input) {
    csrfToken = null;
    const result = await apiRequest("/auth/login", { method: "POST", body: input });
    csrfToken = null;
    emitAuthChange();
    return result;
  },
  async register(input) {
    csrfToken = null;
    const result = await apiRequest("/auth/register", { method: "POST", body: input });
    csrfToken = null;
    return result;
  },
  async logout() {
    const result = await apiRequest("/auth/logout", { method: "POST" });
    csrfToken = null;
    emitAuthChange();
    return result;
  },
};

export const projectsApi = {
  async list(filters = {}) {
    const params = { includeArchived: false, ...filters };
    return mapProjectListDto(await apiRequest(`/projects${queryString(params)}`));
  },
  async get(id) {
    return mapProjectDto(await apiRequest(`/projects/${id}`));
  },
  async create(input) {
    return mapProjectDto(await apiRequest("/projects", { method: "POST", body: input }));
  },
  async update(id, input) {
    return mapProjectDto(await apiRequest(`/projects/${id}`, { method: "PUT", body: input }));
  },
  async archive(id, concurrencyToken) {
    return mapProjectDto(await apiRequest(`/projects/${id}/archive`, { method: "POST", body: { concurrencyToken } }));
  },
  async restore(id, concurrencyToken) {
    return mapProjectDto(await apiRequest(`/projects/${id}/restore`, { method: "POST", body: { concurrencyToken } }));
  },
  async reorderTask(id, input) {
    return mapTaskDto(await apiRequest(`/projects/${id}/tasks/reorder`, { method: "POST", body: input }));
  },
};

export const goalsApi = {
  list(filters = {}) {
    const params = { includeArchived: false, ...filters };
    return apiRequest("/goals" + queryString(params)).then(mapGoalListDto);
  },
  get(id) {
    return apiRequest("/goals/" + id).then(mapGoalDto);
  },
  create(input) {
    return apiRequest("/goals", { method: "POST", body: input }).then(mapGoalDto);
  },
  update(id, input) {
    return apiRequest("/goals/" + id, { method: "PUT", body: input }).then(mapGoalDto);
  },
  listMilestones(id, filters = {}) {
    const params = { includeArchived: false, ...filters };
    return apiRequest("/goals/" + id + "/milestones" + queryString(params)).then(mapGoalMilestoneListDto);
  },
  createMilestone(id, input) {
    return apiRequest("/goals/" + id + "/milestones", { method: "POST", body: input }).then(mapGoalMilestoneDto);
  },
  updateMilestone(id, milestoneId, input) {
    return apiRequest("/goals/" + id + "/milestones/" + milestoneId, { method: "PUT", body: input }).then(mapGoalMilestoneDto);
  },
  proposeRoadmap(id) {
    return apiRequest("/goals/" + id + "/roadmap/propose", { method: "POST" }).then(mapGoalRoadmapProposalDto);
  },
  listPendingRoadmaps() {
    return apiRequest("/ai/goals/roadmaps/pending").then(mapGoalRoadmapProposalListDto);
  },
};

export const rolesApi = {
  list(filters = {}) {
    const params = { includeArchived: false, ...filters };
    return apiRequest(`/roles${queryString(params)}`).then(mapLifeRoleListDto);
  },
  bootstrap() {
    return apiRequest("/roles/bootstrap", { method: "POST" }).then(mapLifeRoleListDto);
  },
  create(input) {
    return apiRequest("/roles", { method: "POST", body: input }).then(mapLifeRoleDto);
  },
  update(id, input) {
    return apiRequest(`/roles/${id}`, { method: "PUT", body: input }).then(mapLifeRoleDto);
  },
  archive(id, concurrencyToken) {
    return apiRequest(`/roles/${id}/archive`, { method: "POST", body: { concurrencyToken } }).then(mapLifeRoleDto);
  },
  restore(id, concurrencyToken) {
    return apiRequest(`/roles/${id}/restore`, { method: "POST", body: { concurrencyToken } }).then(mapLifeRoleDto);
  },
  listDiscoveryCandidates() {
    return apiRequest("/ai/roles/pending").then(mapRoleDiscoveryCandidateListDto);
  },
  discover() {
    return apiRequest("/ai/roles/discover", { method: "POST" }).then(mapRoleDiscoveryRunDto);
  },
};

export const aiProposalsApi = {
  list(filters = {}) {
    return apiRequest(`/ai/proposals${queryString(filters)}`).then(mapAiProposalListDto);
  },
  approve(id, concurrencyToken) {
    return apiRequest(`/ai/proposals/${id}/approve`, { method: "POST", body: { concurrencyToken } }).then(mapAiProposalDto);
  },
  cancel(id, concurrencyToken) {
    return apiRequest(`/ai/proposals/${id}/cancel`, { method: "POST", body: { concurrencyToken } }).then(mapAiProposalDto);
  },
};

export const tasksApi = {
  async list(filters = {}) {
    return mapTaskListDto(await apiRequest(`/tasks${queryString(filters)}`));
  },
  async create(input) {
    return mapTaskDto(await apiRequest("/tasks", { method: "POST", body: input }));
  },
  async update(id, input) {
    return mapTaskDto(await apiRequest(`/tasks/${id}`, { method: "PUT", body: input }));
  },
  remove(id, concurrencyToken) {
    return apiRequest(`/tasks/${id}`, { method: "DELETE", body: { concurrencyToken } });
  },
};

export const calendarApi = {
  list(filters = {}) {
    return apiRequest(`/calendar-events${queryString(filters)}`);
  },
  create(input) {
    return apiRequest("/calendar-events", { method: "POST", body: input });
  },
  update(id, input) {
    return apiRequest(`/calendar-events/${id}`, { method: "PUT", body: input });
  },
  remove(id) {
    return apiRequest(`/calendar-events/${id}`, { method: "DELETE" });
  },
};

export const habitsApi = {
  list(filters = {}) {
    const params = { includeArchived: false, ...filters };
    return apiRequest(`/habits${queryString(params)}`);
  },
  create(input) {
    return apiRequest("/habits", { method: "POST", body: input });
  },
  update(id, input) {
    return apiRequest(`/habits/${id}`, { method: "PUT", body: input });
  },
  archive(id, concurrencyToken) {
    return apiRequest(`/habits/${id}`, { method: "DELETE", body: { concurrencyToken } });
  },
  restore(id, concurrencyToken) {
    return apiRequest(`/habits/${id}/restore`, { method: "POST", body: { concurrencyToken } });
  },
  listCheckIns(filters = {}) {
    return apiRequest(`/habits/check-ins${queryString(filters)}`);
  },
  addCheckIn(id, input) {
    return apiRequest(`/habits/${id}/check-ins`, { method: "POST", body: input });
  },
  removeCheckIn(id, occurredOn) {
    return apiRequest(`/habits/${id}/check-ins/${occurredOn}`, { method: "DELETE" });
  },
};

export const journalApi = {
  list() {
    return apiRequest("/journal").then(mapJournalEntryListDto);
  },
  create(input) {
    return apiRequest("/journal", { method: "POST", body: input }).then(mapJournalEntryDto);
  },
  update(id, input) {
    return apiRequest(`/journal/${id}`, { method: "PUT", body: input }).then(mapJournalEntryDto);
  },
  listPendingAnalyses() {
    return apiRequest("/ai/journal/pending").then((value) => value.map(mapJournalAnalysisDto));
  },
  analyze(id) {
    return apiRequest(`/ai/journal/${id}/analyze`, { method: "POST" }).then(mapJournalAnalysisDto);
  },
};

export const documentsApi = {
  list() {
    return apiRequest("/documents");
  },
  submit(input) {
    return apiRequest("/documents", { method: "POST", body: input });
  },
  job(id) {
    return apiRequest(`/jobs/${id}`);
  },
  async pollJob(id, { intervalMs = 1500, timeoutMs = 120000, onUpdate } = {}) {
    const startedAt = Date.now();
    while (Date.now() - startedAt < timeoutMs) {
      const job = await this.job(id);
      onUpdate?.(job);
      if (["succeeded", "failed"].includes(job.state)) return job;
      await new Promise((resolve) => window.setTimeout(resolve, intervalMs));
    }
    throw new ApiError("Document processing is taking longer than expected.", 408);
  },
};

export const aiApi = {
  query(input) {
    return apiRequest("/ai/query", { method: "POST", body: input });
  },
  proposeTaskClassification(id) {
    return apiRequest("/ai/tasks/" + id + "/classify", { method: "POST" }).then(mapAiProposalDto);
  },
  proposeTaskSchedule(id, input) {
    return apiRequest("/ai/tasks/" + id + "/schedule", { method: "POST", body: input }).then(mapAiProposalDto);
  },
  balance() {
    return apiRequest("/ai/balance", { method: "POST" });
  },
};

export const coachApi = {
  listConversations() {
    return apiRequest("/coach/conversations");
  },
  createConversation(title) {
    return apiRequest("/coach/conversations", { method: "POST", body: { title } });
  },
  listMessages(conversationId) {
    return apiRequest(`/coach/conversations/${conversationId}/messages`);
  },
  postMessage(conversationId, content) {
    return apiRequest(`/coach/conversations/${conversationId}/messages`, { method: "POST", body: { content } });
  },
  listObservations() {
    return apiRequest("/coach/observations");
  },
  dismissObservation(id) {
    return apiRequest(`/coach/observations/${id}/dismiss`, { method: "POST" });
  },
};

