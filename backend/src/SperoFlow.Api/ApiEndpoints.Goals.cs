using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Application;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static void MapGoals(RouteGroupBuilder api)
    {
        var goals = api.MapGroup("/goals");

        goals.MapGet("", async (
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken,
            bool includeArchived = false) =>
        {
            var query = db.Goals.AsNoTracking().Where(goal => goal.OwnerId == currentUser.UserId);
            if (!includeArchived)
            {
                query = query.Where(goal => goal.State != GoalState.Archived);
            }

            var values = await query
                .OrderBy(goal => goal.State)
                .ThenBy(goal => goal.SortOrder)
                .ThenBy(goal => goal.TargetAt)
                .ThenBy(goal => goal.Title)
                .ToListAsync(cancellationToken);
            return Results.Ok(await ToGoalResponsesAsync(values, db, cancellationToken));
        });

        goals.MapGet("/{id:guid}", async (
            Guid id,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var goal = await db.Goals.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.UserId,
                cancellationToken);
            if (goal is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(await ToGoalResponseAsync(goal, db, cancellationToken));
        });

        goals.MapPost("", async (
            CreateGoalRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            if (!await IsOwnedActiveRoleAsync(db, currentUser.UserId, request.RoleId, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["roleId"] = ["The selected role does not exist or is archived."],
                });
            }

            try
            {
                var sortOrder = request.SortOrder == 0
                    ? await GetNextGoalSortOrderAsync(db, currentUser.UserId, cancellationToken)
                    : request.SortOrder;
                var goal = new Goal(
                    currentUser.UserId,
                    request.Title,
                    request.Description,
                    request.LifeArea,
                    request.TargetAt,
                    sortOrder,
                    request.RoleId);
                db.Goals.Add(goal);
                AddAudit(db, currentUser.UserId, "goal", "created", "goal", goal.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created("/api/v1/goals/" + goal.Id, await ToGoalResponseAsync(goal, db, cancellationToken));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });

        goals.MapPut("/{id:guid}", async (
            Guid id,
            UpdateGoalRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var goal = await db.Goals.SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.UserId,
                cancellationToken);
            if (goal is null)
            {
                return Results.NotFound();
            }

            if (goal.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The goal was changed by another request. Refresh and retry." });
            }

            if (!await IsOwnedActiveRoleAsync(db, currentUser.UserId, request.RoleId, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["roleId"] = ["The selected role does not exist or is archived."],
                });
            }

            try
            {
                goal.Update(
                    request.Title,
                    request.Description,
                    request.LifeArea,
                    request.TargetAt,
                    request.SortOrder,
                    request.RoleId);
                switch (request.State)
                {
                    case GoalState.Active:
                        if (goal.State != GoalState.Active)
                        {
                            goal.Restore();
                        }

                        break;
                    case GoalState.Completed:
                        goal.Complete();
                        break;
                    case GoalState.Archived:
                        goal.Archive();
                        break;
                    default:
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["state"] = ["Goal state is invalid."],
                        });
                }

                await CancelPendingGoalRoadmapProposalsAsync(
                    db,
                    currentUser.UserId,
                    goal.Id,
                    cancellationToken);
                AddAudit(db, currentUser.UserId, "goal", "updated", "goal", goal.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(await ToGoalResponseAsync(goal, db, cancellationToken));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });

        goals.MapGet("/{id:guid}/milestones", async (
            Guid id,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken,
            bool includeArchived = false) =>
        {
            var owned = await db.Goals.AsNoTracking().AnyAsync(
                goal => goal.Id == id && goal.OwnerId == currentUser.UserId,
                cancellationToken);
            if (!owned)
            {
                return Results.NotFound();
            }

            var query = db.GoalMilestones.AsNoTracking()
                .Where(milestone => milestone.OwnerId == currentUser.UserId && milestone.GoalId == id);
            if (!includeArchived)
            {
                query = query.Where(milestone => milestone.State != GoalMilestoneState.Archived);
            }

            var values = await query
                .OrderBy(milestone => milestone.State)
                .ThenBy(milestone => milestone.SortOrder)
                .ThenBy(milestone => milestone.Title)
                .ToListAsync(cancellationToken);
            return Results.Ok(values.Select(ToResponse));
        });

        goals.MapPost("/{id:guid}/milestones", async (
            Guid id,
            CreateGoalMilestoneRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var goal = await db.Goals.AsNoTracking().SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.UserId && value.State == GoalState.Active,
                cancellationToken);
            if (goal is null)
            {
                return Results.NotFound();
            }

            try
            {
                var sortOrder = request.SortOrder ?? await GetNextGoalMilestoneSortOrderAsync(db, currentUser.UserId, id, cancellationToken);
                var milestone = new GoalMilestone(
                    currentUser.UserId,
                    id,
                    request.Title,
                    request.Description,
                    request.EstimatedHours,
                    sortOrder);
                await CancelPendingGoalRoadmapProposalsAsync(
                    db,
                    currentUser.UserId,
                    id,
                    cancellationToken);
                db.GoalMilestones.Add(milestone);
                AddAudit(db, currentUser.UserId, "goal", "milestone_created", "goal_milestone", milestone.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created("/api/v1/goals/" + id + "/milestones/" + milestone.Id, ToResponse(milestone));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });

        goals.MapPut("/{id:guid}/milestones/{milestoneId:guid}", async (
            Guid id,
            Guid milestoneId,
            UpdateGoalMilestoneRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var milestone = await db.GoalMilestones.SingleOrDefaultAsync(
                value => value.Id == milestoneId
                    && value.GoalId == id
                    && value.OwnerId == currentUser.UserId,
                cancellationToken);
            if (milestone is null)
            {
                return Results.NotFound();
            }

            if (milestone.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The milestone was changed by another request. Refresh and retry." });
            }

            try
            {
                milestone.Update(request.Title, request.Description, request.EstimatedHours, request.SortOrder);
                switch (request.State)
                {
                    case GoalMilestoneState.Pending:
                        milestone.Reopen();
                        break;
                    case GoalMilestoneState.Completed:
                        milestone.Complete();
                        break;
                    case GoalMilestoneState.Archived:
                        milestone.Archive();
                        break;
                    default:
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["state"] = ["Milestone state is invalid."],
                        });
                }

                AddAudit(db, currentUser.UserId, "goal", "milestone_updated", "goal_milestone", milestone.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToResponse(milestone));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });

        goals.MapPost("/{id:guid}/roadmap/propose", async (
            Guid id,
            AppDbContext db,
            IAiGateway gateway,
            ICurrentUser currentUser,
            IContentProtector protector,
            CancellationToken cancellationToken) =>
        {
            var goal = await db.Goals.SingleOrDefaultAsync(
                value => value.Id == id
                    && value.OwnerId == currentUser.UserId
                    && value.State == GoalState.Active,
                cancellationToken);
            if (goal is null)
            {
                return Results.NotFound();
            }

            if (await db.GoalMilestones.AsNoTracking().AnyAsync(
                value => value.OwnerId == currentUser.UserId
                    && value.GoalId == goal.Id
                    && value.State != GoalMilestoneState.Archived,
                cancellationToken))
            {
                return Results.Conflict(new { error = "This goal already has milestones. Create a new roadmap only after reviewing or archiving them." });
            }

            var sourcePrefix = GoalRoadmapSourcePrefix(goal.Id, goal.ConcurrencyToken);
            var existing = await db.AiActionProposals
                .Where(proposal => proposal.OwnerId == currentUser.UserId
                    && proposal.Source == "graphrag-roadmap"
                    && proposal.State == AiProposalState.Pending
                    && proposal.SourceKey.StartsWith(sourcePrefix))
                .OrderByDescending(proposal => proposal.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                var existingRoadmap = await db.GoalRoadmapProposals.SingleOrDefaultAsync(
                    proposal => proposal.OwnerId == currentUser.UserId && proposal.ProposalId == existing.Id,
                    cancellationToken);
                if (existingRoadmap is not null)
                {
                    return Results.Ok(ToGoalRoadmapProposalResponse(existing, existingRoadmap, currentUser.UserId, protector));
                }
            }

            try
            {
                using var response = await gateway.InvokeAsync(
                    "/api/roadmap/prerequisites",
                    new { goal_name = goal.Title },
                    currentUser.UserId,
                    "ai.invoke",
                    cancellationToken);
                var roadmap = ParseGoalRoadmap(response.RootElement);
                if (roadmap.Steps.Count == 0)
                {
                    return Results.Conflict(new { error = "The knowledge graph did not return a usable roadmap for this goal." });
                }

                var payload = JsonSerializer.Serialize(
                    new GoalRoadmapProposalPayload(goal.Id, goal.ConcurrencyToken),
                    JsonOptions);
                var proposal = new AiActionProposal(
                    currentUser.UserId,
                    AiProposalKind.ApplyGoalRoadmap,
                    "graphrag-roadmap",
                    GoalRoadmapSourceKey(goal.Id, goal.ConcurrencyToken),
                    "Add roadmap: " + goal.Title,
                    "Review the GraphRAG milestones before adding them to this goal.",
                    payload);

                var roadmapProposal = new GoalRoadmapProposal(
                    currentUser.UserId,
                    proposal.Id,
                    goal.Id,
                    goal.ConcurrencyToken,
                    protector.Protect(currentUser.UserId, JsonSerializer.Serialize(roadmap, JsonOptions)));
                db.AiActionProposals.Add(proposal);
                db.GoalRoadmapProposals.Add(roadmapProposal);
                AddAudit(db, currentUser.UserId, "goal_roadmap", "proposal_created", "goal_roadmap_proposal", roadmapProposal.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToGoalRoadmapProposalResponse(proposal, roadmapProposal, currentUser.UserId, protector));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "The GraphRAG roadmap service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (InvalidOperationException)
            {
                return Results.Problem(title: "The GraphRAG roadmap service returned an invalid response.", statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }

    private static void MapGoalRoadmapProposals(RouteGroupBuilder api)
    {
        var roadmaps = api.MapGroup("/ai/goals/roadmaps");
        roadmaps.MapGet("/pending", async (
            AppDbContext db,
            ICurrentUser currentUser,
            IContentProtector protector,
            CancellationToken cancellationToken) =>
        {
            var roadmapProposals = await db.GoalRoadmapProposals.AsNoTracking()
                .Where(value => value.OwnerId == currentUser.UserId && value.State == GoalRoadmapProposalState.Pending)
                .OrderByDescending(value => value.CreatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);
            if (roadmapProposals.Count == 0)
            {
                return Results.Ok(Array.Empty<GoalRoadmapProposalResponse>());
            }

            var ids = roadmapProposals.Select(value => value.ProposalId).ToArray();
            var proposals = await db.AiActionProposals.AsNoTracking()
                .Where(value => value.OwnerId == currentUser.UserId
                    && value.Kind == AiProposalKind.ApplyGoalRoadmap
                    && value.State == AiProposalState.Pending
                    && ids.Contains(value.Id))
                .ToDictionaryAsync(value => value.Id, cancellationToken);
            return Results.Ok(roadmapProposals
                .Where(value => proposals.ContainsKey(value.ProposalId))
                .Select(value => ToGoalRoadmapProposalResponse(proposals[value.ProposalId], value, currentUser.UserId, protector))
                .ToArray());
        });
    }

    private static async Task<bool> IsOwnedActiveGoalAsync(
        AppDbContext db,
        Guid ownerId,
        Guid? goalId,
        CancellationToken cancellationToken) =>
        goalId is null
            || await db.Goals.AsNoTracking().AnyAsync(
                goal => goal.Id == goalId.Value && goal.OwnerId == ownerId && goal.State == GoalState.Active,
                cancellationToken);

    private static async Task CancelPendingGoalRoadmapProposalsAsync(
        AppDbContext db,
        Guid ownerId,
        Guid goalId,
        CancellationToken cancellationToken)
    {
        var sourcePrefix = "goal-roadmap:" + goalId.ToString("N") + ":";
        var pending = await db.AiActionProposals
            .Where(proposal => proposal.OwnerId == ownerId
                && proposal.Source == "graphrag-roadmap"
                && proposal.State == AiProposalState.Pending
                && proposal.SourceKey.StartsWith(sourcePrefix))
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        var proposalIds = pending.Select(proposal => proposal.Id).ToArray();
        var roadmapProposals = await db.GoalRoadmapProposals
            .Where(proposal => proposal.OwnerId == ownerId
                && proposal.State == GoalRoadmapProposalState.Pending
                && proposalIds.Contains(proposal.ProposalId))
            .ToListAsync(cancellationToken);
        foreach (var roadmapProposal in roadmapProposals)
        {
            roadmapProposal.Cancel();
            AddAudit(db, ownerId, "goal_roadmap", "invalidated", "goal_roadmap_proposal", roadmapProposal.Id);
        }

        foreach (var proposal in pending)
        {
            proposal.Cancel();
            AddAudit(db, ownerId, "goal_roadmap", "proposal_invalidated", "ai_action_proposal", proposal.Id);
        }
    }

    private static async Task<int> GetNextGoalSortOrderAsync(
        AppDbContext db,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var last = await db.Goals.AsNoTracking()
            .Where(goal => goal.OwnerId == ownerId)
            .Select(goal => (int?)goal.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;
        return last + 1_000;
    }

    private static async Task<int> GetNextGoalMilestoneSortOrderAsync(
        AppDbContext db,
        Guid ownerId,
        Guid goalId,
        CancellationToken cancellationToken)
    {
        var last = await db.GoalMilestones.AsNoTracking()
            .Where(milestone => milestone.OwnerId == ownerId && milestone.GoalId == goalId)
            .Select(milestone => (int?)milestone.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;
        return last + 1_000;
    }

    private static async Task<IReadOnlyList<GoalResponse>> ToGoalResponsesAsync(
        List<Goal> goals,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (goals.Count == 0)
        {
            return [];
        }

        var ownerId = goals.First().OwnerId;
        var ids = goals.Select(goal => goal.Id).ToArray();
        var milestoneCounts = await db.GoalMilestones.AsNoTracking()
            .Where(milestone => milestone.OwnerId == ownerId && ids.Contains(milestone.GoalId))
            .GroupBy(milestone => milestone.GoalId)
            .Select(group => new
            {
                GoalId = group.Key,
                Total = group.Count(),
                Completed = group.Count(milestone => milestone.State == GoalMilestoneState.Completed),
            })
            .ToDictionaryAsync(value => value.GoalId, cancellationToken);
        var taskCounts = await db.Tasks.AsNoTracking()
            .Where(task => task.OwnerId == ownerId && task.GoalId.HasValue && ids.Contains(task.GoalId.Value))
            .GroupBy(task => task.GoalId!.Value)
            .Select(group => new
            {
                GoalId = group.Key,
                Total = group.Count(),
                Completed = group.Count(task => task.State == TaskState.Completed),
            })
            .ToDictionaryAsync(value => value.GoalId, cancellationToken);

        return goals.Select(goal =>
        {
            milestoneCounts.TryGetValue(goal.Id, out var milestones);
            taskCounts.TryGetValue(goal.Id, out var tasks);
            return ToResponse(
                goal,
                milestones is null ? (0, 0) : (milestones.Total, milestones.Completed),
                tasks is null ? (0, 0) : (tasks.Total, tasks.Completed));
        }).ToArray();
    }

    private static async Task<GoalResponse> ToGoalResponseAsync(
        Goal goal,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var values = await ToGoalResponsesAsync([goal], db, cancellationToken);
        return values[0];
    }

    private static GoalResponse ToResponse(
        Goal goal,
        (int Total, int Completed) milestones,
        (int Total, int Completed) tasks)
    {
        var total = milestones.Total + tasks.Total;
        var completed = milestones.Completed + tasks.Completed;
        var progress = total == 0
            ? 0
            : (int)Math.Round((double)completed * 100 / total, MidpointRounding.AwayFromZero);
        return new GoalResponse(
            goal.Id,
            goal.Title,
            goal.Description,
            goal.LifeArea,
            goal.RoleId,
            goal.TargetAt,
            goal.State,
            goal.SortOrder,
            goal.RoadmapSummary,
            milestones.Total,
            milestones.Completed,
            tasks.Total,
            tasks.Completed,
            progress,
            goal.ConcurrencyToken,
            goal.CreatedAt,
            goal.UpdatedAt);
    }

    private static GoalMilestoneResponse ToResponse(GoalMilestone milestone) =>
        new(
            milestone.Id,
            milestone.GoalId,
            milestone.Title,
            milestone.Description,
            milestone.EstimatedHours,
            milestone.SortOrder,
            milestone.State,
            milestone.CompletedAt,
            milestone.ConcurrencyToken,
            milestone.CreatedAt,
            milestone.UpdatedAt);

    private static string GoalRoadmapSourcePrefix(Guid goalId, Guid concurrencyToken) =>
        "goal-roadmap:" + goalId.ToString("N") + ":" + concurrencyToken.ToString("N") + ":";

    private static string GoalRoadmapSourceKey(Guid goalId, Guid concurrencyToken) =>
        GoalRoadmapSourcePrefix(goalId, concurrencyToken) + Guid.CreateVersion7().ToString("N");

    private static bool IsGoalRoadmapSourceKey(string sourceKey, Guid goalId, Guid concurrencyToken) =>
        sourceKey.StartsWith(GoalRoadmapSourcePrefix(goalId, concurrencyToken), StringComparison.Ordinal);

    private static GoalRoadmapProposalResponse ToGoalRoadmapProposalResponse(
        AiActionProposal proposal,
        GoalRoadmapProposal roadmapProposal,
        Guid ownerId,
        IContentProtector protector)
    {
        var protectedPayload = protector.Unprotect(ownerId, roadmapProposal.ProtectedPayload);
        var roadmap = JsonSerializer.Deserialize<GoalRoadmapContentPayload>(protectedPayload, JsonOptions)
            ?? throw new InvalidOperationException("Goal roadmap proposal is invalid.");
        return new GoalRoadmapProposalResponse(
            ToResponse(proposal),
            roadmapProposal.GoalId,
            new GoalRoadmapResponse(
                roadmap.Summary,
                roadmap.TotalEstimatedHours,
                roadmap.Steps.Select(step => new GoalRoadmapStepResponse(
                    step.SortOrder,
                    step.Title,
                    step.Description,
                    step.EstimatedHours,
                    step.Resources)).ToArray()));
    }

    private static GoalRoadmapContentPayload ParseGoalRoadmap(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("steps", out var stepsValue)
            || stepsValue.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("GraphRAG roadmap response is invalid.");
        }

        var summary = GetText(root, "motivational_summary")
            ?? GetText(root, "summary")
            ?? "A graph-grounded roadmap for this goal.";
        if (summary.Length > 4_000)
        {
            summary = summary[..4_000];
        }

        decimal? totalHours = GetDecimal(root, "total_estimated_hours")
            ?? GetDecimal(root, "totalEstimatedHours");
        var steps = new List<GoalRoadmapStepPayload>();
        foreach (var value in stepsValue.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var title = GetText(value, "topic")
                ?? GetText(value, "topic_name")
                ?? GetText(value, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            title = title.Trim();
            if (title.Length > 300)
            {
                title = title[..300];
            }

            var description = GetText(value, "description");
            if (description?.Length > 4_000)
            {
                description = description[..4_000];
            }

            var estimatedHours = GetDecimal(value, "estimated_hours")
                ?? GetDecimal(value, "estimatedHours");
            if (estimatedHours is < 0 or > 10_000)
            {
                estimatedHours = null;
            }

            var resources = GetStringArray(value, "resources");
            var subtasks = GetStringArray(value, "subtasks");

            var fullDesc = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            if (subtasks != null && subtasks.Count > 0)
            {
                var tasksText = "\n\nKey Work Items:\n" + string.Join("\n", subtasks.Select(s => $"• {s}"));
                fullDesc = string.IsNullOrWhiteSpace(fullDesc) ? tasksText.TrimStart() : (fullDesc + tasksText);
            }

            steps.Add(new GoalRoadmapStepPayload(
                steps.Count + 1_000,
                title,
                fullDesc,
                estimatedHours,
                resources));
            if (steps.Count == 20)
            {
                break;
            }
        }

        return new GoalRoadmapContentPayload(
            summary,
            totalHours is < 0 or > 200_000 ? null : totalHours,
            steps);
    }

    private static List<string>? GetStringArray(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var str = item.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    list.Add(str.Length > 500 ? str[..500] : str);
                }
            }
        }
        return list.Count > 0 ? list : null;
    }

    private static string? GetText(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()?.Trim()
            : null;

    private static decimal? GetDecimal(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return property.TryGetDecimal(out var parsed) ? parsed : null;
    }

    private sealed record GoalRoadmapProposalPayload(
        Guid GoalId,
        Guid SourceConcurrencyToken);

    private sealed record GoalRoadmapContentPayload(
        string Summary,
        decimal? TotalEstimatedHours,
        IReadOnlyList<GoalRoadmapStepPayload> Steps);

    private sealed record GoalRoadmapStepPayload(
        int SortOrder,
        string Title,
        string? Description,
        decimal? EstimatedHours,
        IReadOnlyList<string>? Resources = null);
}