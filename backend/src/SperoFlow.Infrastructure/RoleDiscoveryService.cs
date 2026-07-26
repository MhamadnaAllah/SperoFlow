using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Application;
using SperoFlow.Domain;

namespace SperoFlow.Infrastructure;

/// <summary>
/// Builds an owner-scoped, bounded evidence snapshot and turns validated AI candidates
/// into approval-gated role proposals. It never creates a life role directly.
/// </summary>
public sealed class RoleDiscoveryService : IRoleDiscoveryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db;
    private readonly IAiGateway _aiGateway;
    private readonly IContentProtector _protector;

    public RoleDiscoveryService(AppDbContext db, IAiGateway aiGateway, IContentProtector protector)
    {
        _db = db;
        _aiGateway = aiGateway;
        _protector = protector;
    }

    public async Task<RoleDiscoveryRunResult> DiscoverAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var existingRoles = await _db.LifeRoles.AsNoTracking()
            .Where(role => role.OwnerId == ownerId && !role.IsArchived)
            .OrderBy(role => role.Name)
            .Select(role => role.Name)
            .ToListAsync(cancellationToken);
        var signals = await BuildSignalsAsync(ownerId, cancellationToken);
        if (signals.Count < 2)
        {
            return new RoleDiscoveryRunResult(signals.Count, []);
        }

        using var response = await _aiGateway.InvokeAsync(
            "/api/roles/discover",
            new
            {
                existing_roles = existingRoles,
                signals = signals.Select(signal => new
                {
                    kind = signal.Kind,
                    label = signal.Label,
                    life_area = signal.LifeArea.ToString().ToLowerInvariant(),
                }).ToArray(),
            },
            ownerId,
            "ai.invoke",
            cancellationToken);
        var candidates = ParseCandidates(response.RootElement, signals, existingRoles);
        if (candidates.Count == 0)
        {
            return new RoleDiscoveryRunResult(signals.Count, []);
        }

        var sourceKeys = candidates.Select(candidate => SourceKey(candidate.Name)).ToArray();
        var existingProposals = await _db.AiActionProposals.AsNoTracking()
            .Where(proposal => proposal.OwnerId == ownerId && sourceKeys.Contains(proposal.SourceKey))
            .Select(proposal => new { proposal.Id, proposal.SourceKey, proposal.State })
            .ToListAsync(cancellationToken);
        var knownProposals = existingProposals.ToDictionary(value => value.SourceKey, StringComparer.Ordinal);
        var proposalIds = new List<Guid>();
        var nextSortOrder = await GetNextRoleSortOrderAsync(ownerId, cancellationToken);

        foreach (var candidate in candidates)
        {
            var sourceKey = SourceKey(candidate.Name);
            if (knownProposals.TryGetValue(sourceKey, out var existing))
            {
                if (existing.State == AiProposalState.Pending)
                {
                    proposalIds.Add(existing.Id);
                }

                continue;
            }

            var (color, icon) = Presentation(candidate.LifeArea);
            var sortOrder = nextSortOrder;
            nextSortOrder += 1_000;
            var payload = JsonSerializer.Serialize(new
            {
                name = candidate.Name,
                category = LifeRoleCategory.External,
                defaultLifeArea = candidate.LifeArea,
                color,
                icon,
                sortOrder,
            }, JsonOptions);
            var proposal = new AiActionProposal(
                ownerId,
                AiProposalKind.CreateLifeRole,
                "role-discovery",
                sourceKey,
                "Add role: " + candidate.Name,
                "A repeated pattern in your workspace may represent this role. Review the evidence before approving.",
                payload);
            var evidence = candidate.Evidence.Select(signal => signal.Kind + ": " + signal.Label).ToArray();
            var finding = new RoleDiscoveryFinding(
                ownerId,
                proposal.Id,
                _protector.Protect(ownerId, JsonSerializer.Serialize(evidence, JsonOptions)));
            _db.AiActionProposals.Add(proposal);
            _db.RoleDiscoveryFindings.Add(finding);
            _db.AuditEvents.Add(new AuditEvent(ownerId, "role_discovery", "proposal_created", "role_discovery_finding", finding.Id, "{}"));
            proposalIds.Add(proposal.Id);
            knownProposals[sourceKey] = new { proposal.Id, proposal.SourceKey, proposal.State };
        }

        if (proposalIds.Count == 0)
        {
            return new RoleDiscoveryRunResult(signals.Count, []);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            var persisted = await _db.AiActionProposals.AsNoTracking()
                .Where(proposal => proposal.OwnerId == ownerId
                    && proposal.State == AiProposalState.Pending
                    && sourceKeys.Contains(proposal.SourceKey))
                .Select(proposal => proposal.Id)
                .ToListAsync(cancellationToken);
            return new RoleDiscoveryRunResult(signals.Count, persisted);
        }

        return new RoleDiscoveryRunResult(signals.Count, proposalIds);
    }

    private async Task<List<RoleDiscoverySignal>> BuildSignalsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var tasks = await _db.Tasks.AsNoTracking()
            .Where(task => task.OwnerId == ownerId
                && task.RoleId == null
                && task.State != TaskState.Cancelled)
            .OrderByDescending(task => task.UpdatedAt)
            .Take(48)
            .Select(task => new RoleDiscoverySignal("Task", task.Title, task.LifeArea))
            .ToListAsync(cancellationToken);
        var projects = await _db.Projects.AsNoTracking()
            .Where(project => project.OwnerId == ownerId && project.State == ProjectState.Active)
            .OrderByDescending(project => project.UpdatedAt)
            .Take(16)
            .Select(project => new RoleDiscoverySignal("Project", project.Name, LifeArea.Personal))
            .ToListAsync(cancellationToken);
        var habits = await _db.Habits.AsNoTracking()
            .Where(habit => habit.OwnerId == ownerId && !habit.IsArchived)
            .OrderByDescending(habit => habit.UpdatedAt)
            .Take(16)
            .Select(habit => new RoleDiscoverySignal("Habit", habit.Title, habit.LifeArea))
            .ToListAsync(cancellationToken);

        return tasks.Concat(projects).Concat(habits)
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Label))
            .Take(80)
            .ToList();
    }

    private static List<RoleDiscoveryCandidate> ParseCandidates(
        JsonElement response,
        IReadOnlyList<RoleDiscoverySignal> signals,
        IReadOnlyCollection<string> existingRoles)
    {
        if (response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("candidates", out var candidatesValue)
            || candidatesValue.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Role discovery returned an invalid response.");
        }

        var knownNames = existingRoles.Select(Canonicalize).ToHashSet(StringComparer.Ordinal);
        var candidates = new List<RoleDiscoveryCandidate>();
        foreach (var value in candidatesValue.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty("name", out var nameValue)
                || !value.TryGetProperty("lifeArea", out var lifeAreaValue)
                || !value.TryGetProperty("confidence", out var confidenceValue)
                || !value.TryGetProperty("evidenceIndexes", out var indexesValue)
                || nameValue.ValueKind != JsonValueKind.String
                || lifeAreaValue.ValueKind != JsonValueKind.String
                || !confidenceValue.TryGetDouble(out var confidence)
                || indexesValue.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var name = nameValue.GetString()?.Trim() ?? string.Empty;
            if (name.Length is < 1 or > 160
                || confidence < 0.65
                || !Enum.TryParse<LifeArea>(lifeAreaValue.GetString(), ignoreCase: true, out var lifeArea))
            {
                continue;
            }

            var canonicalName = Canonicalize(name);
            if (canonicalName.Length == 0 || !knownNames.Add(canonicalName))
            {
                continue;
            }

            var evidence = indexesValue.EnumerateArray()
                .Where(index => index.TryGetInt32(out var parsed) && parsed >= 0 && parsed < signals.Count)
                .Select(index => signals[index.GetInt32()])
                .DistinctBy(signal => signal.Kind + "\u001f" + signal.Label)
                .Take(3)
                .ToArray();
            if (evidence.Length < 2)
            {
                continue;
            }

            candidates.Add(new RoleDiscoveryCandidate(name, lifeArea, evidence));
            if (candidates.Count == 5)
            {
                break;
            }
        }

        return candidates;
    }

    private async Task<int> GetNextRoleSortOrderAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var maximum = await _db.LifeRoles.AsNoTracking()
            .Where(role => role.OwnerId == ownerId)
            .Select(role => (int?)role.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;
        return maximum + 1_000;
    }

    private static string SourceKey(string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(name)));
        return "role-discovery:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Canonicalize(string value) =>
        new(value.Trim().Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static (string Color, string Icon) Presentation(LifeArea lifeArea) => lifeArea switch
    {
        LifeArea.Work => ("#0053dc", "work"),
        LifeArea.Family => ("#dc2626", "family_restroom"),
        LifeArea.Physical => ("#dc2626", "fitness_center"),
        LifeArea.Spiritual => ("#a16207", "self_improvement"),
        LifeArea.Social => ("#047857", "groups"),
        LifeArea.Learning => ("#7c3aed", "school"),
        _ => ("#c2410c", "person"),
    };

    private sealed record RoleDiscoverySignal(string Kind, string Label, LifeArea LifeArea);

    private sealed record RoleDiscoveryCandidate(string Name, LifeArea LifeArea, IReadOnlyList<RoleDiscoverySignal> Evidence);
}