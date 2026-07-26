using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class LifeRoleAndAiProposalTests
{
    [Fact]
    public void Core_role_cannot_be_archived()
    {
        var role = new LifeRole(
            Guid.CreateVersion7(),
            "Physical",
            LifeRoleCategory.Internal,
            LifeArea.Physical,
            "#dc2626",
            "fitness_center",
            systemKey: "physical");

        Assert.Throws<DomainValidationException>(() => role.Archive());
    }

    [Fact]
    public void Proposal_approval_is_single_use_and_tracks_the_applied_entity()
    {
        var proposal = new AiActionProposal(
            Guid.CreateVersion7(),
            AiProposalKind.CreateTask,
            "balance",
            "balance:test",
            "Take a brief movement break",
            "Choose a short walk that fits your day.",
            "{\"title\":\"Take a brief movement break\"}");
        var originalToken = proposal.ConcurrencyToken;
        var taskId = Guid.CreateVersion7();

        proposal.Approve(taskId);

        Assert.Equal(AiProposalState.Approved, proposal.State);
        Assert.Equal(taskId, proposal.AppliedEntityId);
        Assert.NotNull(proposal.ResolvedAt);
        Assert.NotEqual(originalToken, proposal.ConcurrencyToken);
        Assert.Throws<DomainValidationException>(() => proposal.Approve(Guid.CreateVersion7()));
    }

    [Fact]
    public void Task_can_link_to_a_life_role()
    {
        var roleId = Guid.CreateVersion7();
        var task = new TaskItem(
            Guid.CreateVersion7(),
            "Prepare for the family check-in",
            null,
            LifeArea.Family,
            roleId: roleId);

        Assert.Equal(roleId, task.RoleId);
    }
}