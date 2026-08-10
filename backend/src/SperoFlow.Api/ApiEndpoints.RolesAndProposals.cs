using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Application;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static readonly CoreLifeRoleDefinition[] CoreLifeRoles =
    [
        new("mental", "Mental", LifeArea.Learning, "#0053dc", "psychology", 1_000),
        new("physical", "Physical", LifeArea.Physical, "#dc2626", "fitness_center", 2_000),
        new("social", "Social", LifeArea.Social, "#047857", "groups", 3_000),
        new("spiritual", "Spiritual", LifeArea.Spiritual, "#a16207", "self_improvement", 4_000),
    ];

    private static void MapLifeRoles(RouteGroupBuilder api)
    {
        var roles = api.MapGroup("/roles");

        roles.MapGet("", async (
            bool includeArchived,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var query = db.LifeRoles.AsNoTracking().Where(role => role.OwnerId == currentUser.UserId);
            if (!includeArchived)
            {
                query = query.Where(role => !role.IsArchived);
            }

            var values = await query
                .OrderBy(role => role.IsArchived)
                .ThenBy(role => role.Category)
                .ThenBy(role => role.SortOrder)
                .ThenBy(role => role.Name)
                .ToListAsync(cancellationToken);
            return Results.Ok(values.Select(ToResponse));
        });

        roles.MapPost("/bootstrap", async (
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var seeded = await EnsureCoreLifeRolesAsync(db, currentUser.UserId, cancellationToken);
            if (seeded)
            {
                AddAudit(db, currentUser.UserId, "role", "core_roles_initialized", "life_role", null);
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    return Results.Conflict(new { error = "Roles were initialized by another request. Refresh and try again." });
                }
            }

            var values = await db.LifeRoles.AsNoTracking()
                .Where(role => role.OwnerId == currentUser.UserId && !role.IsArchived)
                .OrderBy(role => role.Category)
                .ThenBy(role => role.SortOrder)
                .ThenBy(role => role.Name)
                .ToListAsync(cancellationToken);
            return Results.Ok(values.Select(ToResponse));
        });

        roles.MapPost("", async (
            CreateLifeRoleRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var sortOrder = request.SortOrder == 0
                    ? await GetNextRoleSortOrderAsync(db, currentUser.UserId, cancellationToken)
                    : request.SortOrder;
                var role = new LifeRole(
                    currentUser.UserId,
                    request.Name,
                    request.Category,
                    request.DefaultLifeArea,
                    request.Color,
                    request.Icon,
                    sortOrder);
                db.LifeRoles.Add(role);
                AddAudit(db, currentUser.UserId, "role", "created", "life_role", role.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created("/api/v1/roles/" + role.Id, ToResponse(role));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "A role with this identity already exists." });
            }
        });

        roles.MapPut("/{id:guid}", async (
            Guid id,
            UpdateLifeRoleRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var role = await db.LifeRoles.SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.UserId,
                cancellationToken);
            if (role is null)
            {
                return Results.NotFound();
            }

            if (role.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The role was changed by another request. Refresh and retry." });
            }

            try
            {
                role.Update(
                    request.Name,
                    request.Category,
                    request.DefaultLifeArea,
                    request.Color,
                    request.Icon,
                    request.SortOrder);
                AddAudit(db, currentUser.UserId, "role", "updated", "life_role", role.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToResponse(role));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });

        roles.MapPost("/{id:guid}/archive", async (
            Guid id,
            ConcurrencyTokenRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var role = await db.LifeRoles.SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.UserId,
                cancellationToken);
            if (role is null)
            {
                return Results.NotFound();
            }

            if (role.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The role was changed by another request. Refresh and retry." });
            }

            try
            {
                role.Archive();
                AddAudit(db, currentUser.UserId, "role", "archived", "life_role", role.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToResponse(role));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });

        roles.MapPost("/{id:guid}/restore", async (
            Guid id,
            ConcurrencyTokenRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var role = await db.LifeRoles.SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.UserId,
                cancellationToken);
            if (role is null)
            {
                return Results.NotFound();
            }

            if (role.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The role was changed by another request. Refresh and retry." });
            }

            role.Restore();
            AddAudit(db, currentUser.UserId, "role", "restored", "life_role", role.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(role));
        });
    }

    private static void MapAiActionProposals(RouteGroupBuilder api)
    {
        var proposals = api.MapGroup("/ai/proposals");

        proposals.MapGet("", async (
            AiProposalState? state,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var selectedState = state ?? AiProposalState.Pending;
            var values = await db.AiActionProposals.AsNoTracking()
                .Where(proposal => proposal.OwnerId == currentUser.UserId && proposal.State == selectedState)
                .OrderByDescending(proposal => proposal.CreatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);
            return Results.Ok(values.Select(ToResponse));
        });

        proposals.MapPost("/{id:guid}/approve", async (
            Guid id,
            ConcurrencyTokenRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            IContentProtector protector,
            CancellationToken cancellationToken) =>
        {
            var proposal = await db.AiActionProposals.SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.UserId,
                cancellationToken);
            if (proposal is null)
            {
                return Results.NotFound();
            }

            if (proposal.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The suggestion was changed by another request. Refresh and retry." });
            }

            if (proposal.State != AiProposalState.Pending)
            {
                return Results.Conflict(new { error = "Only a pending suggestion can be approved." });
            }

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                switch (proposal.Kind)
                {
                    case AiProposalKind.CreateTask:
                    {
                        var payload = JsonSerializer.Deserialize<CreateTaskProposalPayload>(proposal.Payload, JsonOptions);
                        if (payload is null || !Enum.IsDefined(payload.LifeArea) || !Enum.IsDefined(payload.Quadrant))
                        {
                            return Results.Conflict(new { error = "This task suggestion is no longer valid." });
                        }

                        if (!await IsOwnedActiveRoleAsync(db, currentUser.UserId, payload.RoleId, cancellationToken))
                        {
                            return Results.Conflict(new { error = "The role associated with this suggestion is no longer active." });
                        }

                        var sortOrder = await GetNextTaskSortOrderAsync(
                            db,
                            currentUser.UserId,
                            projectId: null,
                            TaskState.Todo,
                            cancellationToken);
                        var task = new TaskItem(
                            currentUser.UserId,
                            payload.Title,
                            payload.Description,
                            payload.LifeArea,
                            payload.Quadrant,
                            dueAt: null,
                            estimatedMinutes: payload.EstimatedMinutes,
                            startAt: null,
                            projectId: null,
                            sortOrder,
                            TaskState.Todo,
                            payload.RoleId);
                        task.SetSource("ai:" + proposal.Source);
                        db.Tasks.Add(task);
                        proposal.Approve(task.Id);
                        AddAudit(db, currentUser.UserId, "task", "created_from_ai_proposal", "task", task.Id);
                        break;
                    }
                    case AiProposalKind.CreateLifeRole:
                    {
                        var payload = JsonSerializer.Deserialize<CreateLifeRoleProposalPayload>(proposal.Payload, JsonOptions);
                        if (payload is null || !Enum.IsDefined(payload.Category) || !Enum.IsDefined(payload.DefaultLifeArea))
                        {
                            return Results.Conflict(new { error = "This role suggestion is no longer valid." });
                        }

                        RoleDiscoveryFinding? finding = null;
                        if (string.Equals(proposal.Source, "role-discovery", StringComparison.Ordinal))
                        {
                            finding = await db.RoleDiscoveryFindings.SingleOrDefaultAsync(
                                value => value.ProposalId == proposal.Id && value.OwnerId == currentUser.UserId,
                                cancellationToken);
                            if (finding is null || finding.State != RoleDiscoveryFindingState.Pending)
                            {
                                return Results.Conflict(new { error = "This role discovery finding is no longer pending." });
                            }
                        }

                        var normalizedName = payload.Name.Trim();
                        var duplicate = await db.LifeRoles.AnyAsync(
                            role => role.OwnerId == currentUser.UserId
                                && !role.IsArchived
                                && EF.Functions.ILike(role.Name, normalizedName),
                            cancellationToken);
                        if (duplicate)
                        {
                            return Results.Conflict(new { error = "An active role with this name already exists." });
                        }

                        var sortOrder = payload.SortOrder ?? await GetNextRoleSortOrderAsync(db, currentUser.UserId, cancellationToken);
                        var role = new LifeRole(
                            currentUser.UserId,
                            payload.Name,
                            payload.Category,
                            payload.DefaultLifeArea,
                            payload.Color,
                            payload.Icon,
                            sortOrder);
                        db.LifeRoles.Add(role);
                        proposal.Approve(role.Id);
                        finding?.Approve();
                        AddAudit(db, currentUser.UserId, "role", "created_from_ai_proposal", "life_role", role.Id);
                        if (finding is not null)
                        {
                            AddAudit(db, currentUser.UserId, "role_discovery", "approved", "role_discovery_finding", finding.Id);
                        }

                        break;
                    }
                    case AiProposalKind.ApplyJournalInsight:
                    {
                        var payload = JsonSerializer.Deserialize<JournalInsightProposalPayload>(proposal.Payload, JsonOptions);
                        if (payload is null
                            || payload.InsightId == Guid.Empty
                            || payload.JournalEntryId == Guid.Empty
                            || payload.SourceConcurrencyToken == Guid.Empty
                            || !string.Equals(
                                proposal.SourceKey,
                                JournalInsightProposalSourceKey(payload.JournalEntryId, payload.SourceConcurrencyToken),
                                StringComparison.Ordinal))
                        {
                            return Results.Conflict(new { error = "This journal reflection is no longer valid." });
                        }

                        var insight = await db.JournalInsights.SingleOrDefaultAsync(
                            value => value.Id == payload.InsightId
                                && value.OwnerId == currentUser.UserId
                                && value.JournalEntryId == payload.JournalEntryId
                                && value.SourceConcurrencyToken == payload.SourceConcurrencyToken,
                            cancellationToken);
                        var entry = await db.JournalEntries.AsNoTracking().SingleOrDefaultAsync(
                            value => value.Id == payload.JournalEntryId && value.OwnerId == currentUser.UserId,
                            cancellationToken);
                        if (insight is null
                            || entry is null
                            || insight.State != JournalInsightState.Pending
                            || entry.ConcurrencyToken != payload.SourceConcurrencyToken)
                        {
                            return Results.Conflict(new { error = "This journal reflection no longer matches the current entry." });
                        }

                        insight.Approve();
                        proposal.Approve(insight.Id);
                        AddAudit(db, currentUser.UserId, "journal", "insight_approved", "journal_insight", insight.Id);
                        break;
                    }
                    case AiProposalKind.ApplyTaskClassification:
                    {
                        var payload = JsonSerializer.Deserialize<TaskClassificationProposalPayload>(proposal.Payload, JsonOptions);
                        if (payload is null
                            || payload.TaskId == Guid.Empty
                            || payload.SourceConcurrencyToken == Guid.Empty
                            || !Enum.IsDefined(payload.Quadrant)
                            || payload.Quadrant == EisenhowerQuadrant.Unsorted
                            || !IsTaskClassificationSourceKey(proposal.SourceKey, payload.TaskId, payload.SourceConcurrencyToken)
                        )
                        {
                            return Results.Conflict(new { error = "This priority suggestion is no longer valid." });
                        }

                        var task = await db.Tasks.SingleOrDefaultAsync(
                            value => value.Id == payload.TaskId && value.OwnerId == currentUser.UserId,
                            cancellationToken);
                        if (task is null
                            || task.ConcurrencyToken != payload.SourceConcurrencyToken
                            || task.State is TaskState.Completed or TaskState.Cancelled)
                        {
                            return Results.Conflict(new { error = "This priority suggestion no longer matches the current task." });
                        }

                        task.SetQuadrant(payload.Quadrant);
                        proposal.Approve(task.Id);
                        AddAudit(db, currentUser.UserId, "eisenhower", "classification_approved", "task", task.Id);
                        break;
                    }
                    case AiProposalKind.ApplyTaskSchedule:
                    {
                        var payload = JsonSerializer.Deserialize<TaskScheduleProposalPayload>(proposal.Payload, JsonOptions);
                        if (payload is null
                            || payload.TaskId == Guid.Empty
                            || payload.SourceConcurrencyToken == Guid.Empty
                            || payload.DurationMinutes is < 5 or > 480
                            || payload.EndAt != payload.StartAt.AddMinutes(payload.DurationMinutes)
                            || !IsTaskScheduleSourceKey(proposal.SourceKey, payload.TaskId, payload.SourceConcurrencyToken)
                            || !string.Equals(proposal.Source, "scheduler", StringComparison.Ordinal))
                        {
                            return Results.Conflict(new { error = "This schedule suggestion is no longer valid." });
                        }

                        var task = await db.Tasks.SingleOrDefaultAsync(
                            value => value.Id == payload.TaskId && value.OwnerId == currentUser.UserId,
                            cancellationToken);
                        if (task is null
                            || task.ConcurrencyToken != payload.SourceConcurrencyToken
                            || task.State is TaskState.Completed or TaskState.Cancelled
                            || payload.StartAt < DateTimeOffset.UtcNow
                            || (task.DueAt.HasValue && payload.EndAt > task.DueAt.Value))
                        {
                            return Results.Conflict(new { error = "This schedule suggestion no longer matches the current task." });
                        }

                        if (await HasSchedulingConflictAsync(
                            db,
                            currentUser.UserId,
                            task.Id,
                            payload.StartAt,
                            payload.EndAt,
                            cancellationToken))
                        {
                            return Results.Conflict(new { error = "This time is no longer available. Refresh and request another suggestion." });
                        }

                        task.Schedule(payload.StartAt, payload.DurationMinutes);
                        proposal.Approve(task.Id);
                        await CancelPendingTaskClassificationProposalsAsync(
                            db,
                            currentUser.UserId,
                            task.Id,
                            cancellationToken);
                        await CancelPendingTaskScheduleProposalsAsync(
                            db,
                            currentUser.UserId,
                            task.Id,
                            cancellationToken);
                        AddAudit(db, currentUser.UserId, "scheduler", "proposal_approved", "task", task.Id);
                        break;
                    }
                    case AiProposalKind.ApplyGoalRoadmap:
                    {
                        var payload = JsonSerializer.Deserialize<GoalRoadmapProposalPayload>(proposal.Payload, JsonOptions);
                        if (payload is null
                            || payload.GoalId == Guid.Empty
                            || payload.SourceConcurrencyToken == Guid.Empty
                            || !IsGoalRoadmapSourceKey(proposal.SourceKey, payload.GoalId, payload.SourceConcurrencyToken)
                        )
                        {
                            return Results.Conflict(new { error = "This roadmap proposal is no longer valid." });
                        }

                        var roadmapProposal = await db.GoalRoadmapProposals.SingleOrDefaultAsync(
                            value => value.ProposalId == proposal.Id
                                && value.OwnerId == currentUser.UserId
                                && value.GoalId == payload.GoalId
                                && value.SourceConcurrencyToken == payload.SourceConcurrencyToken,
                            cancellationToken);
                        var goal = await db.Goals.SingleOrDefaultAsync(
                            value => value.Id == payload.GoalId && value.OwnerId == currentUser.UserId,
                            cancellationToken);
                        if (roadmapProposal is null
                            || goal is null
                            || roadmapProposal.State != GoalRoadmapProposalState.Pending
                            || goal.State != GoalState.Active
                            || goal.ConcurrencyToken != payload.SourceConcurrencyToken)
                        {
                            return Results.Conflict(new { error = "This roadmap proposal no longer matches the current goal." });
                        }

                        if (await db.GoalMilestones.AnyAsync(
                            value => value.OwnerId == currentUser.UserId
                                && value.GoalId == goal.Id
                                && value.State != GoalMilestoneState.Archived,
                            cancellationToken))
                        {
                            return Results.Conflict(new { error = "This goal already has milestones. Review or cancel the stale roadmap proposal." });
                        }

                        var roadmap = JsonSerializer.Deserialize<GoalRoadmapContentPayload>(
                            protector.Unprotect(currentUser.UserId, roadmapProposal.ProtectedPayload),
                            JsonOptions);
                        if (roadmap is null
                            || roadmap.Steps.Count is < 1 or > 20
                            || roadmap.Steps.Any(step => string.IsNullOrWhiteSpace(step.Title)
                                || step.Title.Length > 300
                                || step.Description?.Length > 4_000
                                || step.EstimatedHours is < 0 or > 10_000
                                || step.SortOrder < 0))
                        {
                            return Results.Conflict(new { error = "This roadmap proposal content is no longer valid." });
                        }

                        foreach (var step in roadmap.Steps.OrderBy(step => step.SortOrder).ThenBy(step => step.Title))
                        {
                            var desc = step.Description;
                            if (step.Resources != null && step.Resources.Count > 0)
                            {
                                var resText = "\n\nResources:\n" + string.Join("\n", step.Resources.Select(r => $"• {r}"));
                                desc = string.IsNullOrWhiteSpace(desc) ? resText.TrimStart() : (desc + resText);
                            }
                            db.GoalMilestones.Add(new GoalMilestone(
                                currentUser.UserId,
                                goal.Id,
                                step.Title,
                                desc,
                                step.EstimatedHours,
                                step.SortOrder));
                        }

                        goal.ApplyRoadmap(roadmap.Summary);
                        roadmapProposal.Approve();
                        proposal.Approve(goal.Id);
                        AddAudit(db, currentUser.UserId, "goal", "roadmap_approved", "goal", goal.Id);
                        AddAudit(db, currentUser.UserId, "goal_roadmap", "approved", "goal_roadmap_proposal", roadmapProposal.Id);
                        break;
                    }
                    case AiProposalKind.CreateHabit:
                    {
                        var payload = JsonSerializer.Deserialize<CreateHabitProposalPayload>(proposal.Payload, JsonOptions);
                        if (payload is null
                            || string.IsNullOrWhiteSpace(payload.Title)
                            || payload.Title.Trim().Length > 300
                            || payload.Description?.Length > 4_000
                            || !Enum.IsDefined(payload.LifeArea)
                            || payload.TargetPerWeek is < 1 or > 21)
                        {
                            return Results.Conflict(new { error = "This habit proposal is no longer valid." });
                        }

                        var habit = new Habit(
                            currentUser.UserId,
                            payload.Title,
                            payload.Description,
                            payload.LifeArea,
                            payload.TargetPerWeek);
                        db.Habits.Add(habit);
                        proposal.Approve(habit.Id);
                        AddAudit(db, currentUser.UserId, "habit", "created_from_ai_proposal", "habit", habit.Id);
                        break;
                    }
                    case AiProposalKind.ApplyCoachObservation:
                    {
                        if (proposal.AppliedEntityId.HasValue)
                        {
                            var observation = await db.CoachObservations.SingleOrDefaultAsync(
                                value => value.Id == proposal.AppliedEntityId.Value && value.OwnerId == currentUser.UserId,
                                cancellationToken);
                            if (observation is null)
                            {
                                return Results.Conflict(new { error = "This observation is no longer valid." });
                            }
                            proposal.Approve(observation.Id);
                        }
                        else
                        {
                            proposal.Approve(proposal.Id);
                        }
                        AddAudit(db, currentUser.UserId, "coach", "observation_approved", "ai_action_proposal", proposal.Id);
                        break;
                    }
                    default:
                        return Results.Conflict(new { error = "This suggestion type is not supported." });
                }

                AddAudit(db, currentUser.UserId, "ai_proposal", "approved", "ai_action_proposal", proposal.Id);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Results.Ok(ToResponse(proposal));
            }
            catch (JsonException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Results.Conflict(new { error = "This suggestion is malformed and cannot be applied." });
            }
            catch (DomainValidationException exception)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DomainValidationProblem(exception);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Results.Conflict(new { error = "The suggestion could not be applied. Refresh and retry." });
            }
        });

        proposals.MapPost("/{id:guid}/cancel", async (
            Guid id,
            ConcurrencyTokenRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            IContentProtector protector,
            CancellationToken cancellationToken) =>
        {
            var proposal = await db.AiActionProposals.SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.UserId,
                cancellationToken);
            if (proposal is null)
            {
                return Results.NotFound();
            }

            if (proposal.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The suggestion was changed by another request. Refresh and retry." });
            }

            if (proposal.State != AiProposalState.Pending)
            {
                return Results.Conflict(new { error = "Only a pending suggestion can be cancelled." });
            }

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            try
            {
                if (proposal.Kind == AiProposalKind.ApplyJournalInsight)
                {
                    var payload = JsonSerializer.Deserialize<JournalInsightProposalPayload>(proposal.Payload, JsonOptions);
                    if (payload is null
                        || payload.InsightId == Guid.Empty
                        || payload.JournalEntryId == Guid.Empty
                        || payload.SourceConcurrencyToken == Guid.Empty
                        || !string.Equals(
                            proposal.SourceKey,
                            JournalInsightProposalSourceKey(payload.JournalEntryId, payload.SourceConcurrencyToken),
                            StringComparison.Ordinal))
                    {
                        return Results.Conflict(new { error = "This journal reflection is no longer valid." });
                    }

                    var insight = await db.JournalInsights.SingleOrDefaultAsync(
                        value => value.Id == payload.InsightId
                            && value.OwnerId == currentUser.UserId
                            && value.JournalEntryId == payload.JournalEntryId
                            && value.SourceConcurrencyToken == payload.SourceConcurrencyToken,
                        cancellationToken);
                    if (insight is null || insight.State != JournalInsightState.Pending)
                    {
                        return Results.Conflict(new { error = "This journal reflection is no longer pending." });
                    }

                    insight.Cancel();
                    AddAudit(db, currentUser.UserId, "journal", "insight_cancelled", "journal_insight", insight.Id);
                }
                else if (proposal.Kind == AiProposalKind.CreateLifeRole
                    && string.Equals(proposal.Source, "role-discovery", StringComparison.Ordinal))
                {
                    var finding = await db.RoleDiscoveryFindings.SingleOrDefaultAsync(
                        value => value.ProposalId == proposal.Id && value.OwnerId == currentUser.UserId,
                        cancellationToken);
                    if (finding is null || finding.State != RoleDiscoveryFindingState.Pending)
                    {
                        return Results.Conflict(new { error = "This role discovery finding is no longer pending." });
                    }

                    finding.Cancel();
                    AddAudit(db, currentUser.UserId, "role_discovery", "cancelled", "role_discovery_finding", finding.Id);
                }
                else if (proposal.Kind == AiProposalKind.ApplyGoalRoadmap)
                {
                    var payload = JsonSerializer.Deserialize<GoalRoadmapProposalPayload>(proposal.Payload, JsonOptions);
                    if (payload is null
                        || payload.GoalId == Guid.Empty
                        || payload.SourceConcurrencyToken == Guid.Empty
                        || !IsGoalRoadmapSourceKey(proposal.SourceKey, payload.GoalId, payload.SourceConcurrencyToken)
                    )
                    {
                        return Results.Conflict(new { error = "This roadmap proposal is no longer valid." });
                    }

                    var roadmapProposal = await db.GoalRoadmapProposals.SingleOrDefaultAsync(
                        value => value.ProposalId == proposal.Id
                            && value.OwnerId == currentUser.UserId
                            && value.State == GoalRoadmapProposalState.Pending,
                        cancellationToken);
                    if (roadmapProposal is null)
                    {
                        return Results.Conflict(new { error = "This roadmap proposal is no longer pending." });
                    }

                    roadmapProposal.Cancel();
                    AddAudit(db, currentUser.UserId, "goal_roadmap", "cancelled", "goal_roadmap_proposal", roadmapProposal.Id);
                }

                proposal.Cancel();
                AddAudit(db, currentUser.UserId, "ai_proposal", "cancelled", "ai_action_proposal", proposal.Id);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Results.Ok(ToResponse(proposal));
            }
            catch (JsonException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Results.Conflict(new { error = "This suggestion is malformed and cannot be cancelled." });
            }
            catch (DomainValidationException exception)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Results.Conflict(new { error = exception.Message });
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Results.Conflict(new { error = "The suggestion could not be cancelled. Refresh and retry." });
            }
        });
    }

    private static async Task<bool> EnsureCoreLifeRolesAsync(
        AppDbContext db,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var existingKeys = await db.LifeRoles.AsNoTracking()
            .Where(role => role.OwnerId == ownerId && role.SystemKey != null)
            .Select(role => role.SystemKey!)
            .ToListAsync(cancellationToken);
        var knownKeys = existingKeys.ToHashSet(StringComparer.Ordinal);
        var added = false;

        foreach (var definition in CoreLifeRoles)
        {
            if (knownKeys.Contains(definition.SystemKey))
            {
                continue;
            }

            db.LifeRoles.Add(new LifeRole(
                ownerId,
                definition.Name,
                LifeRoleCategory.Internal,
                definition.DefaultLifeArea,
                definition.Color,
                definition.Icon,
                definition.SortOrder,
                definition.SystemKey));
            added = true;
        }

        return added;
    }

    private static Task<bool> IsOwnedActiveRoleAsync(
        AppDbContext db,
        Guid ownerId,
        Guid? roleId,
        CancellationToken cancellationToken) =>
        roleId is null || roleId == Guid.Empty
            ? Task.FromResult(true)
            : db.LifeRoles.AnyAsync(
                role => role.Id == roleId.Value && role.OwnerId == ownerId && !role.IsArchived,
                cancellationToken);

    private static async Task<int> GetNextRoleSortOrderAsync(
        AppDbContext db,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var lastSortOrder = await db.LifeRoles.AsNoTracking()
            .Where(role => role.OwnerId == ownerId)
            .Select(role => (int?)role.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;
        return lastSortOrder + 1_000;
    }

    private static async Task<AiActionProposal?> CreateBalanceTaskProposalAsync(
        AppDbContext db,
        Guid ownerId,
        string auditKey,
        JsonElement suggestion,
        CancellationToken cancellationToken)
    {
        if (suggestion.ValueKind != JsonValueKind.Object
            || !suggestion.TryGetProperty("role_id", out var roleIdValue)
            || roleIdValue.ValueKind != JsonValueKind.String
            || !suggestion.TryGetProperty("title", out var titleValue)
            || titleValue.ValueKind != JsonValueKind.String
            || !suggestion.TryGetProperty("description", out var descriptionValue)
            || descriptionValue.ValueKind != JsonValueKind.String
            || !suggestion.TryGetProperty("life_area", out var lifeAreaValue)
            || lifeAreaValue.ValueKind != JsonValueKind.String
            || !suggestion.TryGetProperty("duration_minutes", out var durationValue)
            || durationValue.ValueKind != JsonValueKind.Number
            || !Guid.TryParse(roleIdValue.GetString(), out var roleId))
        {
            return null;
        }

        var title = titleValue.GetString();
        var description = descriptionValue.GetString();
        var lifeAreaText = lifeAreaValue.GetString();
        if (string.IsNullOrWhiteSpace(title)
            || string.IsNullOrWhiteSpace(description)
            || !Enum.TryParse<LifeArea>(lifeAreaText, ignoreCase: true, out var lifeArea)
            || !durationValue.TryGetInt32(out var durationMinutes)
            || durationMinutes is < 5 or > 30)
        {
            return null;
        }

        var sourceKey = "balance:" + auditKey;
        var existing = await db.AiActionProposals.SingleOrDefaultAsync(
            proposal => proposal.OwnerId == ownerId && proposal.SourceKey == sourceKey,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var role = await db.LifeRoles.AsNoTracking().SingleOrDefaultAsync(
            value => value.Id == roleId
                && value.OwnerId == ownerId
                && !value.IsArchived
                && value.DefaultLifeArea == lifeArea,
            cancellationToken);
        if (role is null)
        {
            return null;
        }

        var taskDescription = description.Trim() + " This supports your " + role.Name + " role.";
        var payload = JsonSerializer.Serialize(
            new CreateTaskProposalPayload(
                title,
                taskDescription,
                lifeArea,
                EisenhowerQuadrant.Q2,
                durationMinutes,
                role.Id),
            JsonOptions);
        var proposal = new AiActionProposal(
            ownerId,
            AiProposalKind.CreateTask,
            "balance",
            sourceKey,
            title,
            taskDescription,
            payload);
        db.AiActionProposals.Add(proposal);
        AddAudit(db, ownerId, "ai_proposal", "created", "ai_action_proposal", proposal.Id);
        return proposal;
    }
    private static LifeRoleResponse ToResponse(LifeRole role) =>
        new(
            role.Id,
            role.Name,
            role.Category,
            role.DefaultLifeArea,
            role.Color,
            role.Icon,
            role.SortOrder,
            role.IsArchived,
            role.IsSystemRole,
            role.ConcurrencyToken,
            role.CreatedAt,
            role.UpdatedAt);

    private static AiProposalResponse ToResponse(AiActionProposal proposal)
    {
        using var payload = JsonDocument.Parse(proposal.Payload);
        return new AiProposalResponse(
            proposal.Id,
            proposal.Kind,
            proposal.State,
            proposal.Source,
            proposal.Title,
            proposal.Description,
            payload.RootElement.Clone(),
            proposal.AppliedEntityId,
            proposal.ConcurrencyToken,
            proposal.CreatedAt,
            proposal.ResolvedAt);
    }

    private sealed record CoreLifeRoleDefinition(
        string SystemKey,
        string Name,
        LifeArea DefaultLifeArea,
        string Color,
        string Icon,
        int SortOrder);

    private sealed record CreateTaskProposalPayload(
        string Title,
        string? Description,
        LifeArea LifeArea,
        EisenhowerQuadrant Quadrant,
        int? EstimatedMinutes,
        Guid? RoleId);

    private sealed record CreateLifeRoleProposalPayload(
        string Name,
        LifeRoleCategory Category,
        LifeArea DefaultLifeArea,
        string Color,
        string Icon,
        int? SortOrder);

    private sealed record JournalInsightProposalPayload(
        Guid InsightId,
        Guid JournalEntryId,
        Guid SourceConcurrencyToken);
}