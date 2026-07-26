using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Application;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static void MapEisenhowerProposals(RouteGroupBuilder api)
    {
        var matrix = api.MapGroup("/ai/tasks");

        matrix.MapPost("/{id:guid}/classify", async (
            Guid id,
            AppDbContext db,
            IAiGateway gateway,
            ICurrentUser currentUser,
            IContentProtector protector,
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
                return Results.Conflict(new { error = "Only active tasks can receive a priority suggestion." });
            }

            var sourcePrefix = TaskClassificationSourcePrefix(task.Id, task.ConcurrencyToken);
            var existing = await db.AiActionProposals.AsNoTracking()
                .Where(proposal => proposal.OwnerId == currentUser.UserId
                    && proposal.State == AiProposalState.Pending
                    && proposal.Source == "eisenhower"
                    && proposal.SourceKey.StartsWith(sourcePrefix))
                .OrderByDescending(proposal => proposal.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return Results.Ok(ToResponse(existing));
            }

            var payload = await BuildTaskClassificationSnapshotAsync(
                db,
                task,
                currentUser.UserId,
                protector,
                cancellationToken);
            try
            {
                using var response = await gateway.InvokeAsync(
                    "/api/matrix/predict-quadrant",
                    payload,
                    currentUser.UserId,
                    "ai.invoke",
                    cancellationToken);
                if (!TryParseTaskClassification(response.RootElement, out var quadrant, out var confidence, out var rationale))
                {
                    return Results.Problem(title: "The Eisenhower service returned an invalid classification.", statusCode: StatusCodes.Status502BadGateway);
                }

                var proposal = new AiActionProposal(
                    currentUser.UserId,
                    AiProposalKind.ApplyTaskClassification,
                    "eisenhower",
                    TaskClassificationSourceKey(task.Id, task.ConcurrencyToken),
                    "Prioritize: " + task.Title,
                    rationale,
                    JsonSerializer.Serialize(
                        new TaskClassificationProposalPayload(task.Id, task.ConcurrencyToken, quadrant),
                        JsonOptions));
                db.AiActionProposals.Add(proposal);
                AddAudit(db, currentUser.UserId, "eisenhower", "proposal_created", "ai_action_proposal", proposal.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToResponse(proposal));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "The Eisenhower service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }

    private static async Task<Dictionary<string, object?>> BuildTaskClassificationSnapshotAsync(
        AppDbContext db,
        TaskItem task,
        Guid ownerId,
        IContentProtector protector,
        CancellationToken cancellationToken)
    {
        var goals = await db.Goals.AsNoTracking()
            .Where(goal => goal.OwnerId == ownerId && goal.State == GoalState.Active)
            .OrderBy(goal => goal.TargetAt)
            .ThenBy(goal => goal.SortOrder)
            .Take(12)
            .Select(goal => new { goal.Title, goal.Description, goal.LifeArea, goal.TargetAt })
            .ToListAsync(cancellationToken);
        var journals = await db.JournalEntries.AsNoTracking()
            .Where(entry => entry.OwnerId == ownerId)
            .OrderByDescending(entry => entry.UpdatedAt)
            .Take(3)
            .ToListAsync(cancellationToken);
        var insights = await db.JournalInsights.AsNoTracking()
            .Where(insight => insight.OwnerId == ownerId && insight.State == JournalInsightState.Approved)
            .OrderByDescending(insight => insight.CreatedAt)
            .Take(4)
            .ToListAsync(cancellationToken);

        var journalContext = new List<object>();
        foreach (var journal in journals)
        {
            var content = ClampEisenhowerText(protector.Unprotect(ownerId, journal.ProtectedContent), 1_200);
            if (!string.IsNullOrWhiteSpace(content))
            {
                journalContext.Add(new { content, mood = journal.Mood });
            }
        }

        var insightContext = new List<object>();
        foreach (var insight in insights)
        {
            try
            {
                using var payload = JsonDocument.Parse(protector.Unprotect(ownerId, insight.ProtectedPayload));
                var feedback = ClampEisenhowerText(GetText(payload.RootElement, "feedback"), 600);
                var progressSummary = ClampEisenhowerText(GetText(payload.RootElement, "progressSummary"), 600);
                if (!string.IsNullOrWhiteSpace(feedback) && !string.IsNullOrWhiteSpace(progressSummary))
                {
                    insightContext.Add(new { feedback, progress_summary = progressSummary });
                }
            }
            catch (JsonException)
            {
                // A malformed historical insight is not needed to classify this task.
            }
        }

        return new Dictionary<string, object?>
        {
            ["task"] = new
            {
                title = task.Title,
                description = task.Description ?? string.Empty,
                life_area = task.LifeArea.ToString().ToLowerInvariant(),
                due_at = task.DueAt,
                estimated_minutes = task.EstimatedMinutes,
            },
            ["goals"] = goals.Select(goal => new
            {
                title = goal.Title,
                description = ClampEisenhowerText(goal.Description, 1_000) ?? string.Empty,
                life_area = goal.LifeArea.ToString().ToLowerInvariant(),
                target_at = goal.TargetAt,
            }).ToArray(),
            ["journals"] = journalContext,
            ["insights"] = insightContext,
        };
    }

    private static bool TryParseTaskClassification(
        JsonElement response,
        out EisenhowerQuadrant quadrant,
        out double confidence,
        out string rationale)
    {
        quadrant = EisenhowerQuadrant.Unsorted;
        confidence = 0;
        rationale = string.Empty;
        if (response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("suggestedQuadrant", out var quadrantValue)
            || quadrantValue.ValueKind != JsonValueKind.String
            || !Enum.TryParse<EisenhowerQuadrant>(quadrantValue.GetString(), ignoreCase: true, out quadrant)
            || quadrant == EisenhowerQuadrant.Unsorted
            || !response.TryGetProperty("confidence", out var confidenceValue)
            || !confidenceValue.TryGetDouble(out confidence)
            || confidence is < 0.40 or > 1.0)
        {
            return false;
        }

        rationale = ClampEisenhowerText(GetText(response, "rationale"), 600) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(rationale);
    }

    private static async Task CancelPendingTaskClassificationProposalsAsync(
        AppDbContext db,
        Guid ownerId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var sourcePrefix = "eisenhower:" + taskId.ToString("N") + ":";
        var pending = await db.AiActionProposals
            .Where(proposal => proposal.OwnerId == ownerId
                && proposal.Source == "eisenhower"
                && proposal.State == AiProposalState.Pending
                && proposal.SourceKey.StartsWith(sourcePrefix))
            .ToListAsync(cancellationToken);
        foreach (var proposal in pending)
        {
            proposal.Cancel();
            AddAudit(db, ownerId, "eisenhower", "proposal_invalidated", "ai_action_proposal", proposal.Id);
        }
    }

    private static string TaskClassificationSourcePrefix(Guid taskId, Guid concurrencyToken) =>
        "eisenhower:" + taskId.ToString("N") + ":" + concurrencyToken.ToString("N") + ":";

    private static string TaskClassificationSourceKey(Guid taskId, Guid concurrencyToken) =>
        TaskClassificationSourcePrefix(taskId, concurrencyToken) + Guid.CreateVersion7().ToString("N");

    private static bool IsTaskClassificationSourceKey(string sourceKey, Guid taskId, Guid concurrencyToken) =>
        sourceKey.StartsWith(TaskClassificationSourcePrefix(taskId, concurrencyToken), StringComparison.Ordinal);

    private static string? ClampEisenhowerText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private sealed record TaskClassificationProposalPayload(
        Guid TaskId,
        Guid SourceConcurrencyToken,
        EisenhowerQuadrant Quadrant);
}