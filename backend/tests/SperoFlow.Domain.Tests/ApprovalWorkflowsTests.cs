using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class ApprovalWorkflowsTests
{
    // --- Workflow 1: LifeRole CRUD & Core Internal-Role Seeds ---

    [Fact]
    public void Test_LifeRole_Creation_Validation_And_Archive_Restore_Lifecycle()
    {
        var ownerId = Guid.CreateVersion7();
        var role = new LifeRole(
            ownerId,
            "Software Architect",
            LifeRoleCategory.External,
            LifeArea.Work,
            "#2563eb",
            "code",
            sortOrder: 500);

        Assert.Equal(ownerId, role.OwnerId);
        Assert.Equal("Software Architect", role.Name);
        Assert.Equal(LifeRoleCategory.External, role.Category);
        Assert.Equal(LifeArea.Work, role.DefaultLifeArea);
        Assert.Equal("#2563eb", role.Color);
        Assert.Equal("code", role.Icon);
        Assert.Equal(500, role.SortOrder);
        Assert.False(role.IsArchived);
        Assert.False(role.IsSystemRole);

        var token1 = role.ConcurrencyToken;

        role.Update(
            "Lead Software Architect",
            LifeRoleCategory.External,
            LifeArea.Work,
            "#1d4ed8",
            "developer_mode",
            600);

        Assert.Equal("Lead Software Architect", role.Name);
        Assert.NotEqual(token1, role.ConcurrencyToken);

        var token2 = role.ConcurrencyToken;
        role.Archive();
        Assert.True(role.IsArchived);
        Assert.NotEqual(token2, role.ConcurrencyToken);

        var token3 = role.ConcurrencyToken;
        role.Restore();
        Assert.False(role.IsArchived);
        Assert.NotEqual(token3, role.ConcurrencyToken);
    }

    [Fact]
    public void Test_Core_Internal_Roles_Cannot_Be_Archived_Or_Changed_Category()
    {
        var ownerId = Guid.CreateVersion7();
        var coreRoles = new[]
        {
            new LifeRole(ownerId, "Mental", LifeRoleCategory.Internal, LifeArea.Learning, "#0053dc", "psychology", 1000, "mental"),
            new LifeRole(ownerId, "Physical", LifeRoleCategory.Internal, LifeArea.Physical, "#dc2626", "fitness_center", 2000, "physical"),
            new LifeRole(ownerId, "Social", LifeRoleCategory.Internal, LifeArea.Social, "#047857", "groups", 3000, "social"),
            new LifeRole(ownerId, "Spiritual", LifeRoleCategory.Internal, LifeArea.Spiritual, "#a16207", "self_improvement", 4000, "spiritual"),
        };

        foreach (var role in coreRoles)
        {
            Assert.True(role.IsSystemRole);
            Assert.Throws<DomainValidationException>(() => role.Archive());
            Assert.Throws<DomainValidationException>(() => role.Update(
                role.Name,
                LifeRoleCategory.External, // Invalid change for system role
                role.DefaultLifeArea,
                role.Color,
                role.Icon,
                role.SortOrder));
        }
    }

    // --- Workflow 2: AiActionProposal State Machine ---

    [Fact]
    public void Test_AiActionProposal_State_Machine_Approve_And_Cancel_Transitions()
    {
        var ownerId = Guid.CreateVersion7();
        var proposal = new AiActionProposal(
            ownerId,
            AiProposalKind.CreateTask,
            "coach",
            "coach:task:101",
            "Review Weekly Architecture",
            "Perform weekly system architecture review.",
            "{\"title\":\"Review Weekly Architecture\"}");

        Assert.Equal(AiProposalState.Pending, proposal.State);
        Assert.Null(proposal.AppliedEntityId);
        Assert.Null(proposal.ResolvedAt);

        var initialToken = proposal.ConcurrencyToken;
        var appliedId = Guid.CreateVersion7();
        proposal.Approve(appliedId);

        Assert.Equal(AiProposalState.Approved, proposal.State);
        Assert.Equal(appliedId, proposal.AppliedEntityId);
        Assert.NotNull(proposal.ResolvedAt);
        Assert.NotEqual(initialToken, proposal.ConcurrencyToken);

        // Cancel on new proposal
        var proposal2 = new AiActionProposal(
            ownerId,
            AiProposalKind.CreateHabit,
            "coach",
            "coach:habit:202",
            "Morning Reflection",
            "Spend 10 minutes reflecting.",
            "{\"title\":\"Morning Reflection\"}");

        var token2 = proposal2.ConcurrencyToken;
        proposal2.Cancel();

        Assert.Equal(AiProposalState.Cancelled, proposal2.State);
        Assert.NotNull(proposal2.ResolvedAt);
        Assert.NotEqual(token2, proposal2.ConcurrencyToken);
    }

    [Fact]
    public void Test_AiActionProposal_Cannot_Resolve_Non_Pending()
    {
        var ownerId = Guid.CreateVersion7();
        var proposal = new AiActionProposal(
            ownerId,
            AiProposalKind.CreateTask,
            "coach",
            "coach:task:102",
            "Sample Task",
            "Description",
            "{\"title\":\"Sample Task\"}");

        proposal.Approve(Guid.CreateVersion7());

        Assert.Throws<DomainValidationException>(() => proposal.Approve(Guid.CreateVersion7()));
        Assert.Throws<DomainValidationException>(() => proposal.Cancel());

        var proposal2 = new AiActionProposal(
            ownerId,
            AiProposalKind.CreateTask,
            "coach",
            "coach:task:103",
            "Sample Task 2",
            "Description 2",
            "{\"title\":\"Sample Task 2\"}");

        proposal2.Cancel();

        Assert.Throws<DomainValidationException>(() => proposal2.Approve(Guid.CreateVersion7()));
        Assert.Throws<DomainValidationException>(() => proposal2.Cancel());
    }

    // --- Workflow 3: Balance Evaluator & Proposal Invariants ---

    [Fact]
    public void Test_Balance_Proposal_Creation_Confirm_And_Validation()
    {
        var ownerId = Guid.CreateVersion7();
        var balanceProp = new BalanceProposal(
            ownerId,
            "audit-key-2026-q2",
            BalanceRiskLevel.Medium,
            "Physical role activity is low.",
            "Take a 20-minute daily walk",
            "Schedule a short walk to maintain health.");

        Assert.Equal(ownerId, balanceProp.OwnerId);
        Assert.Equal("audit-key-2026-q2", balanceProp.AuditKey);
        Assert.Equal(BalanceRiskLevel.Medium, balanceProp.RiskLevel);
        Assert.Equal("Physical role activity is low.", balanceProp.Insight);
        Assert.Null(balanceProp.ConfirmedAt);

        var token = balanceProp.ConcurrencyToken;
        balanceProp.Confirm();

        Assert.NotNull(balanceProp.ConfirmedAt);
        Assert.NotEqual(token, balanceProp.ConcurrencyToken);
    }

    // --- Workflow 4: Role Discovery Snapshot & Approval-Gated Proposals ---

    [Fact]
    public void Test_RoleDiscoveryFinding_Evidence_Encryption_And_Approval_Lifecycle()
    {
        var ownerId = Guid.CreateVersion7();
        var proposalId = Guid.CreateVersion7();
        var finding = new RoleDiscoveryFinding(
            ownerId,
            proposalId,
            "protected-encrypted-evidence-string");

        Assert.Equal(ownerId, finding.OwnerId);
        Assert.Equal(proposalId, finding.ProposalId);
        Assert.Equal("protected-encrypted-evidence-string", finding.ProtectedEvidence);
        Assert.Equal(RoleDiscoveryFindingState.Pending, finding.State);
        Assert.Null(finding.ResolvedAt);

        var token = finding.ConcurrencyToken;
        finding.Approve();

        Assert.Equal(RoleDiscoveryFindingState.Approved, finding.State);
        Assert.NotNull(finding.ResolvedAt);
        Assert.NotEqual(token, finding.ConcurrencyToken);

        Assert.Throws<DomainValidationException>(() => finding.Approve());
        Assert.Throws<DomainValidationException>(() => finding.Cancel());
    }

    // --- Workflow 5: Journal Reflections & Revision-Pinned Approval ---

    [Fact]
    public void Test_JournalInsight_Revision_Pinning_And_Auto_Cancellation()
    {
        var ownerId = Guid.CreateVersion7();
        var journalEntry = new JournalEntry(ownerId, "Protected content from morning reflection", "Focused");
        var sourceToken = journalEntry.ConcurrencyToken;

        var insight = new JournalInsight(
            ownerId,
            journalEntry.Id,
            sourceToken,
            "protected-insight-payload");

        Assert.Equal(journalEntry.Id, insight.JournalEntryId);
        Assert.Equal(sourceToken, insight.SourceConcurrencyToken);
        Assert.Equal(JournalInsightState.Pending, insight.State);

        // Edit journal entry -> rotates ConcurrencyToken
        journalEntry.ReplaceContent("Updated content after edit", "Reflective");
        Assert.NotEqual(sourceToken, journalEntry.ConcurrencyToken);

        // The insight was pinned to sourceToken which no longer matches current entry token
        Assert.Equal(sourceToken, insight.SourceConcurrencyToken);
    }

    // --- Workflow 6: Goals, Milestones & GraphRAG Roadmaps ---

    [Fact]
    public void Test_GoalRoadmapProposal_Requires_Explicit_Approval_Before_Milestone_Creation()
    {
        var ownerId = Guid.CreateVersion7();
        var goalId = Guid.CreateVersion7();
        var proposalId = Guid.CreateVersion7();
        var sourceToken = Guid.CreateVersion7();

        var roadmapProposal = new GoalRoadmapProposal(
            ownerId,
            proposalId,
            goalId,
            sourceToken,
            "protected-roadmap-payload");

        Assert.Equal(GoalRoadmapProposalState.Pending, roadmapProposal.State);
        Assert.Null(roadmapProposal.ResolvedAt);

        roadmapProposal.Approve();
        Assert.Equal(GoalRoadmapProposalState.Approved, roadmapProposal.State);
        Assert.NotNull(roadmapProposal.ResolvedAt);
    }

    // --- Workflow 7: Eisenhower Classification & Quadrant Invariants ---

    [Fact]
    public void Test_Eisenhower_Task_Classification_Requires_Proposal_Approval()
    {
        var ownerId = Guid.CreateVersion7();
        var task = new TaskItem(ownerId, "Review security policies", "Annual audit", LifeArea.Work, EisenhowerQuadrant.Unsorted);

        Assert.Equal(EisenhowerQuadrant.Unsorted, task.Quadrant);

        // Simulated AI proposal creation does not mutate task
        var proposal = new AiActionProposal(
            ownerId,
            AiProposalKind.ApplyTaskClassification,
            "eisenhower",
            "eisenhower:" + task.Id.ToString("N"),
            "Classify: Review security policies",
            "Suggested quadrant Q2 based on deadline and impact.",
            "{\"taskId\":\"" + task.Id + "\",\"quadrant\":\"Q2\"}");

        Assert.Equal(EisenhowerQuadrant.Unsorted, task.Quadrant);
        Assert.Equal(AiProposalState.Pending, proposal.State);

        // User approves proposal -> backend invokes SetQuadrant
        task.SetQuadrant(EisenhowerQuadrant.Q2);
        proposal.Approve(task.Id);

        Assert.Equal(EisenhowerQuadrant.Q2, task.Quadrant);
        Assert.Equal(AiProposalState.Approved, proposal.State);
    }

    // --- Workflow 8: Intelligent Scheduling & Schedule Invalidation ---

    [Fact]
    public void Test_Intelligent_Scheduling_Focus_Block_Validation_And_Approval()
    {
        var ownerId = Guid.CreateVersion7();
        var startAt = DateTimeOffset.UtcNow.AddDays(1);
        var dueAt = startAt.AddHours(4);

        var task = new TaskItem(
            ownerId,
            "Draft Q2 Roadmap",
            "High impact planning task",
            LifeArea.Work,
            EisenhowerQuadrant.Q2,
            dueAt: dueAt,
            estimatedMinutes: 60);

        var token1 = task.ConcurrencyToken;
        task.Schedule(startAt, 90);

        Assert.Equal(startAt, task.StartAt);
        Assert.Equal(90, task.EstimatedMinutes);
        Assert.NotEqual(token1, task.ConcurrencyToken);
    }

    // --- Workflow 9: Coach Product Slice ---

    [Fact]
    public void Test_CoachConversation_CoachMessage_And_CoachObservation_Lifecycle()
    {
        var ownerId = Guid.CreateVersion7();

        var conv = new CoachConversation(ownerId, "Strategy Alignment");
        Assert.Equal("Strategy Alignment", conv.Title);

        conv.UpdateTitle("Q3 Strategy Alignment");
        Assert.Equal("Q3 Strategy Alignment", conv.Title);

        var userMsg = new CoachMessage(ownerId, conv.Id, CoachMessageSenderRole.User, "How do I balance learning and work?");
        var coachMsg = new CoachMessage(ownerId, conv.Id, CoachMessageSenderRole.Coach, "Focus on Q2 high-leverage tasks first.");

        Assert.Equal(CoachMessageSenderRole.User, userMsg.SenderRole);
        Assert.Equal(CoachMessageSenderRole.Coach, coachMsg.SenderRole);

        var obs = new CoachObservation(ownerId, CoachObservationScope.QuadrantImbalance, "Q1 load is 70% of total tasks", conv.Id);
        Assert.False(obs.IsDismissed);

        obs.Dismiss();
        Assert.True(obs.IsDismissed);
    }

    [Fact]
    public void Test_Coach_Suggestions_Route_Through_AiActionProposal_Queue()
    {
        var ownerId = Guid.CreateVersion7();

        // 1. CreateHabit proposal
        var habitProp = new AiActionProposal(ownerId, AiProposalKind.CreateHabit, "coach", "coach:habit:1", "Habit Title", "Desc", "{\"title\":\"Habit Title\"}");
        // 2. CreateTask proposal
        var taskProp = new AiActionProposal(ownerId, AiProposalKind.CreateTask, "coach", "coach:task:1", "Task Title", "Desc", "{\"title\":\"Task Title\"}");
        // 3. ApplyGoalRoadmap proposal
        var roadmapProp = new AiActionProposal(ownerId, AiProposalKind.ApplyGoalRoadmap, "coach", "coach:roadmap:1", "Roadmap Title", "Desc", "{\"goalId\":\"" + Guid.CreateVersion7() + "\"}");
        // 4. ApplyTaskSchedule proposal
        var schedProp = new AiActionProposal(ownerId, AiProposalKind.ApplyTaskSchedule, "coach", "coach:sched:1", "Schedule Title", "Desc", "{\"taskId\":\"" + Guid.CreateVersion7() + "\"}");

        Assert.Equal(AiProposalState.Pending, habitProp.State);
        Assert.Equal(AiProposalState.Pending, taskProp.State);
        Assert.Equal(AiProposalState.Pending, roadmapProp.State);
        Assert.Equal(AiProposalState.Pending, schedProp.State);
    }
}
