using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class GoalAndRoadmapTests
{
    [Fact]
    public void Goal_tracks_a_role_and_rejects_invalid_sort_order()
    {
        var roleId = Guid.CreateVersion7();
        var goal = new Goal(
            Guid.CreateVersion7(),
            "Complete the data engineering path",
            "Build practical skill through milestones.",
            LifeArea.Learning,
            DateTimeOffset.UtcNow.AddMonths(6),
            1_000,
            roleId);

        Assert.Equal(roleId, goal.RoleId);
        Assert.Equal(GoalState.Active, goal.State);
        Assert.Throws<DomainValidationException>(() => new Goal(
            Guid.CreateVersion7(),
            "Invalid goal",
            null,
            LifeArea.Work,
            null,
            -1));
    }

    [Fact]
    public void Milestone_lifecycle_tracks_completion_and_rejects_archived_completion()
    {
        var milestone = new GoalMilestone(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Learn the foundations",
            null,
            12.5m,
            1_000);

        milestone.Complete();

        Assert.Equal(GoalMilestoneState.Completed, milestone.State);
        Assert.NotNull(milestone.CompletedAt);

        milestone.Archive();

        Assert.Throws<DomainValidationException>(() => milestone.Complete());
    }

    [Fact]
    public void Task_can_link_to_a_goal_and_accept_an_approved_quadrant()
    {
        var goalId = Guid.CreateVersion7();
        var task = new TaskItem(
            Guid.CreateVersion7(),
            "Practice the concepts",
            null,
            LifeArea.Learning,
            goalId: goalId);
        var originalToken = task.ConcurrencyToken;

        task.SetQuadrant(EisenhowerQuadrant.Q2);

        Assert.Equal(goalId, task.GoalId);
        Assert.Equal(EisenhowerQuadrant.Q2, task.Quadrant);
        Assert.NotEqual(originalToken, task.ConcurrencyToken);
    }

    [Fact]
    public void Roadmap_proposal_requires_explicit_resolution()
    {
        var proposal = new GoalRoadmapProposal(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "protected-roadmap");

        proposal.Approve();

        Assert.Equal(GoalRoadmapProposalState.Approved, proposal.State);
        Assert.NotNull(proposal.ResolvedAt);
        Assert.Throws<DomainValidationException>(() => proposal.Cancel());
    }
}