using Microsoft.EntityFrameworkCore;
using SperoFlow.Application;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static void MapJournalInsights(RouteGroupBuilder api)
    {
        var journal = api.MapGroup("/ai/journal");

        journal.MapGet("/pending", async (
            AppDbContext db,
            ICurrentUser currentUser,
            IContentProtector protector,
            CancellationToken cancellationToken) =>
        {
            var insights = await db.JournalInsights.AsNoTracking()
                .Where(value => value.OwnerId == currentUser.UserId && value.State == JournalInsightState.Pending)
                .OrderByDescending(value => value.CreatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);
            if (insights.Count == 0)
            {
                return Results.Ok(Array.Empty<JournalAnalysisResponse>());
            }

            var entryIds = insights.Select(value => value.JournalEntryId).Distinct().ToArray();
            var currentRevisions = await db.JournalEntries.AsNoTracking()
                .Where(value => value.OwnerId == currentUser.UserId && entryIds.Contains(value.Id))
                .Select(value => new { value.Id, value.ConcurrencyToken })
                .ToDictionaryAsync(value => value.Id, value => value.ConcurrencyToken, cancellationToken);
            var currentInsights = insights
                .Where(insight => currentRevisions.GetValueOrDefault(insight.JournalEntryId) == insight.SourceConcurrencyToken)
                .ToArray();
            if (currentInsights.Length == 0)
            {
                return Results.Ok(Array.Empty<JournalAnalysisResponse>());
            }

            var sourceKeys = currentInsights
                .Select(insight => JournalInsightProposalSourceKey(insight.JournalEntryId, insight.SourceConcurrencyToken))
                .ToArray();
            var proposals = await db.AiActionProposals.AsNoTracking()
                .Where(value => value.OwnerId == currentUser.UserId
                    && value.Kind == AiProposalKind.ApplyJournalInsight
                    && value.State == AiProposalState.Pending
                    && sourceKeys.Contains(value.SourceKey))
                .ToListAsync(cancellationToken);
            var proposalsByKey = proposals.ToDictionary(value => value.SourceKey, StringComparer.Ordinal);
            var values = currentInsights
                .Select(insight => new
                {
                    Insight = insight,
                    Proposal = proposalsByKey.GetValueOrDefault(
                        JournalInsightProposalSourceKey(insight.JournalEntryId, insight.SourceConcurrencyToken)),
                })
                .Where(value => value.Proposal is not null)
                .Select(value => new JournalAnalysisResponse(
                    ToResponse(value.Proposal!),
                    ToResponse(value.Insight, currentUser.UserId, protector)))
                .ToArray();
            return Results.Ok(values);
        });

        journal.MapPost("/{id:guid}/analyze", async (
            Guid id,
            AppDbContext db,
            ICurrentUser currentUser,
            IContentProtector protector,
            IAiGateway gateway,
            CancellationToken cancellationToken) =>
        {
            var entry = await db.JournalEntries.SingleOrDefaultAsync(
                value => value.Id == id && value.OwnerId == currentUser.UserId,
                cancellationToken);
            if (entry is null)
            {
                return Results.NotFound();
            }

            var existing = await FindJournalAnalysisAsync(
                db,
                currentUser.UserId,
                entry.Id,
                entry.ConcurrencyToken,
                protector,
                cancellationToken);
            if (existing is not null)
            {
                return Results.Ok(existing);
            }

            var priorEntries = await db.JournalEntries.AsNoTracking()
                .Where(value => value.OwnerId == currentUser.UserId && value.Id != entry.Id)
                .OrderByDescending(value => value.CreatedAt)
                .Take(7)
                .ToListAsync(cancellationToken);
            var aiPayload = new
            {
                current_entry = new
                {
                    content = LimitJournalText(protector.Unprotect(currentUser.UserId, entry.ProtectedContent), 6_000),
                    mood = entry.Mood,
                },
                prior_entries = priorEntries
                    .OrderBy(value => value.CreatedAt)
                    .Select(value => new
                    {
                        content = LimitJournalText(protector.Unprotect(currentUser.UserId, value.ProtectedContent), 2_500),
                        mood = value.Mood,
                    })
                    .ToArray(),
            };

            JournalReflectionPayload reflection;
            try
            {
                using var aiResponse = await gateway.InvokeAsync(
                    "/api/journal/analyze",
                    aiPayload,
                    currentUser.UserId,
                    "ai.invoke",
                    cancellationToken);
                reflection = ParseJournalReflection(aiResponse.RootElement);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "The reflection service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (InvalidOperationException)
            {
                return Results.Problem(title: "The reflection service returned an invalid response.", statusCode: StatusCodes.Status502BadGateway);
            }

            var protectedPayload = protector.Protect(
                currentUser.UserId,
                JsonSerializer.Serialize(reflection, JsonOptions));
            var insight = new JournalInsight(
                currentUser.UserId,
                entry.Id,
                entry.ConcurrencyToken,
                protectedPayload);
            var sourceKey = JournalInsightProposalSourceKey(entry.Id, entry.ConcurrencyToken);
            var proposal = new AiActionProposal(
                currentUser.UserId,
                AiProposalKind.ApplyJournalInsight,
                "journal",
                sourceKey,
                "Review your journal reflection",
                "A reflection is ready for your review. Approve it to keep it with this entry.",
                JsonSerializer.Serialize(
                    new JournalInsightProposalPayload(insight.Id, entry.Id, entry.ConcurrencyToken),
                    JsonOptions));
            db.JournalInsights.Add(insight);
            db.AiActionProposals.Add(proposal);
            AddAudit(db, currentUser.UserId, "journal", "insight_generated", "journal_insight", insight.Id);
            AddAudit(db, currentUser.UserId, "ai_proposal", "created", "ai_action_proposal", proposal.Id);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                var persisted = await FindJournalAnalysisAsync(
                    db,
                    currentUser.UserId,
                    id,
                    entry.ConcurrencyToken,
                    protector,
                    cancellationToken);
                if (persisted is not null)
                {
                    return Results.Ok(persisted);
                }

                return Results.Conflict(new { error = "A reflection is already being prepared for this entry. Refresh and try again." });
            }

            return Results.Ok(new JournalAnalysisResponse(
                ToResponse(proposal),
                ToResponse(insight, currentUser.UserId, protector)));
        });
    }

    private static async Task<JournalAnalysisResponse?> FindJournalAnalysisAsync(
        AppDbContext db,
        Guid ownerId,
        Guid journalEntryId,
        Guid sourceConcurrencyToken,
        IContentProtector protector,
        CancellationToken cancellationToken)
    {
        var insight = await db.JournalInsights.AsNoTracking().SingleOrDefaultAsync(
            value => value.OwnerId == ownerId
                && value.JournalEntryId == journalEntryId
                && value.SourceConcurrencyToken == sourceConcurrencyToken,
            cancellationToken);
        if (insight is null)
        {
            return null;
        }

        var proposal = await db.AiActionProposals.AsNoTracking().SingleOrDefaultAsync(
            value => value.OwnerId == ownerId
                && value.Kind == AiProposalKind.ApplyJournalInsight
                && value.SourceKey == JournalInsightProposalSourceKey(journalEntryId, sourceConcurrencyToken),
            cancellationToken);
        return proposal is null
            ? null
            : new JournalAnalysisResponse(ToResponse(proposal), ToResponse(insight, ownerId, protector));
    }

    private static string JournalInsightProposalSourceKey(Guid journalEntryId, Guid sourceConcurrencyToken) =>
        $"journal-insight:{journalEntryId:N}:{sourceConcurrencyToken:N}";

    private static JournalInsightResponse ToResponse(JournalInsight insight, Guid ownerId, IContentProtector protector)
    {
        using var payload = JsonDocument.Parse(protector.Unprotect(ownerId, insight.ProtectedPayload));
        var reflection = ParseJournalReflection(payload.RootElement);
        return new JournalInsightResponse(
            insight.Id,
            insight.State,
            reflection.Emotions,
            reflection.Feedback,
            reflection.ProgressSummary,
            insight.SourceConcurrencyToken,
            insight.CreatedAt,
            insight.ResolvedAt);
    }

    private static JournalReflectionPayload ParseJournalReflection(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Journal reflection payload must be an object.");
        }

        if (!response.TryGetProperty("emotions", out var emotionsValue)
            || emotionsValue.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Journal reflection is missing emotions.");
        }

        var emotions = emotionsValue.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 80)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        if (emotions.Length == 0)
        {
            throw new InvalidOperationException("Journal reflection must contain at least one emotion.");
        }

        return new JournalReflectionPayload(
            emotions,
            ReadJournalReflectionText(response, "feedback"),
            ReadJournalReflectionText(response, "progressSummary"));
    }

    private static string ReadJournalReflectionText(JsonElement response, string propertyName)
    {
        if (!response.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Journal reflection is missing {propertyName}.");
        }

        var text = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 600)
        {
            throw new InvalidOperationException($"Journal reflection {propertyName} is invalid.");
        }

        return text;
    }

    private static string LimitJournalText(string content, int maximumLength) =>
        content.Length <= maximumLength ? content : content[..maximumLength];

    private sealed record JournalReflectionPayload(
        IReadOnlyList<string> Emotions,
        string Feedback,
        string ProgressSummary);
}