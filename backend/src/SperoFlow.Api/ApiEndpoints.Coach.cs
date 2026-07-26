using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Application;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static void MapCoach(RouteGroupBuilder api)
    {
        var coach = api.MapGroup("/coach");

        coach.MapGet("/conversations", async (
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var conversations = await db.CoachConversations.AsNoTracking()
                .Where(conv => conv.OwnerId == currentUser.UserId && !conv.IsArchived)
                .OrderByDescending(conv => conv.UpdatedAt)
                .ToListAsync(cancellationToken);

            var responses = conversations.Select(conv => new CoachConversationResponse(
                conv.Id,
                conv.Title,
                conv.IsArchived,
                conv.ConcurrencyToken,
                conv.CreatedAt,
                conv.UpdatedAt)).ToList();

            return Results.Ok(responses);
        });

        coach.MapPost("/conversations", async (
            CreateCoachConversationRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var conversation = new CoachConversation(currentUser.UserId, request.Title);
                db.CoachConversations.Add(conversation);
                AddAudit(db, currentUser.UserId, "coach", "conversation_created", "coach_conversation", conversation.Id);
                await db.SaveChangesAsync(cancellationToken);

                return Results.Created("/api/v1/coach/conversations/" + conversation.Id, new CoachConversationResponse(
                    conversation.Id,
                    conversation.Title,
                    conversation.IsArchived,
                    conversation.ConcurrencyToken,
                    conversation.CreatedAt,
                    conversation.UpdatedAt));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });

        coach.MapGet("/conversations/{id:guid}/messages", async (
            Guid id,
            AppDbContext db,
            ICurrentUser currentUser,
            IContentProtector protector,
            CancellationToken cancellationToken) =>
        {
            var conversation = await db.CoachConversations.AsNoTracking()
                .SingleOrDefaultAsync(conv => conv.Id == id && conv.OwnerId == currentUser.UserId, cancellationToken);
            if (conversation is null)
            {
                return Results.NotFound();
            }

            var messages = await db.CoachMessages.AsNoTracking()
                .Where(msg => msg.ConversationId == id && msg.OwnerId == currentUser.UserId)
                .OrderBy(msg => msg.CreatedAt)
                .ToListAsync(cancellationToken);

            var responses = messages.Select(msg => new CoachMessageResponse(
                msg.Id,
                msg.ConversationId,
                msg.SenderRole,
                protector.Unprotect(currentUser.UserId, msg.ProtectedContent),
                msg.CreatedAt)).ToList();

            return Results.Ok(responses);
        });

        coach.MapPost("/conversations/{id:guid}/messages", async (
            Guid id,
            PostCoachMessageRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            IContentProtector protector,
            IAiGateway gateway,
            CancellationToken cancellationToken) =>
        {
            var conversation = await db.CoachConversations
                .SingleOrDefaultAsync(conv => conv.Id == id && conv.OwnerId == currentUser.UserId, cancellationToken);
            if (conversation is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Trim().Length > 4_000)
            {
                return Results.BadRequest(new { error = "Message content must be between 1 and 4,000 characters." });
            }

            var protectedUserContent = protector.Protect(currentUser.UserId, request.Content.Trim());
            var userMessage = new CoachMessage(
                currentUser.UserId,
                conversation.Id,
                CoachMessageSenderRole.User,
                protectedUserContent);
            db.CoachMessages.Add(userMessage);

            // Snapshot context for Coach AI
            var recentRoles = await db.LifeRoles.AsNoTracking()
                .Where(r => r.OwnerId == currentUser.UserId && !r.IsArchived)
                .Select(r => new { r.Name, r.DefaultLifeArea })
                .ToListAsync(cancellationToken);

            var recentTasks = await db.Tasks.AsNoTracking()
                .Where(t => t.OwnerId == currentUser.UserId && t.State == TaskState.Todo)
                .Take(20)
                .Select(t => new { t.Title, t.Quadrant, t.LifeArea })
                .ToListAsync(cancellationToken);

            var activeHabits = await db.Habits.AsNoTracking()
                .Where(h => h.OwnerId == currentUser.UserId && !h.IsArchived)
                .Select(h => new { h.Title, h.LifeArea, h.TargetPerWeek })
                .ToListAsync(cancellationToken);

            var payload = new
            {
                conversation_id = conversation.Id.ToString(),
                user_message = request.Content.Trim(),
                active_roles = recentRoles,
                open_tasks = recentTasks,
                active_habits = activeHabits,
            };

            string coachResponseText = "I have noted your update. Focus on your Q2 priorities for sustainable progress.";
            List<AiProposalResponse> createdProposals = [];
            List<CoachObservationResponse> createdObservations = [];

            try
            {
                var responseDoc = await gateway.InvokeAsync(
                    "/api/v1/ai/coach/respond",
                    payload,
                    currentUser.UserId,
                    "coach",
                    cancellationToken);

                if (responseDoc.RootElement.TryGetProperty("message_content", out var msgElement) &&
                    msgElement.ValueKind == JsonValueKind.String)
                {
                    coachResponseText = msgElement.GetString() ?? coachResponseText;
                }

                if (responseDoc.RootElement.TryGetProperty("observations", out var obsElement) &&
                    obsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var obs in obsElement.EnumerateArray())
                    {
                        var obsText = obs.GetProperty("content").GetString();
                        var scopeStr = obs.TryGetProperty("scope", out var s) ? s.GetString() : "HabitPattern";
                        if (!Enum.TryParse<CoachObservationScope>(scopeStr, true, out var scope))
                        {
                            scope = CoachObservationScope.HabitPattern;
                        }

                        if (!string.IsNullOrWhiteSpace(obsText))
                        {
                            var protectedObs = protector.Protect(currentUser.UserId, obsText);
                            var observation = new CoachObservation(currentUser.UserId, scope, protectedObs, conversation.Id);
                            db.CoachObservations.Add(observation);
                            createdObservations.Add(new CoachObservationResponse(
                                observation.Id,
                                observation.Scope,
                                obsText,
                                conversation.Id,
                                false,
                                observation.CreatedAt));
                        }
                    }
                }

                if (responseDoc.RootElement.TryGetProperty("proposals", out var propsElement) &&
                    propsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var prop in propsElement.EnumerateArray())
                    {
                        var kindStr = prop.GetProperty("kind").GetString();
                        var title = prop.GetProperty("title").GetString() ?? "Coach Recommendation";
                        var description = prop.GetProperty("description").GetString() ?? "";
                        var payloadJson = prop.GetProperty("payload").GetRawText();

                        if (Enum.TryParse<AiProposalKind>(kindStr, true, out var kind))
                        {
                            var sourceKey = $"coach:{conversation.Id}:{Guid.CreateVersion7()}";
                            var proposal = new AiActionProposal(
                                currentUser.UserId,
                                kind,
                                "coach",
                                sourceKey,
                                title,
                                description,
                                payloadJson);
                            db.AiActionProposals.Add(proposal);
                            createdProposals.Add(ToResponse(proposal));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fallback deterministic response when ai-api is unreachable
                coachResponseText = "Coach recommendation: Protect your high-impact Quadrant 2 time blocks and maintain your daily habits.";
            }

            var protectedCoachContent = protector.Protect(currentUser.UserId, coachResponseText);
            var coachMessage = new CoachMessage(
                currentUser.UserId,
                conversation.Id,
                CoachMessageSenderRole.Coach,
                protectedCoachContent);
            db.CoachMessages.Add(coachMessage);

            conversation.Touch();
            AddAudit(db, currentUser.UserId, "coach", "message_exchanged", "coach_conversation", conversation.Id);

            await db.SaveChangesAsync(cancellationToken);

            var userMsgResp = new CoachMessageResponse(
                userMessage.Id,
                userMessage.ConversationId,
                userMessage.SenderRole,
                request.Content.Trim(),
                userMessage.CreatedAt);

            var coachMsgResp = new CoachMessageResponse(
                coachMessage.Id,
                coachMessage.ConversationId,
                coachMessage.SenderRole,
                coachResponseText,
                coachMessage.CreatedAt);

            return Results.Ok(new CoachInteractionResponse(
                userMsgResp,
                coachMsgResp,
                createdObservations,
                createdProposals));
        });

        coach.MapGet("/observations", async (
            AppDbContext db,
            ICurrentUser currentUser,
            IContentProtector protector,
            CancellationToken cancellationToken) =>
        {
            var observations = await db.CoachObservations.AsNoTracking()
                .Where(obs => obs.OwnerId == currentUser.UserId && !obs.IsDismissed)
                .OrderByDescending(obs => obs.CreatedAt)
                .ToListAsync(cancellationToken);

            var responses = observations.Select(obs => new CoachObservationResponse(
                obs.Id,
                obs.Scope,
                protector.Unprotect(currentUser.UserId, obs.ProtectedContent),
                obs.ConversationId,
                obs.IsDismissed,
                obs.CreatedAt)).ToList();

            return Results.Ok(responses);
        });

        coach.MapPost("/observations/{id:guid}/dismiss", async (
            Guid id,
            AppDbContext db,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var observation = await db.CoachObservations
                .SingleOrDefaultAsync(obs => obs.Id == id && obs.OwnerId == currentUser.UserId, cancellationToken);
            if (observation is null)
            {
                return Results.NotFound();
            }

            observation.Dismiss();
            AddAudit(db, currentUser.UserId, "coach", "observation_dismissed", "coach_observation", observation.Id);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        });
    }
}
