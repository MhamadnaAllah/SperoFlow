using System.ComponentModel.DataAnnotations;

namespace SperoFlow.Domain;

public enum TaskState
{
    Todo,
    InProgress,
    Completed,
    Cancelled,
}

public enum ProjectState
{
    Active,
    Completed,
    Archived,
}

public enum GoalState
{
    Active,
    Completed,
    Archived,
}

public enum GoalMilestoneState
{
    Pending,
    Completed,
    Archived,
}

public enum GoalRoadmapProposalState
{
    Pending,
    Approved,
    Cancelled,
}

public enum EisenhowerQuadrant
{
    Unsorted,
    Q1,
    Q2,
    Q3,
    Q4,
}

public enum LifeArea
{
    Work,
    Family,
    Physical,
    Spiritual,
    Social,
    Learning,
    Personal,
}

public enum LifeRoleCategory
{
    Internal,
    External,
}

public enum AiProposalKind
{
    CreateTask,
    CreateLifeRole,
    ApplyJournalInsight,
    ApplyTaskClassification,
    ApplyGoalRoadmap,
    ApplyTaskSchedule,
    CreateHabit,
    ApplyCoachObservation,
}

public enum AiProposalState
{
    Pending,
    Approved,
    Cancelled,
}

public enum JournalInsightState
{
    Pending,
    Approved,
    Cancelled,
}

public enum RoleDiscoveryFindingState
{
    Pending,
    Approved,
    Cancelled,
}

public enum DocumentState
{
    Pending,
    Processing,
    Completed,
    Failed,
}

public enum IngestionState
{
    Queued,
    Processing,
    Succeeded,
    Failed,
}

public enum KnowledgeDatasetState
{
    Active,
    Archived,
}

public enum KnowledgeSourceFileState
{
    PendingUpload,
    Uploaded,
    Queued,
    Processing,
    Completed,
    Failed,
}

public enum DatasetIngestionState
{
    Queued,
    Processing,
    WaitingForOcr,
    Succeeded,
    SucceededWithWarnings,
    Failed,
}

public enum BalanceRiskLevel
{
    Low,
    Medium,
    High,
    InsufficientData,
}

public sealed class DomainValidationException(string message) : InvalidOperationException(message);

public abstract class AuditableEntity
{
    protected AuditableEntity()
    {
    }

    protected AuditableEntity(Guid ownerId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new DomainValidationException("An owner is required.");
        }

        OwnerId = ownerId;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid OwnerId { get; private set; }

    protected void ChangeOwner(Guid ownerId)
    {
        if (ownerId == Guid.Empty)
        {
            throw new DomainValidationException("An owner is required.");
        }

        OwnerId = ownerId;
    }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    [ConcurrencyCheck]
    public Guid ConcurrencyToken { get; private set; } = Guid.CreateVersion7();

    public void Touch()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.CreateVersion7();
    }
}

public sealed class Project : AuditableEntity
{
    private Project()
    {
    }

    public Project(
        Guid ownerId,
        string name,
        string? description,
        string color,
        string icon,
        DateTimeOffset? startAt,
        DateTimeOffset? targetAt,
        int sortOrder = 0)
        : base(ownerId)
    {
        Apply(name, description, color, icon, startAt, targetAt, sortOrder);
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string Color { get; private set; } = "indigo";

    public string Icon { get; private set; } = "folder";

    public DateTimeOffset? StartAt { get; private set; }

    public DateTimeOffset? TargetAt { get; private set; }

    public ProjectState State { get; private set; } = ProjectState.Active;

    public int SortOrder { get; private set; }

    public void Update(
        string name,
        string? description,
        string color,
        string icon,
        DateTimeOffset? startAt,
        DateTimeOffset? targetAt,
        int sortOrder)
    {
        Apply(name, description, color, icon, startAt, targetAt, sortOrder);
        Touch();
    }

    public void Complete()
    {
        State = ProjectState.Completed;
        Touch();
    }

    public void Archive()
    {
        State = ProjectState.Archived;
        Touch();
    }

    public void Restore()
    {
        State = ProjectState.Active;
        Touch();
    }

    private void Apply(
        string name,
        string? description,
        string color,
        string icon,
        DateTimeOffset? startAt,
        DateTimeOffset? targetAt,
        int sortOrder)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > 240)
        {
            throw new DomainValidationException("Project name must be between 1 and 240 characters.");
        }

        if (description?.Length > 8_000)
        {
            throw new DomainValidationException("Project description cannot exceed 8,000 characters.");
        }

        if (string.IsNullOrWhiteSpace(color) || color.Trim().Length > 40)
        {
            throw new DomainValidationException("Project color is invalid.");
        }

        if (string.IsNullOrWhiteSpace(icon) || icon.Trim().Length > 64)
        {
            throw new DomainValidationException("Project icon is invalid.");
        }

        if (startAt.HasValue && targetAt.HasValue && startAt > targetAt)
        {
            throw new DomainValidationException("A project target date cannot be before its start date.");
        }

        if (sortOrder < 0)
        {
            throw new DomainValidationException("Project sort order cannot be negative.");
        }

        Name = normalizedName;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Color = color.Trim();
        Icon = icon.Trim();
        StartAt = startAt;
        TargetAt = targetAt;
        SortOrder = sortOrder;
    }
}
public sealed class Goal : AuditableEntity
{
    private Goal()
    {
    }

    public Goal(
        Guid ownerId,
        string title,
        string? description,
        LifeArea lifeArea,
        DateTimeOffset? targetAt,
        int sortOrder = 0,
        Guid? roleId = null)
        : base(ownerId)
    {
        Apply(title, description, lifeArea, targetAt, sortOrder, roleId);
    }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public LifeArea LifeArea { get; private set; }

    public Guid? RoleId { get; private set; }

    public DateTimeOffset? TargetAt { get; private set; }

    public GoalState State { get; private set; } = GoalState.Active;

    public int SortOrder { get; private set; }

    public string? RoadmapSummary { get; private set; }

    public void Update(
        string title,
        string? description,
        LifeArea lifeArea,
        DateTimeOffset? targetAt,
        int sortOrder,
        Guid? roleId)
    {
        Apply(title, description, lifeArea, targetAt, sortOrder, roleId);
        Touch();
    }

    public void Complete()
    {
        State = GoalState.Completed;
        Touch();
    }

    public void Archive()
    {
        State = GoalState.Archived;
        Touch();
    }

    public void Restore()
    {
        State = GoalState.Active;
        Touch();
    }

    public void ApplyRoadmap(string? summary)
    {
        var normalized = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        if (normalized?.Length > 4_000)
        {
            throw new DomainValidationException("Goal roadmap summary cannot exceed 4,000 characters.");
        }

        RoadmapSummary = normalized;
        Touch();
    }

    private void Apply(
        string title,
        string? description,
        LifeArea lifeArea,
        DateTimeOffset? targetAt,
        int sortOrder,
        Guid? roleId)
    {
        if (!Enum.IsDefined(lifeArea))
        {
            throw new DomainValidationException("Goal life area is invalid.");
        }

        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > 240)
        {
            throw new DomainValidationException("Goal title must be between 1 and 240 characters.");
        }

        if (description?.Length > 8_000)
        {
            throw new DomainValidationException("Goal description cannot exceed 8,000 characters.");
        }

        if (sortOrder < 0)
        {
            throw new DomainValidationException("Goal sort order cannot be negative.");
        }

        Title = normalizedTitle;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        LifeArea = lifeArea;
        TargetAt = targetAt;
        SortOrder = sortOrder;
        RoleId = roleId;
    }
}

public sealed class GoalMilestone : AuditableEntity
{
    private GoalMilestone()
    {
    }

    public GoalMilestone(
        Guid ownerId,
        Guid goalId,
        string title,
        string? description,
        decimal? estimatedHours,
        int sortOrder)
        : base(ownerId)
    {
        if (goalId == Guid.Empty)
        {
            throw new DomainValidationException("A milestone requires a goal.");
        }

        GoalId = goalId;
        Apply(title, description, estimatedHours, sortOrder);
    }

    public Guid GoalId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public decimal? EstimatedHours { get; private set; }

    public int SortOrder { get; private set; }

    public GoalMilestoneState State { get; private set; } = GoalMilestoneState.Pending;

    public DateTimeOffset? CompletedAt { get; private set; }

    public void Update(string title, string? description, decimal? estimatedHours, int sortOrder)
    {
        Apply(title, description, estimatedHours, sortOrder);
        Touch();
    }

    public void Complete()
    {
        if (State == GoalMilestoneState.Archived)
        {
            throw new DomainValidationException("An archived milestone cannot be completed.");
        }

        State = GoalMilestoneState.Completed;
        CompletedAt ??= DateTimeOffset.UtcNow;
        Touch();
    }

    public void Reopen()
    {
        if (State == GoalMilestoneState.Archived)
        {
            throw new DomainValidationException("An archived milestone cannot be reopened.");
        }

        State = GoalMilestoneState.Pending;
        CompletedAt = null;
        Touch();
    }

    public void Archive()
    {
        State = GoalMilestoneState.Archived;
        CompletedAt = null;
        Touch();
    }

    private void Apply(string title, string? description, decimal? estimatedHours, int sortOrder)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > 300)
        {
            throw new DomainValidationException("Milestone title must be between 1 and 300 characters.");
        }

        if (description?.Length > 4_000)
        {
            throw new DomainValidationException("Milestone description cannot exceed 4,000 characters.");
        }

        if (estimatedHours is < 0 or > 10_000)
        {
            throw new DomainValidationException("Milestone estimated hours must be between 0 and 10,000.");
        }

        if (sortOrder < 0)
        {
            throw new DomainValidationException("Milestone sort order cannot be negative.");
        }

        Title = normalizedTitle;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        EstimatedHours = estimatedHours;
        SortOrder = sortOrder;
    }
}

public sealed class GoalRoadmapProposal : AuditableEntity
{
    private GoalRoadmapProposal()
    {
    }

    public GoalRoadmapProposal(
        Guid ownerId,
        Guid proposalId,
        Guid goalId,
        Guid sourceConcurrencyToken,
        string protectedPayload)
        : base(ownerId)
    {
        if (proposalId == Guid.Empty || goalId == Guid.Empty || sourceConcurrencyToken == Guid.Empty)
        {
            throw new DomainValidationException("A roadmap proposal requires a proposal, goal, and source version.");
        }

        if (string.IsNullOrWhiteSpace(protectedPayload) || protectedPayload.Length > 64_000)
        {
            throw new DomainValidationException("Goal roadmap content is invalid.");
        }

        ProposalId = proposalId;
        GoalId = goalId;
        SourceConcurrencyToken = sourceConcurrencyToken;
        ProtectedPayload = protectedPayload;
    }

    public Guid ProposalId { get; private set; }

    public Guid GoalId { get; private set; }

    public Guid SourceConcurrencyToken { get; private set; }

    public string ProtectedPayload { get; private set; } = string.Empty;

    public GoalRoadmapProposalState State { get; private set; } = GoalRoadmapProposalState.Pending;

    public DateTimeOffset? ResolvedAt { get; private set; }

    public void Approve()
    {
        Resolve(GoalRoadmapProposalState.Approved);
    }

    public void Cancel()
    {
        Resolve(GoalRoadmapProposalState.Cancelled);
    }

    private void Resolve(GoalRoadmapProposalState state)
    {
        if (State != GoalRoadmapProposalState.Pending)
        {
            throw new DomainValidationException("Only a pending goal roadmap proposal can be resolved.");
        }

        State = state;
        ResolvedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}

public sealed class LifeRole : AuditableEntity
{
    private LifeRole()
    {
    }

    public LifeRole(
        Guid ownerId,
        string name,
        LifeRoleCategory category,
        LifeArea defaultLifeArea,
        string color,
        string icon,
        int sortOrder = 0,
        string? systemKey = null)
        : base(ownerId)
    {
        SystemKey = NormalizeSystemKey(systemKey);
        Apply(name, category, defaultLifeArea, color, icon, sortOrder);
    }

    public string Name { get; private set; } = string.Empty;

    public LifeRoleCategory Category { get; private set; }

    public LifeArea DefaultLifeArea { get; private set; }

    public string Color { get; private set; } = "#0053dc";

    public string Icon { get; private set; } = "person";

    public int SortOrder { get; private set; }

    public bool IsArchived { get; private set; }

    public string? SystemKey { get; private set; }

    public bool IsSystemRole => SystemKey is not null;

    public void Update(
        string name,
        LifeRoleCategory category,
        LifeArea defaultLifeArea,
        string color,
        string icon,
        int sortOrder)
    {
        if (IsSystemRole && category != LifeRoleCategory.Internal)
        {
            throw new DomainValidationException("A core internal role must remain internal.");
        }

        Apply(name, category, defaultLifeArea, color, icon, sortOrder);
        Touch();
    }

    public void Archive()
    {
        if (IsSystemRole)
        {
            throw new DomainValidationException("Core internal roles cannot be archived.");
        }

        IsArchived = true;
        Touch();
    }

    public void Restore()
    {
        IsArchived = false;
        Touch();
    }

    private void Apply(
        string name,
        LifeRoleCategory category,
        LifeArea defaultLifeArea,
        string color,
        string icon,
        int sortOrder)
    {
        if (!Enum.IsDefined(category) || !Enum.IsDefined(defaultLifeArea))
        {
            throw new DomainValidationException("Role category or life area is invalid.");
        }

        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > 160)
        {
            throw new DomainValidationException("Role name must be between 1 and 160 characters.");
        }

        if (string.IsNullOrWhiteSpace(color) || color.Trim().Length > 40)
        {
            throw new DomainValidationException("Role color is invalid.");
        }

        if (string.IsNullOrWhiteSpace(icon) || icon.Trim().Length > 64)
        {
            throw new DomainValidationException("Role icon is invalid.");
        }

        if (sortOrder < 0)
        {
            throw new DomainValidationException("Role sort order cannot be negative.");
        }

        Name = normalizedName;
        Category = category;
        DefaultLifeArea = defaultLifeArea;
        Color = color.Trim();
        Icon = icon.Trim();
        SortOrder = sortOrder;
    }

    private static string? NormalizeSystemKey(string? systemKey)
    {
        if (string.IsNullOrWhiteSpace(systemKey))
        {
            return null;
        }

        var normalized = systemKey.Trim().ToLowerInvariant();
        if (normalized.Length > 32)
        {
            throw new DomainValidationException("Role system key cannot exceed 32 characters.");
        }

        return normalized;
    }
}
public sealed class RoleDiscoveryFinding : AuditableEntity
{
    private RoleDiscoveryFinding()
    {
    }

    public RoleDiscoveryFinding(Guid ownerId, Guid proposalId, string protectedEvidence)
        : base(ownerId)
    {
        if (proposalId == Guid.Empty)
        {
            throw new DomainValidationException("A role discovery finding requires a proposal.");
        }

        if (string.IsNullOrWhiteSpace(protectedEvidence) || protectedEvidence.Length > 24_000)
        {
            throw new DomainValidationException("Role discovery evidence is invalid.");
        }

        ProposalId = proposalId;
        ProtectedEvidence = protectedEvidence;
    }

    public Guid ProposalId { get; private set; }

    public string ProtectedEvidence { get; private set; } = string.Empty;

    public RoleDiscoveryFindingState State { get; private set; } = RoleDiscoveryFindingState.Pending;

    public DateTimeOffset? ResolvedAt { get; private set; }

    public void Approve()
    {
        if (State != RoleDiscoveryFindingState.Pending)
        {
            throw new DomainValidationException("Only a pending role discovery finding can be approved.");
        }

        State = RoleDiscoveryFindingState.Approved;
        ResolvedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Cancel()
    {
        if (State != RoleDiscoveryFindingState.Pending)
        {
            throw new DomainValidationException("Only a pending role discovery finding can be cancelled.");
        }

        State = RoleDiscoveryFindingState.Cancelled;
        ResolvedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
public sealed class TaskItem : AuditableEntity

{
    private TaskItem()
    {
    }

    public TaskItem(
        Guid ownerId,
        string title,
        string? description,
        LifeArea lifeArea,
        EisenhowerQuadrant quadrant = EisenhowerQuadrant.Unsorted,
        DateTimeOffset? dueAt = null,
        int? estimatedMinutes = null,
        DateTimeOffset? startAt = null,
        Guid? projectId = null,
        int sortOrder = 0,
        TaskState state = TaskState.Todo,
        Guid? roleId = null,
        Guid? goalId = null)
        : base(ownerId)
    {
        Apply(title, description, lifeArea, quadrant, startAt, dueAt, estimatedMinutes, null, projectId, sortOrder, roleId, goalId);
        ApplyState(state);
    }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public LifeArea LifeArea { get; private set; }

    public EisenhowerQuadrant Quadrant { get; private set; }

    public TaskState State { get; private set; } = TaskState.Todo;

    public Guid? ProjectId { get; private set; }

    public Guid? RoleId { get; private set; }

    public Guid? GoalId { get; private set; }

    public DateTimeOffset? StartAt { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    public DateTimeOffset? ReminderAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public int? EstimatedMinutes { get; private set; }

    public string? Source { get; private set; }

    public int SortOrder { get; private set; }

    public void Update(
        string title,
        string? description,
        LifeArea lifeArea,
        EisenhowerQuadrant quadrant,
        TaskState state,
        DateTimeOffset? startAt,
        DateTimeOffset? dueAt,
        int? estimatedMinutes,
        DateTimeOffset? reminderAt,
        Guid? projectId,
        int sortOrder,
        Guid? roleId = null,
        Guid? goalId = null)
    {
        Apply(title, description, lifeArea, quadrant, startAt, dueAt, estimatedMinutes, reminderAt, projectId, sortOrder, roleId, goalId);
        ApplyState(state);
        Touch();
    }

    public void Reposition(TaskState state, int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new DomainValidationException("Task sort order cannot be negative.");
        }

        ApplyState(state);
        SortOrder = sortOrder;
        Touch();
    }

    public void Complete()
    {
        ApplyState(TaskState.Completed);
        Touch();
    }

    public void Reopen()
    {
        ApplyState(TaskState.Todo);
        Touch();
    }

    public void Cancel()
    {
        ApplyState(TaskState.Cancelled);
        Touch();
    }

    public void Schedule(DateTimeOffset startAt, int estimatedMinutes)
    {
        if (State is TaskState.Completed or TaskState.Cancelled)
        {
            throw new DomainValidationException("Only active tasks can be scheduled.");
        }

        if (estimatedMinutes is < 5 or > 480)
        {
            throw new DomainValidationException("A scheduled task must be between 5 and 480 minutes.");
        }

        if (DueAt.HasValue && startAt.AddMinutes(estimatedMinutes) > DueAt.Value)
        {
            throw new DomainValidationException("A scheduled task cannot end after its due date.");
        }

        StartAt = startAt;
        EstimatedMinutes = estimatedMinutes;
        Touch();
    }

    public void SetQuadrant(EisenhowerQuadrant quadrant)
    {
        if (!Enum.IsDefined(quadrant))
        {
            throw new DomainValidationException("Task quadrant is invalid.");
        }

        Quadrant = quadrant;
        Touch();
    }

    public void SetSource(string? source)
    {
        if (source?.Length > 100)
        {
            throw new DomainValidationException("Task source cannot exceed 100 characters.");
        }

        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        Touch();
    }

    private void Apply(
        string title,
        string? description,
        LifeArea lifeArea,
        EisenhowerQuadrant quadrant,
        DateTimeOffset? startAt,
        DateTimeOffset? dueAt,
        int? estimatedMinutes,
        DateTimeOffset? reminderAt,
        Guid? projectId,
        int sortOrder,
        Guid? roleId,
        Guid? goalId)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > 500)
        {
            throw new DomainValidationException("Task title must be between 1 and 500 characters.");
        }

        if (description?.Length > 8_000)
        {
            throw new DomainValidationException("Task description cannot exceed 8,000 characters.");
        }

        if (estimatedMinutes is < 1 or > 1_440)
        {
            throw new DomainValidationException("Estimated minutes must be between 1 and 1,440.");
        }

        if (reminderAt.HasValue && dueAt.HasValue && reminderAt > dueAt)
        {
            throw new DomainValidationException("A reminder cannot be after its due date.");
        }

        if (startAt.HasValue && dueAt.HasValue && startAt > dueAt)
        {
            throw new DomainValidationException("A task due date cannot be before its start date.");
        }

        if (sortOrder < 0)
        {
            throw new DomainValidationException("Task sort order cannot be negative.");
        }

        Title = normalizedTitle;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        LifeArea = lifeArea;
        Quadrant = quadrant;
        ProjectId = projectId;
        RoleId = roleId;
        GoalId = goalId;
        StartAt = startAt;
        DueAt = dueAt;
        EstimatedMinutes = estimatedMinutes;
        ReminderAt = reminderAt;
        SortOrder = sortOrder;
    }

    private void ApplyState(TaskState state)
    {
        State = state;
        if (state == TaskState.Completed)
        {
            CompletedAt ??= DateTimeOffset.UtcNow;
            return;
        }

        CompletedAt = null;
    }
}

public sealed class CalendarEvent : AuditableEntity
{
    private CalendarEvent()
    {
    }

    public CalendarEvent(
        Guid ownerId,
        string title,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        string color,
        string? role)
        : base(ownerId)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 500)
        {
            throw new DomainValidationException("Calendar event title is required and cannot exceed 500 characters.");
        }

        if (endsAt <= startsAt)
        {
            throw new DomainValidationException("Calendar event end time must be after the start time.");
        }

        Title = title.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        Color = string.IsNullOrWhiteSpace(color) ? "indigo" : color.Trim();
        Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
    }

    public string Title { get; private set; } = string.Empty;

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public string Color { get; private set; } = "indigo";

    public string? Role { get; private set; }

    public void Update(string title, DateTimeOffset startsAt, DateTimeOffset endsAt, string color, string? role)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 500 || endsAt <= startsAt)
        {
            throw new DomainValidationException("Calendar event data is invalid.");
        }

        Title = title.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        Color = string.IsNullOrWhiteSpace(color) ? "indigo" : color.Trim();
        Role = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        Touch();
    }
}

public sealed class Habit : AuditableEntity
{
    private Habit()
    {
    }

    public Habit(Guid ownerId, string title, string? description, LifeArea lifeArea, int targetPerWeek)
        : base(ownerId)
    {
        Apply(title, description, lifeArea, targetPerWeek);
    }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public LifeArea LifeArea { get; private set; }

    public int TargetPerWeek { get; private set; }

    public bool IsArchived { get; private set; }

    public void Update(string title, string? description, LifeArea lifeArea, int targetPerWeek)
    {
        Apply(title, description, lifeArea, targetPerWeek);
        Touch();
    }

    public void Archive()
    {
        IsArchived = true;
        Touch();
    }

    public void Restore()
    {
        IsArchived = false;
        Touch();
    }

    private void Apply(string title, string? description, LifeArea lifeArea, int targetPerWeek)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 300)
        {
            throw new DomainValidationException("Habit title is required and cannot exceed 300 characters.");
        }

        if (description?.Length > 4_000 || targetPerWeek is < 1 or > 21)
        {
            throw new DomainValidationException("Habit data is invalid.");
        }

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        LifeArea = lifeArea;
        TargetPerWeek = targetPerWeek;
    }
}

public sealed class HabitCheckIn : AuditableEntity
{
    private HabitCheckIn()
    {
    }

    public HabitCheckIn(Guid ownerId, Guid habitId, DateOnly occurredOn, string? note)
        : base(ownerId)
    {
        if (habitId == Guid.Empty)
        {
            throw new DomainValidationException("A habit is required.");
        }

        HabitId = habitId;
        OccurredOn = occurredOn;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    public Guid HabitId { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public string? Note { get; private set; }
}

public sealed class JournalEntry : AuditableEntity
{
    private JournalEntry()
    {
    }

    public JournalEntry(Guid ownerId, string protectedContent, string? mood = null)
        : base(ownerId)
    {
        if (string.IsNullOrWhiteSpace(protectedContent))
        {
            throw new DomainValidationException("Journal content is required.");
        }

        ProtectedContent = protectedContent;
        Mood = NormalizeMood(mood);
    }

    public string ProtectedContent { get; private set; } = string.Empty;

    public string? Mood { get; private set; }

    public void ReplaceContent(string protectedContent, string? mood = null)
    {
        if (string.IsNullOrWhiteSpace(protectedContent))
        {
            throw new DomainValidationException("Journal content is required.");
        }

        ProtectedContent = protectedContent;
        Mood = NormalizeMood(mood);
        Touch();
    }

    private static string? NormalizeMood(string? mood)
    {
        if (string.IsNullOrWhiteSpace(mood))
        {
            return null;
        }

        var normalized = mood.Trim();
        if (normalized.Length > 32)
        {
            throw new DomainValidationException("Journal mood cannot exceed 32 characters.");
        }

        return normalized;
    }
}

public sealed class JournalInsight : AuditableEntity
{
    private JournalInsight()
    {
    }

    public JournalInsight(Guid ownerId, Guid journalEntryId, Guid sourceConcurrencyToken, string protectedPayload)
        : base(ownerId)
    {
        if (journalEntryId == Guid.Empty)
        {
            throw new DomainValidationException("A journal insight requires a journal entry.");
        }

        if (sourceConcurrencyToken == Guid.Empty)
        {
            throw new DomainValidationException("A journal insight requires the source journal revision.");
        }

        if (string.IsNullOrWhiteSpace(protectedPayload) || protectedPayload.Length > 24_000)
        {
            throw new DomainValidationException("Journal insight content is invalid.");
        }

        JournalEntryId = journalEntryId;
        SourceConcurrencyToken = sourceConcurrencyToken;
        ProtectedPayload = protectedPayload;
    }

    public Guid JournalEntryId { get; private set; }

    public Guid SourceConcurrencyToken { get; private set; }

    public string ProtectedPayload { get; private set; } = string.Empty;

    public JournalInsightState State { get; private set; } = JournalInsightState.Pending;

    public DateTimeOffset? ResolvedAt { get; private set; }

    public void Approve()
    {
        if (State != JournalInsightState.Pending)
        {
            throw new DomainValidationException("Only a pending journal insight can be approved.");
        }

        State = JournalInsightState.Approved;
        ResolvedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Cancel()
    {
        if (State != JournalInsightState.Pending)
        {
            throw new DomainValidationException("Only a pending journal insight can be cancelled.");
        }

        State = JournalInsightState.Cancelled;
        ResolvedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
public sealed class DocumentAsset : AuditableEntity
{
    private DocumentAsset()
    {
    }

    public DocumentAsset(Guid ownerId, string title, string objectKey, string contentType, long sizeBytes)
        : base(ownerId)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 500)
        {
            throw new DomainValidationException("Document title is required and cannot exceed 500 characters.");
        }

        if (string.IsNullOrWhiteSpace(objectKey) || string.IsNullOrWhiteSpace(contentType) || sizeBytes < 0)
        {
            throw new DomainValidationException("Document storage metadata is invalid.");
        }

        Title = title.Trim();
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
    }

    public string Title { get; private set; } = string.Empty;

    public string ObjectKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = "text/plain";

    public long SizeBytes { get; private set; }

    public DocumentState State { get; private set; } = DocumentState.Pending;

    public string? FailureReason { get; private set; }

    public void MarkProcessing()
    {
        State = DocumentState.Processing;
        FailureReason = null;
        Touch();
    }

    public void MarkCompleted()
    {
        State = DocumentState.Completed;
        FailureReason = null;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        State = DocumentState.Failed;
        FailureReason = reason.Length > 1_000 ? reason[..1_000] : reason;
        Touch();
    }
}

public sealed class IngestionJob : AuditableEntity
{
    private IngestionJob()
    {
    }

    public IngestionJob(Guid ownerId, Guid documentId, string roadmapName, string sourceType)
        : base(ownerId)
    {
        if (documentId == Guid.Empty || string.IsNullOrWhiteSpace(roadmapName) || string.IsNullOrWhiteSpace(sourceType))
        {
            throw new DomainValidationException("Ingestion job data is invalid.");
        }

        DocumentId = documentId;
        RoadmapName = roadmapName.Trim();
        SourceType = sourceType.Trim();
    }

    public Guid DocumentId { get; private set; }

    public string RoadmapName { get; private set; } = string.Empty;

    public string SourceType { get; private set; } = "text";

    public IngestionState State { get; private set; } = IngestionState.Queued;

    public int AttemptCount { get; private set; }

    public string? FailureReason { get; private set; }

    public void MarkProcessing()
    {
        State = IngestionState.Processing;
        AttemptCount++;
        FailureReason = null;
        Touch();
    }

    public void MarkSucceeded()
    {
        State = IngestionState.Succeeded;
        FailureReason = null;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        State = IngestionState.Failed;
        FailureReason = reason.Length > 1_000 ? reason[..1_000] : reason;
        Touch();
    }
}

public sealed class InAppNotification : AuditableEntity
{
    private InAppNotification()
    {
    }

    public InAppNotification(Guid ownerId, string category, string title, string body, string sourceKey)
        : base(ownerId)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(sourceKey))
        {
            throw new DomainValidationException("Notification data is invalid.");
        }

        Category = category.Trim();
        Title = title.Trim();
        Body = body.Trim();
        SourceKey = sourceKey.Trim();
    }

    public string Category { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public string SourceKey { get; private set; } = string.Empty;

    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead()
    {
        ReadAt = DateTimeOffset.UtcNow;
        Touch();
    }
}

public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    public AuditEvent(Guid? ownerId, string category, string action, string entityType, Guid? entityId, string metadata)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(entityType))
        {
            throw new DomainValidationException("Audit event data is invalid.");
        }

        OwnerId = ownerId;
        Category = category.Trim();
        Action = action.Trim();
        EntityType = entityType.Trim();
        EntityId = entityId;
        Metadata = metadata;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid? OwnerId { get; private set; }

    public string Category { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public Guid? EntityId { get; private set; }

    public string Metadata { get; private set; } = "{}";

    public DateTimeOffset OccurredAt { get; private set; } = DateTimeOffset.UtcNow;
}

public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(Guid? ownerId, string type, string payload)
    {
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(payload))
        {
            throw new DomainValidationException("Outbox message data is invalid.");
        }

        OwnerId = ownerId;
        Type = type.Trim();
        Payload = payload;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid? OwnerId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? DispatchedAt { get; private set; }

    public int DispatchAttemptCount { get; private set; }

    public void MarkDispatched()
    {
        DispatchAttemptCount++;
        DispatchedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAttempted()
    {
        DispatchAttemptCount++;
    }
}

public sealed class AiActionProposal : AuditableEntity
{
    private AiActionProposal()
    {
    }

    public AiActionProposal(
        Guid ownerId,
        AiProposalKind kind,
        string source,
        string sourceKey,
        string title,
        string description,
        string payload)
        : base(ownerId)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Trim().Length > 100)
        {
            throw new DomainValidationException("Proposal source is invalid.");
        }

        if (string.IsNullOrWhiteSpace(sourceKey) || sourceKey.Trim().Length > 160)
        {
            throw new DomainValidationException("Proposal source key is invalid.");
        }

        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 300)
        {
            throw new DomainValidationException("Proposal title is invalid.");
        }

        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 4_000)
        {
            throw new DomainValidationException("Proposal description is invalid.");
        }

        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 64_000)
        {
            throw new DomainValidationException("Proposal payload is invalid.");
        }

        Kind = kind;
        Source = source.Trim();
        SourceKey = sourceKey.Trim();
        Title = title.Trim();
        Description = description.Trim();
        Payload = payload;
    }

    public AiProposalKind Kind { get; private set; }

    public AiProposalState State { get; private set; } = AiProposalState.Pending;

    public string Source { get; private set; } = string.Empty;

    public string SourceKey { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public Guid? AppliedEntityId { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public void Approve(Guid appliedEntityId)
    {
        if (State != AiProposalState.Pending)
        {
            throw new DomainValidationException("Only a pending proposal can be approved.");
        }

        if (appliedEntityId == Guid.Empty)
        {
            throw new DomainValidationException("An approved proposal must identify the applied record.");
        }

        State = AiProposalState.Approved;
        AppliedEntityId = appliedEntityId;
        ResolvedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Cancel()
    {
        if (State != AiProposalState.Pending)
        {
            throw new DomainValidationException("Only a pending proposal can be cancelled.");
        }

        State = AiProposalState.Cancelled;
        ResolvedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
public sealed class BalanceProposal : AuditableEntity
{
    private BalanceProposal()
    {
    }

    public BalanceProposal(
        Guid ownerId,
        string auditKey,
        BalanceRiskLevel riskLevel,
        string insight,
        string? suggestedTitle,
        string? suggestedDescription)
        : base(ownerId)
    {
        AuditKey = auditKey;
        RiskLevel = riskLevel;
        Insight = insight;
        SuggestedTitle = suggestedTitle;
        SuggestedDescription = suggestedDescription;
    }

    public string AuditKey { get; private set; } = string.Empty;

    public BalanceRiskLevel RiskLevel { get; private set; }

    public string Insight { get; private set; } = string.Empty;

    public string? SuggestedTitle { get; private set; }

    public string? SuggestedDescription { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public void Confirm()
    {
        ConfirmedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
public sealed class KnowledgeDataset : AuditableEntity
{
    private KnowledgeDataset()
    {
    }

    public KnowledgeDataset(Guid ownerId, string name, string? description)
        : base(ownerId)
    {
        Apply(name, description);
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public KnowledgeDatasetState State { get; private set; } = KnowledgeDatasetState.Active;

    public void Update(string name, string? description)
    {
        Apply(name, description);
        Touch();
    }

    public void AssignOwner(Guid ownerId)
    {
        ChangeOwner(ownerId);
        Touch();
    }

    public void Archive()
    {
        State = KnowledgeDatasetState.Archived;
        Touch();
    }

    public void Restore()
    {
        State = KnowledgeDatasetState.Active;
        Touch();
    }

    private void Apply(string name, string? description)
    {
        var normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > 240)
        {
            throw new DomainValidationException("Dataset name must be between 1 and 240 characters.");
        }

        if (description?.Length > 8_000)
        {
            throw new DomainValidationException("Dataset description cannot exceed 8,000 characters.");
        }

        Name = normalizedName;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
}

public sealed class KnowledgeSourceFile : AuditableEntity
{
    private const long MaximumUploadBytes = 100L * 1024 * 1024;

    private KnowledgeSourceFile()
    {
    }

    public KnowledgeSourceFile(
        Guid ownerId,
        Guid datasetId,
        string fileName,
        string objectKey,
        string contentType,
        long expectedSizeBytes,
        string expectedSha256)
        : base(ownerId)
    {
        if (datasetId == Guid.Empty)
        {
            throw new DomainValidationException("A dataset is required for an uploaded source file.");
        }

        DatasetId = datasetId;
        FileName = NormalizeFileName(fileName);
        ObjectKey = NormalizeObjectKey(objectKey);
        ContentType = NormalizeContentType(contentType);
        ExpectedSizeBytes = ValidateSize(expectedSizeBytes);
        ExpectedSha256 = NormalizeHash(expectedSha256);
    }

    public Guid DatasetId { get; private set; }

    public string FileName { get; private set; } = string.Empty;

    public string ObjectKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = "application/octet-stream";

    public long ExpectedSizeBytes { get; private set; }

    public string ExpectedSha256 { get; private set; } = string.Empty;

    public long? UploadedSizeBytes { get; private set; }

    public string? UploadedSha256 { get; private set; }

    public KnowledgeSourceFileState State { get; private set; } = KnowledgeSourceFileState.PendingUpload;

    public string? FailureReason { get; private set; }

    public void AssignOwner(Guid ownerId)
    {
        ChangeOwner(ownerId);
        Touch();
    }
    public void ConfirmUpload(long actualSizeBytes, string actualSha256, string actualContentType)
    {
        if (State != KnowledgeSourceFileState.PendingUpload)
        {
            throw new DomainValidationException("This source file upload has already been finalized.");
        }

        if (ValidateSize(actualSizeBytes) != ExpectedSizeBytes ||
            !string.Equals(NormalizeHash(actualSha256), ExpectedSha256, StringComparison.Ordinal) ||
            !string.Equals(NormalizeContentType(actualContentType), ContentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException("Uploaded source metadata does not match the approved upload request.");
        }

        UploadedSizeBytes = actualSizeBytes;
        UploadedSha256 = ExpectedSha256;
        State = KnowledgeSourceFileState.Uploaded;
        FailureReason = null;
        Touch();
    }

    public void MarkQueued()
    {
        if (State is not (KnowledgeSourceFileState.Uploaded or KnowledgeSourceFileState.Failed))
        {
            throw new DomainValidationException("Only uploaded or failed sources can be queued for ingestion.");
        }

        State = KnowledgeSourceFileState.Queued;
        FailureReason = null;
        Touch();
    }

    public void MarkProcessing()
    {
        State = KnowledgeSourceFileState.Processing;
        FailureReason = null;
        Touch();
    }

    public void MarkCompleted()
    {
        State = KnowledgeSourceFileState.Completed;
        FailureReason = null;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        State = KnowledgeSourceFileState.Failed;
        FailureReason = NormalizeFailure(reason);
        Touch();
    }

    private static string NormalizeFileName(string fileName)
    {
        var normalized = fileName?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 500 || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || normalized.Contains('/') || normalized.Contains('\\') || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new DomainValidationException("The source file name is invalid.");
        }

        return normalized;
    }

    private static string NormalizeObjectKey(string objectKey)
    {
        var normalized = objectKey?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 1_000 || normalized.StartsWith('/') || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new DomainValidationException("The source object key is invalid.");
        }

        return normalized;
    }

    private static string NormalizeContentType(string contentType)
    {
        var normalized = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length is < 3 or > 255)
        {
            throw new DomainValidationException("The source content type is invalid.");
        }

        return normalized;
    }

    private static long ValidateSize(long sizeBytes)
    {
        if (sizeBytes is < 1 or > MaximumUploadBytes)
        {
            throw new DomainValidationException("Dataset source files must be between 1 byte and 100 MB.");
        }

        return sizeBytes;
    }

    private static string NormalizeHash(string hash)
    {
        var normalized = hash?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new DomainValidationException("A SHA-256 hash is required for every dataset source file.");
        }

        return normalized;
    }

    private static string NormalizeFailure(string reason)
    {
        var normalized = reason?.Trim() ?? "Dataset ingestion failed without a diagnostic.";
        return normalized.Length > 1_000 ? normalized[..1_000] : normalized;
    }
}

public sealed class DatasetIngestionJob : AuditableEntity
{
    private DatasetIngestionJob()
    {
    }

    public DatasetIngestionJob(Guid ownerId, Guid datasetId, Guid sourceFileId)
        : base(ownerId)
    {
        if (datasetId == Guid.Empty || sourceFileId == Guid.Empty)
        {
            throw new DomainValidationException("Dataset ingestion jobs require a dataset and source file.");
        }

        DatasetId = datasetId;
        SourceFileId = sourceFileId;
    }

    public Guid DatasetId { get; private set; }

    public Guid SourceFileId { get; private set; }

    public DatasetIngestionState State { get; private set; } = DatasetIngestionState.Queued;

    public int AttemptCount { get; private set; }

    public string? TextractJobId { get; private set; }

    public string Report { get; private set; } = "{}";

    public string? FailureReason { get; private set; }

    public void AssignOwner(Guid ownerId)
    {
        ChangeOwner(ownerId);
        Touch();
    }
    public void MarkProcessing()
    {
        State = DatasetIngestionState.Processing;
        AttemptCount++;
        FailureReason = null;
        Touch();
    }

    public void MarkWaitingForOcr(string textractJobId)
    {
        if (string.IsNullOrWhiteSpace(textractJobId) || textractJobId.Trim().Length > 200)
        {
            throw new DomainValidationException("A valid Textract job ID is required.");
        }

        State = DatasetIngestionState.WaitingForOcr;
        TextractJobId = textractJobId.Trim();
        Touch();
    }

    public void MarkSucceeded(string report)
    {
        State = DatasetIngestionState.Succeeded;
        Report = NormalizeReport(report);
        FailureReason = null;
        Touch();
    }

    public void MarkSucceededWithWarnings(string report)
    {
        State = DatasetIngestionState.SucceededWithWarnings;
        Report = NormalizeReport(report);
        FailureReason = null;
        Touch();
    }

    public void MarkFailed(string reason, string? report = null)
    {
        State = DatasetIngestionState.Failed;
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Dataset ingestion failed without a diagnostic." : reason.Trim();
        FailureReason = normalizedReason.Length > 1_000 ? normalizedReason[..1_000] : normalizedReason;
        if (report is not null)
        {
            Report = NormalizeReport(report);
        }

        Touch();
    }

    public void Retry()
    {
        if (State != DatasetIngestionState.Failed)
        {
            throw new DomainValidationException("Only failed dataset ingestion jobs can be retried.");
        }

        State = DatasetIngestionState.Queued;
        FailureReason = null;
        TextractJobId = null;
        Touch();
    }

    private static string NormalizeReport(string report)
    {
        var normalized = string.IsNullOrWhiteSpace(report) ? "{}" : report.Trim();
        if (normalized.Length > 32_000)
        {
            throw new DomainValidationException("Dataset ingestion reports cannot exceed 32,000 characters.");
        }

        return normalized;
    }
}

public sealed class AdminBootstrap
{
    public const int SingletonId = 1;

    private AdminBootstrap()
    {
    }

    public AdminBootstrap(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainValidationException("The bootstrap administrator is required.");
        }

        UserId = userId;
        ReservedAt = DateTimeOffset.UtcNow;
    }

    public int Id { get; private set; } = SingletonId;

    public Guid UserId { get; private set; }

    public DateTimeOffset ReservedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public void MarkCompleted()
    {
        if (CompletedAt.HasValue)
        {
            throw new DomainValidationException("The administrator bootstrap has already completed.");
        }

        CompletedAt = DateTimeOffset.UtcNow;
    }
}

public enum CoachMessageSenderRole
{
    User,
    Coach,
}

public enum CoachObservationScope
{
    HabitPattern,
    QuadrantImbalance,
    ReflectionInsight,
    SchedulingTrend,
}

public sealed class CoachConversation : AuditableEntity
{
    private CoachConversation()
    {
    }

    public CoachConversation(Guid ownerId, string title)
        : base(ownerId)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > 300)
        {
            throw new DomainValidationException("Conversation title must be between 1 and 300 characters.");
        }

        Title = normalizedTitle;
    }

    public string Title { get; private set; } = string.Empty;

    public bool IsArchived { get; private set; }

    public void UpdateTitle(string title)
    {
        var normalizedTitle = title?.Trim() ?? string.Empty;
        if (normalizedTitle.Length is < 1 or > 300)
        {
            throw new DomainValidationException("Conversation title must be between 1 and 300 characters.");
        }

        Title = normalizedTitle;
        Touch();
    }

    public void Archive()
    {
        IsArchived = true;
        Touch();
    }

    public void Restore()
    {
        IsArchived = false;
        Touch();
    }
}

public sealed class CoachMessage : AuditableEntity
{
    private CoachMessage()
    {
    }

    public CoachMessage(
        Guid ownerId,
        Guid conversationId,
        CoachMessageSenderRole senderRole,
        string protectedContent)
        : base(ownerId)
    {
        if (conversationId == Guid.Empty)
        {
            throw new DomainValidationException("A conversation is required.");
        }

        if (string.IsNullOrWhiteSpace(protectedContent) || protectedContent.Length > 16_000)
        {
            throw new DomainValidationException("Message content is invalid.");
        }

        if (!Enum.IsDefined(senderRole))
        {
            throw new DomainValidationException("Sender role is invalid.");
        }

        ConversationId = conversationId;
        SenderRole = senderRole;
        ProtectedContent = protectedContent;
    }

    public Guid ConversationId { get; private set; }

    public CoachMessageSenderRole SenderRole { get; private set; }

    public string ProtectedContent { get; private set; } = string.Empty;
}

public sealed class CoachObservation : AuditableEntity
{
    private CoachObservation()
    {
    }

    public CoachObservation(
        Guid ownerId,
        CoachObservationScope scope,
        string protectedContent,
        Guid? conversationId = null)
        : base(ownerId)
    {
        if (!Enum.IsDefined(scope))
        {
            throw new DomainValidationException("Observation scope is invalid.");
        }

        if (string.IsNullOrWhiteSpace(protectedContent) || protectedContent.Length > 16_000)
        {
            throw new DomainValidationException("Observation content is invalid.");
        }

        Scope = scope;
        ProtectedContent = protectedContent;
        ConversationId = conversationId;
    }

    public CoachObservationScope Scope { get; private set; }

    public string ProtectedContent { get; private set; } = string.Empty;

    public Guid? ConversationId { get; private set; }

    public bool IsDismissed { get; private set; }

    public void Dismiss()
    {
        IsDismissed = true;
        Touch();
    }
}

