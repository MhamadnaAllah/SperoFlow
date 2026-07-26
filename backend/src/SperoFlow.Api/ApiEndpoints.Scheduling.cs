using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Application;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static void MapTaskSchedulingProposals(RouteGroupBuilder api)
    {
        var scheduling = api.MapGroup("/ai/tasks");
        scheduling.MapPost("/{id:guid}/schedule", async (
            Guid id,
            CreateTaskScheduleProposalRequest request,
            AppDbContext db,
            IAiGateway gateway,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var task = await db.Tasks.SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.UserId,
                cancellationToken);
            if (task is null)
            {
                return Results.NotFound();
            }

            if (task.State is TaskState.Completed or TaskState.Cancelled)
            {
                return Results.Conflict(new { error = "Only active tasks can receive a schedule suggestion." });
            }

            if (request.DurationMinutes is < 5 or > 480)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["durationMinutes"] = ["Duration must be between 5 and 480 minutes."],
                });
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var targetDate = request.TargetDate ?? today.AddDays(1);
            if (targetDate < today)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["targetDate"] = ["Choose today or a future date for a schedule suggestion."],
                });
            }

            if (task.DueAt.HasValue && targetDate > DateOnly.FromDateTime(task.DueAt.Value.UtcDateTime))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["targetDate"] = ["Choose a date on or before the task due date."],
                });
            }

            var durationMinutes = request.DurationMinutes ?? Math.Clamp(task.EstimatedMinutes ?? 30, 5, 480);
            var sourceConcurrencyToken = task.ConcurrencyToken;
            var sourcePrefix = TaskScheduleSourcePrefix(task.Id, sourceConcurrencyToken);
            var existing = await db.AiActionProposals.AsNoTracking()
                .Where(proposal => proposal.OwnerId == currentUser.UserId
                    && proposal.Source == "scheduler"
                    && proposal.State == AiProposalState.Pending
                    && proposal.SourceKey.StartsWith(sourcePrefix))
                .OrderByDescending(proposal => proposal.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return Results.Ok(ToResponse(existing));
            }

            var snapshot = await BuildTaskScheduleSnapshotAsync(
                db,
                task,
                currentUser.UserId,
                targetDate,
                durationMinutes,
                today,
                cancellationToken);
            try
            {
                using var response = await gateway.InvokeAsync(
                    "/api/schedule/propose",
                    snapshot,
                    currentUser.UserId,
                    "ai.invoke",
                    cancellationToken);
                if (!TryParseScheduleDecision(
                    response.RootElement,
                    targetDate,
                    task.DueAt,
                    out var decision,
                    out var error,
                    out var noAvailableSlot))
                {
                    return noAvailableSlot
                        ? Results.Conflict(new { error })
                        : Results.Problem(title: "The scheduling service returned an invalid suggestion.", statusCode: StatusCodes.Status502BadGateway);
                }

                await db.Entry(task).ReloadAsync(cancellationToken);
                if (task.ConcurrencyToken != sourceConcurrencyToken
                    || task.State is TaskState.Completed or TaskState.Cancelled)
                {
                    return Results.Conflict(new { error = "The task changed while its schedule was being prepared. Refresh and retry." });
                }

                if (await HasSchedulingConflictAsync(
                    db,
                    currentUser.UserId,
                    task.Id,
                    decision.StartAt,
                    decision.EndAt,
                    cancellationToken))
                {
                    return Results.Conflict(new { error = "The suggested time is no longer available. Refresh and request another suggestion." });
                }

                existing = await db.AiActionProposals.AsNoTracking()
                    .Where(proposal => proposal.OwnerId == currentUser.UserId
                        && proposal.Source == "scheduler"
                        && proposal.State == AiProposalState.Pending
                        && proposal.SourceKey.StartsWith(sourcePrefix))
                    .OrderByDescending(proposal => proposal.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                if (existing is not null)
                {
                    return Results.Ok(ToResponse(existing));
                }

                var proposal = new AiActionProposal(
                    currentUser.UserId,
                    AiProposalKind.ApplyTaskSchedule,
                    "scheduler",
                    TaskScheduleSourceKey(task.Id, sourceConcurrencyToken),
                    "Schedule: " + task.Title,
                    decision.Reason,
                    JsonSerializer.Serialize(
                        new TaskScheduleProposalPayload(
                            task.Id,
                            sourceConcurrencyToken,
                            decision.StartAt,
                            decision.EndAt,
                            decision.DurationMinutes,
                            targetDate),
                        JsonOptions));
                db.AiActionProposals.Add(proposal);
                AddAudit(db, currentUser.UserId, "scheduler", "proposal_created", "ai_action_proposal", proposal.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToResponse(proposal));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "The scheduling service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }

    private static async Task<Dictionary<string, object?>> BuildTaskScheduleSnapshotAsync(
        AppDbContext db,
        TaskItem task,
        Guid ownerId,
        DateOnly targetDate,
        int durationMinutes,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var dayEnd = dayStart.AddDays(1);
        var calendarEvents = await db.CalendarEvents.AsNoTracking()
            .Where(value => value.OwnerId == ownerId && value.StartsAt < dayEnd && value.EndsAt > dayStart)
            .OrderBy(value => value.StartsAt)
            .Select(value => new { value.Title, value.StartsAt, value.EndsAt })
            .ToListAsync(cancellationToken);
        var scheduledTasks = await db.Tasks.AsNoTracking()
            .Where(value => value.OwnerId == ownerId
                && value.Id != task.Id
                && value.StartAt.HasValue
                && value.StartAt.Value < dayEnd
                && value.State != TaskState.Completed
                && value.State != TaskState.Cancelled)
            .OrderBy(value => value.StartAt)
            .Select(value => new { value.Title, value.StartAt, value.EstimatedMinutes })
            .ToListAsync(cancellationToken);
        var matrixTasks = await db.Tasks.AsNoTracking()
            .Where(value => value.OwnerId == ownerId && value.State != TaskState.Cancelled)
            .Select(value => new { value.Quadrant, value.State })
            .ToListAsync(cancellationToken);
        var roleRows = await db.Tasks.AsNoTracking()
            .Where(value => value.OwnerId == ownerId
                && value.State != TaskState.Completed
                && value.State != TaskState.Cancelled)
            .GroupBy(value => value.LifeArea)
            .Select(group => new { LifeArea = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var matrixLoad = new Dictionary<string, Dictionary<string, int>>();
        foreach (var quadrant in new[] { EisenhowerQuadrant.Q1, EisenhowerQuadrant.Q2, EisenhowerQuadrant.Q3, EisenhowerQuadrant.Q4 })
        {
            var values = matrixTasks.Where(value => value.Quadrant == quadrant).ToArray();
            matrixLoad[quadrant.ToString()] = new Dictionary<string, int>
            {
                ["task_count"] = values.Length,
                ["completed"] = values.Count(value => value.State == TaskState.Completed),
                ["pending"] = values.Count(value => value.State is TaskState.Todo or TaskState.InProgress),
            };
        }

        var scheduledBlocks = scheduledTasks
            .Where(value => value.StartAt.HasValue)
            .Select(value => new
            {
                title = value.Title,
                start_time = value.StartAt!.Value,
                end_time = value.StartAt!.Value.AddMinutes(Math.Clamp(value.EstimatedMinutes ?? 30, 5, 480)),
                source = "task",
            })
            .Where(value => value.end_time > dayStart)
            .ToArray();
        var totalRoleTasks = roleRows.Sum(value => value.Count);

        return new Dictionary<string, object?>
        {
            ["task"] = new
            {
                title = task.Title,
                description = task.Description ?? string.Empty,
                duration_minutes = durationMinutes,
                source = task.Quadrant == EisenhowerQuadrant.Q1 ? "urgent" : "task",
                role_category = task.LifeArea.ToString().ToLowerInvariant(),
                target_date = targetDate,
                not_before = targetDate == today ? (DateTimeOffset?)DateTimeOffset.UtcNow : null,
            },
            ["calendar_events"] = calendarEvents.Select(value => new
            {
                title = value.Title,
                start_time = value.StartsAt,
                end_time = value.EndsAt,
                source = "calendar",
            }).ToArray(),
            ["scheduled_tasks"] = scheduledBlocks,
            ["matrix_load"] = matrixLoad,
            ["stress_level"] = "normal",
            ["role_distribution"] = roleRows.Select(value => new
            {
                role_category = value.LifeArea.ToString().ToLowerInvariant(),
                percentage = totalRoleTasks == 0 ? 0 : Math.Round(value.Count * 100d / totalRoleTasks, 1),
            }).ToArray(),
        };
    }

    private static bool TryParseScheduleDecision(
        JsonElement response,
        DateOnly targetDate,
        DateTimeOffset? dueAt,
        out TaskScheduleDecision decision,
        out string error,
        out bool noAvailableSlot)
    {
        decision = default!;
        error = "The scheduling service returned an invalid suggestion.";
        noAvailableSlot = false;
        if (response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("status", out var statusValue)
            || statusValue.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var reason = ClampScheduleText(GetScheduleText(response, "reason"), 600)
            ?? "No conflict-free time is available in the selected window.";
        var status = statusValue.GetString();
        if (string.Equals(status, "no_available_slot", StringComparison.Ordinal))
        {
            error = reason;
            noAvailableSlot = true;
            return false;
        }

        if (!string.Equals(status, "success", StringComparison.Ordinal)
            || !response.TryGetProperty("suggestedSlot", out var slot)
            || slot.ValueKind != JsonValueKind.Object
            || !TryGetDateTimeOffset(slot, "startTime", out var startAt)
            || !TryGetDateTimeOffset(slot, "endTime", out var endAt)
            || !slot.TryGetProperty("durationMinutes", out var durationValue)
            || !durationValue.TryGetInt32(out var durationMinutes)
            || durationMinutes is < 5 or > 480
            || endAt != startAt.AddMinutes(durationMinutes)
            || DateOnly.FromDateTime(startAt.Date) != targetDate
            || startAt < DateTimeOffset.UtcNow
            || (dueAt.HasValue && endAt > dueAt.Value))
        {
            return false;
        }

        decision = new TaskScheduleDecision(startAt, endAt, durationMinutes, reason);
        return true;
    }

    private static bool TryGetDateTimeOffset(JsonElement value, string propertyName, out DateTimeOffset parsed)
    {
        parsed = default;
        return value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                property.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out parsed);
    }

    private static string? GetScheduleText(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;

    private static string? ClampScheduleText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static async Task<bool> HasSchedulingConflictAsync(
        AppDbContext db,
        Guid ownerId,
        Guid taskId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        CancellationToken cancellationToken)
    {
        if (await db.CalendarEvents.AsNoTracking().AnyAsync(
            value => value.OwnerId == ownerId && value.StartsAt < endAt && value.EndsAt > startAt,
            cancellationToken))
        {
            return true;
        }

        var scheduledTasks = await db.Tasks.AsNoTracking()
            .Where(value => value.OwnerId == ownerId
                && value.Id != taskId
                && value.StartAt.HasValue
                && value.StartAt.Value < endAt
                && value.State != TaskState.Completed
                && value.State != TaskState.Cancelled)
            .Select(value => new { value.StartAt, value.EstimatedMinutes })
            .ToListAsync(cancellationToken);
        return scheduledTasks.Any(value => value.StartAt!.Value.AddMinutes(Math.Clamp(value.EstimatedMinutes ?? 30, 5, 480)) > startAt);
    }

    private static async Task CancelPendingTaskScheduleProposalsAsync(
        AppDbContext db,
        Guid ownerId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var sourcePrefix = "scheduler:" + taskId.ToString("N") + ":";
        var pending = await db.AiActionProposals
            .Where(proposal => proposal.OwnerId == ownerId
                && proposal.Source == "scheduler"
                && proposal.State == AiProposalState.Pending
                && proposal.SourceKey.StartsWith(sourcePrefix))
            .ToListAsync(cancellationToken);
        foreach (var proposal in pending)
        {
            proposal.Cancel();
            AddAudit(db, ownerId, "scheduler", "proposal_invalidated", "ai_action_proposal", proposal.Id);
        }
    }

    private static string TaskScheduleSourcePrefix(Guid taskId, Guid concurrencyToken) =>
        "scheduler:" + taskId.ToString("N") + ":" + concurrencyToken.ToString("N") + ":";

    private static string TaskScheduleSourceKey(Guid taskId, Guid concurrencyToken) =>
        TaskScheduleSourcePrefix(taskId, concurrencyToken) + Guid.CreateVersion7().ToString("N");

    private static bool IsTaskScheduleSourceKey(string sourceKey, Guid taskId, Guid concurrencyToken) =>
        sourceKey.StartsWith(TaskScheduleSourcePrefix(taskId, concurrencyToken), StringComparison.Ordinal);

    private sealed record TaskScheduleDecision(
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        int DurationMinutes,
        string Reason);

    private sealed record TaskScheduleProposalPayload(
        Guid TaskId,
        Guid SourceConcurrencyToken,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        int DurationMinutes,
        DateOnly TargetDate);
}