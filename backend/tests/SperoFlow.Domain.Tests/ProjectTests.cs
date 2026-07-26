using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class ProjectTests
{
    [Fact]
    public void Constructor_rejects_target_before_start()
    {
        var start = DateTimeOffset.UtcNow.AddDays(2);
        var target = start.AddDays(-1);

        Assert.Throws<DomainValidationException>(() => new Project(
            Guid.CreateVersion7(),
            "Launch plan",
            null,
            "#0053dc",
            "rocket_launch",
            start,
            target));
    }

    [Fact]
    public void Archive_changes_state_and_rotates_concurrency_token()
    {
        var project = new Project(
            Guid.CreateVersion7(),
            "Launch plan",
            null,
            "#0053dc",
            "rocket_launch",
            null,
            null);
        var originalToken = project.ConcurrencyToken;

        project.Archive();

        Assert.Equal(ProjectState.Archived, project.State);
        Assert.NotEqual(originalToken, project.ConcurrencyToken);
    }

    [Fact]
    public void Task_update_can_link_project_and_set_in_progress_state()
    {
        var ownerId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        var start = DateTimeOffset.UtcNow.AddDays(1);
        var due = start.AddDays(3);
        var task = new TaskItem(ownerId, "Draft brief", null, LifeArea.Work);

        task.Update(
            "Draft brief",
            "Initial version",
            LifeArea.Work,
            EisenhowerQuadrant.Q2,
            TaskState.InProgress,
            start,
            due,
            90,
            null,
            projectId,
            1_000);

        Assert.Equal(projectId, task.ProjectId);
        Assert.Equal(TaskState.InProgress, task.State);
        Assert.Equal(start, task.StartAt);
        Assert.Equal(due, task.DueAt);
        Assert.Equal(1_000, task.SortOrder);
        Assert.Null(task.CompletedAt);
    }
}
