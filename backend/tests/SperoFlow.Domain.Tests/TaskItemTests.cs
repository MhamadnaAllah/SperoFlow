using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class TaskItemTests
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
    public void Constructor_rejects_a_reminder_after_the_due_date()
    {
        var dueAt = DateTimeOffset.UtcNow.AddHours(1);

        Assert.Throws<DomainValidationException>(() => new TaskItem(
            Guid.CreateVersion7(),
            "Plan a release",
            null,
            LifeArea.Work,
            EisenhowerQuadrant.Q1,
            dueAt,
            30));
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

    [Fact]
    public void Schedule_sets_the_focus_block_and_rotates_the_concurrency_token()
    {
        var startAt = DateTimeOffset.UtcNow.AddDays(1);
        var task = new TaskItem(
            Guid.CreateVersion7(),
            "Write the proposal",
            null,
            LifeArea.Work,
            EisenhowerQuadrant.Q2,
            dueAt: startAt.AddHours(2));
        var originalToken = task.ConcurrencyToken;

        task.Schedule(startAt, 45);

        Assert.Equal(startAt, task.StartAt);
        Assert.Equal(45, task.EstimatedMinutes);
        Assert.NotEqual(originalToken, task.ConcurrencyToken);
    }

    [Fact]
    public void Schedule_rejects_a_block_that_ends_after_the_due_date()
    {
        var startAt = DateTimeOffset.UtcNow.AddDays(1);
        var task = new TaskItem(
            Guid.CreateVersion7(),
            "Write the proposal",
            null,
            LifeArea.Work,
            EisenhowerQuadrant.Q2,
            dueAt: startAt.AddMinutes(30));

        Assert.Throws<DomainValidationException>(() => task.Schedule(startAt, 45));
    }
}
