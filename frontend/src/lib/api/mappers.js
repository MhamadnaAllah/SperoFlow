const PROJECT_STATES = new Set(["active", "completed", "archived"]);
const GOAL_STATES = new Set(["active", "completed", "archived"]);
const MILESTONE_STATES = new Set(["pending", "completed", "archived"]);
const TASK_STATES = new Set(["todo", "inProgress", "completed", "cancelled"]);
const ROLE_CATEGORIES = new Set(["internal", "external"]);
const PROPOSAL_KINDS = new Set(["createTask", "createLifeRole", "applyJournalInsight", "applyTaskClassification", "applyGoalRoadmap", "applyTaskSchedule"]);
const PROPOSAL_STATES = new Set(["pending", "approved", "cancelled"]);
const JOURNAL_INSIGHT_STATES = new Set(["pending", "approved", "cancelled"]);
const LIFE_AREAS = new Set(["work", "family", "physical", "spiritual", "social", "learning", "personal"]);

function objectValue(value, label) {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new TypeError(`${label} response is invalid.`);
  }
  return value;
}

function text(value, label) {
  if (typeof value !== "string" || value.length === 0) {
    throw new TypeError(`${label} is required in the API response.`);
  }
  return value;
}

function optionalText(value, label) {
  if (value === null || value === undefined) return null;
  return text(value, label);
}

function enumValue(value, values, label) {
  const normalized = text(value, label);
  if (!values.has(normalized)) {
    throw new TypeError(`${label} has an unsupported value.`);
  }
  return normalized;
}

function integer(value, label) {
  if (!Number.isInteger(value)) {
    throw new TypeError(label + " must be an integer.");
  }
  return value;
}

function optionalNumber(value, label) {
  if (value === null || value === undefined) return null;
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new TypeError(label + " must be a finite number.");
  }
  return value;
}
function boolean(value, label) {
  if (typeof value !== "boolean") {
    throw new TypeError(`${label} must be a boolean.`);
  }
  return value;
}

function optionalIsoDate(value, label) {
  if (value === null || value === undefined) return null;
  return text(value, label);
}

function listValue(value, label) {
  if (!Array.isArray(value)) {
    throw new TypeError(`${label} response must be an array.`);
  }
  return value;
}

/**
 * Normalizes the ASP.NET ProjectResponse contract before UI code consumes it.
 * @param {unknown} value
 */
export function mapProjectDto(value) {
  const dto = objectValue(value, "Project");
  return {
    id: text(dto.id, "Project id"),
    name: text(dto.name, "Project name"),
    description: optionalText(dto.description, "Project description"),
    color: text(dto.color, "Project color"),
    icon: text(dto.icon, "Project icon"),
    startAt: optionalIsoDate(dto.startAt, "Project startAt"),
    targetAt: optionalIsoDate(dto.targetAt, "Project targetAt"),
    state: enumValue(dto.state, PROJECT_STATES, "Project state"),
    sortOrder: integer(dto.sortOrder, "Project sortOrder"),
    totalTaskCount: integer(dto.totalTaskCount, "Project totalTaskCount"),
    completedTaskCount: integer(dto.completedTaskCount, "Project completedTaskCount"),
    progressPercent: integer(dto.progressPercent, "Project progressPercent"),
    concurrencyToken: text(dto.concurrencyToken, "Project concurrencyToken"),
    createdAt: text(dto.createdAt, "Project createdAt"),
    updatedAt: text(dto.updatedAt, "Project updatedAt"),
  };
}

/** @param {unknown} value */
export function mapProjectListDto(value) {
  return listValue(value, "Projects").map(mapProjectDto);
}

/** @param {unknown} value */
export function mapGoalDto(value) {
  const dto = objectValue(value, "Goal");
  return {
    id: text(dto.id, "Goal id"),
    title: text(dto.title, "Goal title"),
    description: optionalText(dto.description, "Goal description"),
    lifeArea: enumValue(dto.lifeArea, LIFE_AREAS, "Goal lifeArea"),
    roleId: optionalText(dto.roleId, "Goal roleId"),
    targetAt: optionalIsoDate(dto.targetAt, "Goal targetAt"),
    state: enumValue(dto.state, GOAL_STATES, "Goal state"),
    sortOrder: integer(dto.sortOrder, "Goal sortOrder"),
    roadmapSummary: optionalText(dto.roadmapSummary, "Goal roadmapSummary"),
    totalMilestoneCount: integer(dto.totalMilestoneCount, "Goal totalMilestoneCount"),
    completedMilestoneCount: integer(dto.completedMilestoneCount, "Goal completedMilestoneCount"),
    totalTaskCount: integer(dto.totalTaskCount, "Goal totalTaskCount"),
    completedTaskCount: integer(dto.completedTaskCount, "Goal completedTaskCount"),
    progressPercent: integer(dto.progressPercent, "Goal progressPercent"),
    concurrencyToken: text(dto.concurrencyToken, "Goal concurrencyToken"),
    createdAt: text(dto.createdAt, "Goal createdAt"),
    updatedAt: text(dto.updatedAt, "Goal updatedAt"),
  };
}

/** @param {unknown} value */
export function mapGoalListDto(value) {
  return listValue(value, "Goals").map(mapGoalDto);
}

/** @param {unknown} value */
export function mapGoalMilestoneDto(value) {
  const dto = objectValue(value, "Goal milestone");
  return {
    id: text(dto.id, "Goal milestone id"),
    goalId: text(dto.goalId, "Goal milestone goalId"),
    title: text(dto.title, "Goal milestone title"),
    description: optionalText(dto.description, "Goal milestone description"),
    estimatedHours: optionalNumber(dto.estimatedHours, "Goal milestone estimatedHours"),
    sortOrder: integer(dto.sortOrder, "Goal milestone sortOrder"),
    state: enumValue(dto.state, MILESTONE_STATES, "Goal milestone state"),
    completedAt: optionalIsoDate(dto.completedAt, "Goal milestone completedAt"),
    concurrencyToken: text(dto.concurrencyToken, "Goal milestone concurrencyToken"),
    createdAt: text(dto.createdAt, "Goal milestone createdAt"),
    updatedAt: text(dto.updatedAt, "Goal milestone updatedAt"),
  };
}

/** @param {unknown} value */
export function mapGoalMilestoneListDto(value) {
  return listValue(value, "Goal milestones").map(mapGoalMilestoneDto);
}

/** @param {unknown} value */
export function mapGoalRoadmapProposalDto(value) {
  const dto = objectValue(value, "Goal roadmap proposal");
  const proposal = mapAiProposalDto(dto.proposal);
  if (proposal.kind !== "applyGoalRoadmap") {
    throw new TypeError("Goal roadmap proposal has an unsupported proposal kind.");
  }
  const roadmap = objectValue(dto.roadmap, "Goal roadmap");
  return {
    proposal,
    goalId: text(dto.goalId, "Goal roadmap goalId"),
    roadmap: {
      summary: text(roadmap.summary, "Goal roadmap summary"),
      totalEstimatedHours: optionalNumber(roadmap.totalEstimatedHours, "Goal roadmap totalEstimatedHours"),
      steps: listValue(roadmap.steps, "Goal roadmap steps").map((step, index) => {
        const item = objectValue(step, "Goal roadmap step " + index);
        const label = "Goal roadmap step " + index;
        return {
          sortOrder: integer(item.sortOrder, label + " sortOrder"),
          title: text(item.title, label + " title"),
          description: optionalText(item.description, label + " description"),
          estimatedHours: optionalNumber(item.estimatedHours, label + " estimatedHours"),
        };
      }),
    },
  };
}

/** @param {unknown} value */
export function mapGoalRoadmapProposalListDto(value) {
  return listValue(value, "Goal roadmap proposals").map(mapGoalRoadmapProposalDto);
}

/** @param {unknown} value */
export function mapLifeRoleDto(value) {
  const dto = objectValue(value, "Life role");
  return {
    id: text(dto.id, "Life role id"),
    name: text(dto.name, "Life role name"),
    category: enumValue(dto.category, ROLE_CATEGORIES, "Life role category"),
    defaultLifeArea: enumValue(dto.defaultLifeArea, LIFE_AREAS, "Life role defaultLifeArea"),
    color: text(dto.color, "Life role color"),
    icon: text(dto.icon, "Life role icon"),
    sortOrder: integer(dto.sortOrder, "Life role sortOrder"),
    isArchived: boolean(dto.isArchived, "Life role isArchived"),
    isSystemRole: boolean(dto.isSystemRole, "Life role isSystemRole"),
    concurrencyToken: text(dto.concurrencyToken, "Life role concurrencyToken"),
    createdAt: text(dto.createdAt, "Life role createdAt"),
    updatedAt: text(dto.updatedAt, "Life role updatedAt"),
  };
}

/** @param {unknown} value */
export function mapLifeRoleListDto(value) {
  return listValue(value, "Life roles").map(mapLifeRoleDto);
}

/** @param {unknown} value */
export function mapAiProposalDto(value) {
  const dto = objectValue(value, "AI proposal");
  return {
    id: text(dto.id, "AI proposal id"),
    kind: enumValue(dto.kind, PROPOSAL_KINDS, "AI proposal kind"),
    state: enumValue(dto.state, PROPOSAL_STATES, "AI proposal state"),
    source: text(dto.source, "AI proposal source"),
    title: text(dto.title, "AI proposal title"),
    description: text(dto.description, "AI proposal description"),
    payload: objectValue(dto.payload, "AI proposal payload"),
    appliedEntityId: optionalText(dto.appliedEntityId, "AI proposal appliedEntityId"),
    concurrencyToken: text(dto.concurrencyToken, "AI proposal concurrencyToken"),
    createdAt: text(dto.createdAt, "AI proposal createdAt"),
    resolvedAt: optionalIsoDate(dto.resolvedAt, "AI proposal resolvedAt"),
  };
}

/** @param {unknown} value */
export function mapAiProposalListDto(value) {
  return listValue(value, "AI proposals").map(mapAiProposalDto);
}

/** @param {unknown} value */
export function mapRoleDiscoveryCandidateDto(value) {
  const dto = objectValue(value, "Role discovery candidate");
  const proposal = mapAiProposalDto(dto.proposal);
  if (proposal.kind !== "createLifeRole" || proposal.source !== "role-discovery") {
    throw new TypeError("Role discovery candidate has an unsupported proposal.");
  }
  return {
    proposal,
    evidence: listValue(dto.evidence, "Role discovery evidence").map((item, index) => text(item, `Role discovery evidence ${index}`)),
  };
}

/** @param {unknown} value */
export function mapRoleDiscoveryCandidateListDto(value) {
  return listValue(value, "Role discovery candidates").map(mapRoleDiscoveryCandidateDto);
}

/** @param {unknown} value */
export function mapRoleDiscoveryRunDto(value) {
  const dto = objectValue(value, "Role discovery run");
  return {
    evidenceCount: integer(dto.evidenceCount, "Role discovery evidenceCount"),
    candidates: mapRoleDiscoveryCandidateListDto(dto.candidates),
  };
}

/** @param {unknown} value */
export function mapJournalInsightDto(value) {
  const dto = objectValue(value, "Journal insight");
  return {
    id: text(dto.id, "Journal insight id"),
    state: enumValue(dto.state, JOURNAL_INSIGHT_STATES, "Journal insight state"),
    emotions: listValue(dto.emotions, "Journal insight emotions").map((emotion, index) => text(emotion, `Journal insight emotion ${index}`)),
    feedback: text(dto.feedback, "Journal insight feedback"),
    progressSummary: text(dto.progressSummary, "Journal insight progressSummary"),
    sourceConcurrencyToken: text(dto.sourceConcurrencyToken, "Journal insight sourceConcurrencyToken"),
    createdAt: text(dto.createdAt, "Journal insight createdAt"),
    resolvedAt: optionalIsoDate(dto.resolvedAt, "Journal insight resolvedAt"),
  };
}

/** @param {unknown} value */
export function mapJournalEntryDto(value) {
  const dto = objectValue(value, "Journal entry");
  return {
    id: text(dto.id, "Journal entry id"),
    content: text(dto.content, "Journal entry content"),
    mood: optionalText(dto.mood, "Journal entry mood"),
    insight: dto.insight === null || dto.insight === undefined ? null : mapJournalInsightDto(dto.insight),
    concurrencyToken: text(dto.concurrencyToken, "Journal entry concurrencyToken"),
    createdAt: text(dto.createdAt, "Journal entry createdAt"),
    updatedAt: text(dto.updatedAt, "Journal entry updatedAt"),
  };
}

/** @param {unknown} value */
export function mapJournalEntryListDto(value) {
  return listValue(value, "Journal entries").map(mapJournalEntryDto);
}

/** @param {unknown} value */
export function mapJournalAnalysisDto(value) {
  const dto = objectValue(value, "Journal analysis");
  const proposal = mapAiProposalDto(dto.proposal);
  if (proposal.kind !== "applyJournalInsight") {
    throw new TypeError("Journal analysis has an unsupported proposal kind.");
  }
  const insight = mapJournalInsightDto(dto.insight);
  const journalEntryId = text(proposal.payload.journalEntryId, "Journal analysis journalEntryId");
  if (text(proposal.payload.insightId, "Journal analysis insightId") !== insight.id) {
    throw new TypeError("Journal analysis insight does not match its proposal.");
  }
  return { proposal, insight, journalEntryId };
}

/**
 * Normalizes the ASP.NET TaskResponse contract before UI code consumes it.
 * @param {unknown} value
 */
export function mapTaskDto(value) {
  const dto = objectValue(value, "Task");
  return {
    id: text(dto.id, "Task id"),
    title: text(dto.title, "Task title"),
    description: optionalText(dto.description, "Task description"),
    lifeArea: enumValue(dto.lifeArea, LIFE_AREAS, "Task lifeArea"),
    quadrant: text(dto.quadrant, "Task quadrant"),
    state: enumValue(dto.state, TASK_STATES, "Task state"),
    projectId: optionalText(dto.projectId, "Task projectId"),
    roleId: optionalText(dto.roleId, "Task roleId"),
    goalId: optionalText(dto.goalId, "Task goalId"),
    startAt: optionalIsoDate(dto.startAt, "Task startAt"),
    dueAt: optionalIsoDate(dto.dueAt, "Task dueAt"),
    reminderAt: optionalIsoDate(dto.reminderAt, "Task reminderAt"),
    completedAt: optionalIsoDate(dto.completedAt, "Task completedAt"),
    estimatedMinutes: dto.estimatedMinutes === null || dto.estimatedMinutes === undefined ? null : integer(dto.estimatedMinutes, "Task estimatedMinutes"),
    sortOrder: integer(dto.sortOrder, "Task sortOrder"),
    concurrencyToken: text(dto.concurrencyToken, "Task concurrencyToken"),
    createdAt: text(dto.createdAt, "Task createdAt"),
    updatedAt: text(dto.updatedAt, "Task updatedAt"),
  };
}

/** @param {unknown} value */
export function mapTaskListDto(value) {
  return listValue(value, "Tasks").map(mapTaskDto);
}
