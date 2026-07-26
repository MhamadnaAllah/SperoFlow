using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class TaskCreationTests
{
    [Fact]
    public void Constructor_persists_requested_initial_state()
    {
        var task = new TaskItem(
            Guid.CreateVersion7(),
            "Prepare the launch note",
            null,
            LifeArea.Work,
            sortOrder: 1_000,
            state: TaskState.InProgress);

        Assert.Equal(TaskState.InProgress, task.State);
        Assert.Null(task.CompletedAt);
        Assert.Equal(1_000, task.SortOrder);
    }

    [Fact]
    public void Constructor_sets_completion_timestamp_for_completed_task()
    {
        var task = new TaskItem(
            Guid.CreateVersion7(),
            "Confirm launch readiness",
            null,
            LifeArea.Work,
            state: TaskState.Completed);

        Assert.Equal(TaskState.Completed, task.State);
        Assert.NotNull(task.CompletedAt);
    }
}
