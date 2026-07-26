using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class DomainModelTests
{
    [Fact]
    public void Complete_sets_completion_timestamp_and_rotates_concurrency_token()
    {
        var task = new TaskItem(
            Guid.CreateVersion7(),
            "Prepare quarterly review",
            null,
            LifeArea.Work,
            EisenhowerQuadrant.Q2);
        var originalToken = task.ConcurrencyToken;

        task.Complete();

        Assert.Equal(TaskState.Completed, task.State);
        Assert.NotNull(task.CompletedAt);
        Assert.NotEqual(originalToken, task.ConcurrencyToken);
    }

    [Fact]
    public void Constructor_rejects_an_invalid_estimate()
    {
        Assert.Throws<DomainValidationException>(() => new TaskItem(
            Guid.CreateVersion7(),
            "Plan a release",
            null,
            LifeArea.Work,
            EisenhowerQuadrant.Q1,
            null,
            1_441));
    }

    [Fact]
    public void Calendar_event_rejects_an_invalid_time_range()
    {
        var start = DateTimeOffset.UtcNow;

        Assert.Throws<DomainValidationException>(() => new CalendarEvent(
            Guid.CreateVersion7(),
            "Conflicting event",
            start,
            start,
            "indigo",
            null));
    }
}
