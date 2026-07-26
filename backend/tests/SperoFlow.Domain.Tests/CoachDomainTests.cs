using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class CoachDomainTests
{
    [Fact]
    public void Coach_conversation_creation_and_title_update()
    {
        var ownerId = Guid.CreateVersion7();
        var conv = new CoachConversation(ownerId, "Weekly Q2 Alignment");

        Assert.Equal(ownerId, conv.OwnerId);
        Assert.Equal("Weekly Q2 Alignment", conv.Title);
        Assert.False(conv.IsArchived);

        conv.UpdateTitle("Updated Alignment Thread");
        Assert.Equal("Updated Alignment Thread", conv.Title);

        conv.Archive();
        Assert.True(conv.IsArchived);

        conv.Restore();
        Assert.False(conv.IsArchived);
    }

    [Fact]
    public void Coach_message_validates_content_and_roles()
    {
        var ownerId = Guid.CreateVersion7();
        var convId = Guid.CreateVersion7();

        var msg = new CoachMessage(ownerId, convId, CoachMessageSenderRole.User, "How do I balance Q2 work?");
        Assert.Equal(ownerId, msg.OwnerId);
        Assert.Equal(convId, msg.ConversationId);
        Assert.Equal(CoachMessageSenderRole.User, msg.SenderRole);
        Assert.Equal("How do I balance Q2 work?", msg.ProtectedContent);

        Assert.Throws<DomainValidationException>(() => new CoachMessage(ownerId, Guid.Empty, CoachMessageSenderRole.Coach, "Response"));
        Assert.Throws<DomainValidationException>(() => new CoachMessage(ownerId, convId, CoachMessageSenderRole.Coach, ""));
    }

    [Fact]
    public void Coach_observation_creation_and_dismissal()
    {
        var ownerId = Guid.CreateVersion7();
        var convId = Guid.CreateVersion7();

        var obs = new CoachObservation(ownerId, CoachObservationScope.HabitPattern, "Consistent morning routine", convId);
        Assert.Equal(ownerId, obs.OwnerId);
        Assert.Equal(CoachObservationScope.HabitPattern, obs.Scope);
        Assert.Equal("Consistent morning routine", obs.ProtectedContent);
        Assert.Equal(convId, obs.ConversationId);
        Assert.False(obs.IsDismissed);

        obs.Dismiss();
        Assert.True(obs.IsDismissed);
    }

    [Fact]
    public void Create_habit_proposal_state_transitions()
    {
        var ownerId = Guid.CreateVersion7();
        var proposal = new AiActionProposal(
            ownerId,
            AiProposalKind.CreateHabit,
            "coach",
            "coach:habit:1",
            "Daily Focus Habit",
            "Dedicate 15 minutes to Q2 planning daily.",
            "{\"title\":\"Daily Focus Habit\",\"lifeArea\":\"Personal\",\"targetPerWeek\":5}");

        Assert.Equal(AiProposalState.Pending, proposal.State);

        var habitId = Guid.CreateVersion7();
        proposal.Approve(habitId);

        Assert.Equal(AiProposalState.Approved, proposal.State);
        Assert.Equal(habitId, proposal.AppliedEntityId);
    }
}
