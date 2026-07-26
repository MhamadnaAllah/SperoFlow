using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class BoundaryAndEdgeCaseTests
{
    [Fact]
    public void Test_Concurrent_Proposal_Approvals_Throws_On_Second_Attempt()
    {
        var ownerId = Guid.CreateVersion7();
        var proposal = new AiActionProposal(
            ownerId,
            AiProposalKind.CreateTask,
            "coach",
            "key-1",
            "Concurrent Task",
            "Description",
            "{\"title\":\"Concurrent Task\"}");

        var entityId1 = Guid.CreateVersion7();
        var entityId2 = Guid.CreateVersion7();

        proposal.Approve(entityId1);
        Assert.Equal(AiProposalState.Approved, proposal.State);
        Assert.Equal(entityId1, proposal.AppliedEntityId);

        // Second approval attempt MUST throw exception
        Assert.Throws<DomainValidationException>(() => proposal.Approve(entityId2));
    }

    [Fact]
    public void Test_Stale_Payloads_And_Mismatched_Tokens()
    {
        var ownerId = Guid.CreateVersion7();
        var journalEntry = new JournalEntry(ownerId, "Original journal text", "Good");
        var originalToken = journalEntry.ConcurrencyToken;

        var insight = new JournalInsight(
            ownerId,
            journalEntry.Id,
            originalToken,
            "Insight based on original text");

        // Simulate entry modification
        journalEntry.ReplaceContent("Updated journal text", "Great");
        var updatedToken = journalEntry.ConcurrencyToken;

        Assert.NotEqual(originalToken, updatedToken);
        Assert.Equal(originalToken, insight.SourceConcurrencyToken);
        // The pinned token no longer matches updatedToken
    }

    [Fact]
    public void Test_Expired_And_Empty_Sessions_Owner_Validation()
    {
        Assert.Throws<DomainValidationException>(() => new Goal(
            Guid.Empty,
            "Title",
            "Description",
            LifeArea.Learning,
            null));

        Assert.Throws<DomainValidationException>(() => new Habit(
            Guid.Empty,
            "Title",
            "Description",
            LifeArea.Physical,
            3));

        Assert.Throws<DomainValidationException>(() => new JournalEntry(
            Guid.Empty,
            "Protected content"));
    }

    [Fact]
    public void Test_Orphaned_Proposals_Rejection()
    {
        var ownerId = Guid.CreateVersion7();

        // Milestone requires non-empty goal ID
        Assert.Throws<DomainValidationException>(() => new GoalMilestone(
            ownerId,
            Guid.Empty,
            "Orphaned Milestone",
            "Description",
            2.5m,
            1));

        // Journal insight requires non-empty entry ID
        Assert.Throws<DomainValidationException>(() => new JournalInsight(
            ownerId,
            Guid.Empty,
            Guid.CreateVersion7(),
            "Orphaned Insight Payload"));

        // Role discovery finding requires non-empty proposal ID
        Assert.Throws<DomainValidationException>(() => new RoleDiscoveryFinding(
            ownerId,
            Guid.Empty,
            "Orphaned Finding Evidence"));
    }

    [Fact]
    public void Test_Unicode_RTL_And_Special_Characters()
    {
        var ownerId = Guid.CreateVersion7();

        // Arabic text
        var arabicTask = new TaskItem(
            ownerId,
            "مهمة جديدة لليوم",
            "تخطيط المهام الأسبوعية بشكل متوازن",
            LifeArea.Spiritual,
            EisenhowerQuadrant.Q2);

        Assert.Equal("مهمة جديدة لليوم", arabicTask.Title);
        Assert.Equal("تخطيط المهام الأسبوعية بشكل متوازن", arabicTask.Description);

        // Hebrew text
        var hebrewRole = new LifeRole(
            ownerId,
            "תפקיד חדש",
            LifeRoleCategory.External,
            LifeArea.Social,
            "#10b981",
            "group");

        Assert.Equal("תפקיד חדש", hebrewRole.Name);

        // Chinese text & Emojis
        var emojiTask = new TaskItem(
            ownerId,
            "🎯 Weekly Sprint Review 🚀 学习 Roadmap",
            "Completing GraphRAG pipeline verification ✨",
            LifeArea.Learning);

        Assert.Equal("🎯 Weekly Sprint Review 🚀 学习 Roadmap", emojiTask.Title);
        Assert.Equal("Completing GraphRAG pipeline verification ✨", emojiTask.Description);
    }

    [Fact]
    public void Test_Task_Schedule_Boundary_Limits()
    {
        var ownerId = Guid.CreateVersion7();
        var startAt = DateTimeOffset.UtcNow.AddDays(1);
        var task = new TaskItem(
            ownerId,
            "Boundary Schedule Task",
            "Testing limits",
            LifeArea.Work,
            dueAt: startAt.AddMinutes(60));

        // Valid boundary: 5 minutes
        task.Schedule(startAt, 5);
        Assert.Equal(5, task.EstimatedMinutes);

        // Valid boundary: 60 minutes (dueAt is startAt + 60 min)
        task.Schedule(startAt, 60);
        Assert.Equal(60, task.EstimatedMinutes);

        // Reject < 5 minutes
        Assert.Throws<DomainValidationException>(() => task.Schedule(startAt, 4));

        // Reject > 480 minutes
        Assert.Throws<DomainValidationException>(() => task.Schedule(startAt, 481));

        // Reject duration exceeding dueAt
        Assert.Throws<DomainValidationException>(() => task.Schedule(startAt, 61));
    }

    [Fact]
    public void Test_GoalMilestone_EstimatedHours_Boundaries()
    {
        var ownerId = Guid.CreateVersion7();
        var goalId = Guid.CreateVersion7();

        // Min boundary: 0
        var m1 = new GoalMilestone(ownerId, goalId, "Zero hours milestone", null, 0m, 0);
        Assert.Equal(0m, m1.EstimatedHours);

        // Max boundary: 10,000
        var m2 = new GoalMilestone(ownerId, goalId, "Max hours milestone", null, 10_000m, 1);
        Assert.Equal(10_000m, m2.EstimatedHours);

        // Invalid < 0
        Assert.Throws<DomainValidationException>(() => new GoalMilestone(ownerId, goalId, "Invalid milestone", null, -0.5m, 2));

        // Invalid > 10,000
        Assert.Throws<DomainValidationException>(() => new GoalMilestone(ownerId, goalId, "Invalid milestone", null, 10_000.5m, 3));
    }
}
