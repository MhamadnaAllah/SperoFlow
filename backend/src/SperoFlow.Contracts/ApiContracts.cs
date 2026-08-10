using System.Text.Json;
using SperoFlow.Domain;

namespace SperoFlow.Contracts;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName, string? BootstrapToken = null);

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);

public sealed record ConfirmEmailRequest(Guid UserId, string Token);

public sealed record AuthenticatedUserResponse(Guid Id, string Email, string? DisplayName, bool EmailConfirmed, IReadOnlyCollection<string> Roles);

public sealed record CsrfTokenResponse(string Token);

public sealed record UpdateKnowledgePortalRoleRequest(string Role, bool Enabled);

public sealed record CreateLifeRoleRequest(
    string Name,
    LifeRoleCategory Category,
    LifeArea DefaultLifeArea,
    string Color = "#0053dc",
    string Icon = "person",
    int SortOrder = 0);

public sealed record UpdateLifeRoleRequest(
    string Name,
    LifeRoleCategory Category,
    LifeArea DefaultLifeArea,
    string Color,
    string Icon,
    int SortOrder,
    Guid ConcurrencyToken);

public sealed record LifeRoleResponse(
    Guid Id,
    string Name,
    LifeRoleCategory Category,
    LifeArea DefaultLifeArea,
    string Color,
    string Icon,
    int SortOrder,
    bool IsArchived,
    bool IsSystemRole,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AiProposalResponse(
    Guid Id,
    AiProposalKind Kind,
    AiProposalState State,
    string Source,
    string Title,
    string Description,
    JsonElement Payload,
    Guid? AppliedEntityId,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record RoleDiscoveryCandidateResponse(
    AiProposalResponse Proposal,
    IReadOnlyList<string> Evidence);

public sealed record RoleDiscoveryRunResponse(
    int EvidenceCount,
    IReadOnlyList<RoleDiscoveryCandidateResponse> Candidates);

public sealed record CreateProjectRequest(
    string Name,
    string? Description,
    string Color = "indigo",
    string Icon = "folder",
    DateTimeOffset? StartAt = null,
    DateTimeOffset? TargetAt = null,
    int SortOrder = 0);

public sealed record UpdateProjectRequest(
    string Name,
    string? Description,
    string Color,
    string Icon,
    DateTimeOffset? StartAt,
    DateTimeOffset? TargetAt,
    ProjectState State,
    int SortOrder,
    Guid ConcurrencyToken);

public sealed record ProjectResponse(
    Guid Id,
    string Name,
    string? Description,
    string Color,
    string Icon,
    DateTimeOffset? StartAt,
    DateTimeOffset? TargetAt,
    ProjectState State,
    int SortOrder,
    int TotalTaskCount,
    int CompletedTaskCount,
    int ProgressPercent,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateGoalRequest(
    string Title,
    string? Description,
    LifeArea LifeArea,
    DateTimeOffset? TargetAt = null,
    int SortOrder = 0,
    Guid? RoleId = null);

public sealed record UpdateGoalRequest(
    string Title,
    string? Description,
    LifeArea LifeArea,
    DateTimeOffset? TargetAt,
    GoalState State,
    int SortOrder,
    Guid? RoleId,
    Guid ConcurrencyToken);

public sealed record GoalResponse(
    Guid Id,
    string Title,
    string? Description,
    LifeArea LifeArea,
    Guid? RoleId,
    DateTimeOffset? TargetAt,
    GoalState State,
    int SortOrder,
    string? RoadmapSummary,
    int TotalMilestoneCount,
    int CompletedMilestoneCount,
    int TotalTaskCount,
    int CompletedTaskCount,
    int ProgressPercent,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateGoalMilestoneRequest(
    string Title,
    string? Description,
    decimal? EstimatedHours = null,
    int? SortOrder = null);

public sealed record UpdateGoalMilestoneRequest(
    string Title,
    string? Description,
    decimal? EstimatedHours,
    GoalMilestoneState State,
    int SortOrder,
    Guid ConcurrencyToken);

public sealed record GoalMilestoneResponse(
    Guid Id,
    Guid GoalId,
    string Title,
    string? Description,
    decimal? EstimatedHours,
    int SortOrder,
    GoalMilestoneState State,
    DateTimeOffset? CompletedAt,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GoalRoadmapStepResponse(
    int SortOrder,
    string Title,
    string? Description,
    decimal? EstimatedHours,
    IReadOnlyList<string>? Resources = null);

public sealed record GoalRoadmapResponse(
    string Summary,
    decimal? TotalEstimatedHours,
    IReadOnlyList<GoalRoadmapStepResponse> Steps);

public sealed record GoalRoadmapProposalResponse(
    AiProposalResponse Proposal,
    Guid GoalId,
    GoalRoadmapResponse Roadmap);

public sealed record ProjectTaskReorderRequest(
    Guid TaskId,
    TaskState State,
    Guid? BeforeTaskId,
    Guid ConcurrencyToken);

public sealed record ConcurrencyTokenRequest(Guid ConcurrencyToken);

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    LifeArea LifeArea,
    EisenhowerQuadrant Quadrant = EisenhowerQuadrant.Unsorted,
    DateTimeOffset? DueAt = null,
    int? EstimatedMinutes = null,
    DateTimeOffset? ReminderAt = null,
    Guid? ProjectId = null,
    DateTimeOffset? StartAt = null,
    int? SortOrder = null,
    TaskState State = TaskState.Todo,
    Guid? RoleId = null,
    Guid? GoalId = null);

public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    LifeArea LifeArea,
    EisenhowerQuadrant Quadrant,
    TaskState State,
    DateTimeOffset? StartAt,
    DateTimeOffset? DueAt,
    int? EstimatedMinutes,
    DateTimeOffset? ReminderAt,
    Guid? ProjectId,
    Guid? RoleId,
    Guid? GoalId,
    int SortOrder,
    Guid ConcurrencyToken);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Description,
    LifeArea LifeArea,
    EisenhowerQuadrant Quadrant,
    TaskState State,
    Guid? ProjectId,
    Guid? RoleId,
    Guid? GoalId,
    DateTimeOffset? StartAt,
    DateTimeOffset? DueAt,
    DateTimeOffset? ReminderAt,
    DateTimeOffset? CompletedAt,
    int? EstimatedMinutes,
    int SortOrder,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateCalendarEventRequest(
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Color = "indigo",
    string? Role = null);

public sealed record UpdateCalendarEventRequest(
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Color,
    string? Role,
    Guid ConcurrencyToken);

public sealed record CalendarEventResponse(
    Guid Id,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Color,
    string? Role,
    Guid ConcurrencyToken);

public sealed record CreateHabitRequest(string Title, string? Description, LifeArea LifeArea, int TargetPerWeek);

public sealed record UpdateHabitRequest(string Title, string? Description, LifeArea LifeArea, int TargetPerWeek, Guid ConcurrencyToken);

public sealed record HabitResponse(
    Guid Id,
    string Title,
    string? Description,
    LifeArea LifeArea,
    int TargetPerWeek,
    bool IsArchived,
    Guid ConcurrencyToken);

public sealed record CreateHabitCheckInRequest(DateOnly OccurredOn, string? Note);

public sealed record HabitCheckInResponse(Guid Id, Guid HabitId, DateOnly OccurredOn, string? Note);

public sealed record CreateJournalEntryRequest(string Content, string? Mood = null);

public sealed record UpdateJournalEntryRequest(string Content, string? Mood, Guid ConcurrencyToken);

public sealed record JournalInsightResponse(
    Guid Id,
    JournalInsightState State,
    IReadOnlyList<string> Emotions,
    string Feedback,
    string ProgressSummary,
    Guid SourceConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record JournalEntryResponse(
    Guid Id,
    string Content,
    string? Mood,
    JournalInsightResponse? Insight,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record JournalAnalysisResponse(AiProposalResponse Proposal, JournalInsightResponse Insight);

public sealed record CreateDocumentRequest(
    string Title,
    string Content,
    string ContentType = "text/plain",
    string SourceType = "text",
    string? RoadmapName = null);

public sealed record DocumentResponse(
    Guid Id,
    string Title,
    string ContentType,
    long SizeBytes,
    DocumentState State,
    DateTimeOffset CreatedAt);

public sealed record JobResponse(
    Guid Id,
    Guid DocumentId,
    IngestionState State,
    int AttemptCount,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record GraphQueryRequest(string Question, string Strategy = "hybrid", int? TopK = null, string Scope = "roadmap", IReadOnlyCollection<Guid>? DatasetIds = null);

public sealed record GraphQueryResponse(JsonElement Payload);

public sealed record CreateTaskScheduleProposalRequest(
    DateOnly? TargetDate = null,
    int? DurationMinutes = null);

public sealed record BalanceEvaluationResponse(
    Guid? ProposalId,
    string RiskLevel,
    string DataQuality,
    int AttentionScore,
    string Insight,
    string? SuggestedTitle,
    string? SuggestedDescription,
    bool RequiresConfirmation);

public sealed record InternalIngestionJobResponse(
    Guid JobId,
    Guid DocumentId,
    string RoadmapName,
    string SourceType,
    string Content);

public sealed record InternalIngestionCompletionRequest(
    bool Succeeded,
    int NodesCreated,
    int EdgesCreated,
    int VectorsEmbedded,
    string? Error = null);

public sealed record ProblemResponse(string Title, int Status, string? Detail, string TraceId);
public sealed record CreateKnowledgeDatasetRequest(Guid OwnerId, string Name, string? Description = null);

public sealed record UpdateKnowledgeDatasetRequest(string Name, string? Description, Guid ConcurrencyToken);

public sealed record AssignKnowledgeDatasetOwnerRequest(Guid OwnerId, Guid ConcurrencyToken);

public sealed record KnowledgeDatasetResponse(
    Guid Id,
    Guid OwnerId,
    string Name,
    string? Description,
    KnowledgeDatasetState State,
    int SourceFileCount,
    int SucceededSourceFileCount,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record KnowledgeSourceFileResponse(
    Guid Id,
    Guid DatasetId,
    string FileName,
    string ContentType,
    long ExpectedSizeBytes,
    string ExpectedSha256,
    long? UploadedSizeBytes,
    KnowledgeSourceFileState State,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record IssueDatasetUploadRequest(
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256);

public sealed record DatasetUploadResponse(
    KnowledgeSourceFileResponse SourceFile,
    string UploadUrl,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    DateTimeOffset ExpiresAt);

public sealed record FinalizeDatasetUploadResponse(
    KnowledgeSourceFileResponse SourceFile,
    DatasetIngestionJobResponse Job);

public sealed record DatasetIngestionJobResponse(
    Guid Id,
    Guid DatasetId,
    Guid SourceFileId,
    DatasetIngestionState State,
    int AttemptCount,
    string? TextractJobId,
    string Report,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record InternalDatasetIngestionJobResponse(
    Guid JobId,
    Guid DatasetId,
    Guid SourceFileId,
    Guid OwnerId,
    string DatasetName,
    string ObjectKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DatasetIngestionState State,
    string? TextractJobId);

public sealed record InternalDatasetIngestionCompletionRequest(
    DatasetIngestionState State,
    string Report,
    int ContentUnits,
    int Entities,
    int Facts,
    int Vectors,
    string? Error = null,
    string? TextractJobId = null);

public sealed record DatasetIngestionOutboxEvent(Guid JobId, Guid DatasetId, Guid SourceFileId);

public sealed record CreateHabitProposalPayload(
    string Title,
    string? Description,
    LifeArea LifeArea,
    int TargetPerWeek);

public sealed record CreateCoachConversationRequest(string Title);

public sealed record PostCoachMessageRequest(string Content);

public sealed record CoachConversationResponse(
    Guid Id,
    string Title,
    bool IsArchived,
    Guid ConcurrencyToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CoachMessageResponse(
    Guid Id,
    Guid ConversationId,
    CoachMessageSenderRole SenderRole,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record CoachObservationResponse(
    Guid Id,
    CoachObservationScope Scope,
    string Content,
    Guid? ConversationId,
    bool IsDismissed,
    DateTimeOffset CreatedAt);

public sealed record CoachInteractionResponse(
    CoachMessageResponse UserMessage,
    CoachMessageResponse CoachMessage,
    IReadOnlyList<CoachObservationResponse> Observations,
    IReadOnlyList<AiProposalResponse> Proposals);

