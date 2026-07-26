using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class JournalInsightTests
{
    [Fact]
    public void Insight_requires_explicit_approval_and_tracks_its_source_revision()
    {
        var ownerId = Guid.CreateVersion7();
        var journalEntryId = Guid.CreateVersion7();
        var sourceRevision = Guid.CreateVersion7();
        var insight = new JournalInsight(
            ownerId,
            journalEntryId,
            sourceRevision,
            "protected-payload");
        var originalToken = insight.ConcurrencyToken;

        insight.Approve();

        Assert.Equal(JournalInsightState.Approved, insight.State);
        Assert.Equal(journalEntryId, insight.JournalEntryId);
        Assert.Equal(sourceRevision, insight.SourceConcurrencyToken);
        Assert.NotNull(insight.ResolvedAt);
        Assert.NotEqual(originalToken, insight.ConcurrencyToken);
        Assert.Throws<DomainValidationException>(() => insight.Cancel());
    }

    [Fact]
    public void Insight_cannot_be_created_without_a_source_revision()
    {
        Assert.Throws<DomainValidationException>(() => new JournalInsight(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.Empty,
            "protected-payload"));
    }
}