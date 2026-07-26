using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Application;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static void MapRoleDiscovery(RouteGroupBuilder api)
    {
        var discovery = api.MapGroup("/ai/roles");

        discovery.MapGet("/pending", async (
            AppDbContext db,
            ICurrentUser currentUser,
            IContentProtector protector,
            CancellationToken cancellationToken) =>
        {
            var candidates = await GetRoleDiscoveryCandidatesAsync(
                db,
                currentUser.UserId,
                protector,
                proposalIds: null,
                cancellationToken);
            return Results.Ok(candidates);
        });

        discovery.MapPost("/discover", async (
            AppDbContext db,
            IRoleDiscoveryService roleDiscovery,
            ICurrentUser currentUser,
            IContentProtector protector,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await roleDiscovery.DiscoverAsync(currentUser.UserId, cancellationToken);
                var candidates = await GetRoleDiscoveryCandidatesAsync(
                    db,
                    currentUser.UserId,
                    protector,
                    result.ProposalIds,
                    cancellationToken);
                return Results.Ok(new RoleDiscoveryRunResponse(result.EvidenceCount, candidates));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "The role discovery service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (InvalidOperationException)
            {
                return Results.Problem(title: "The role discovery service returned an invalid response.", statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }

    private static async Task<IReadOnlyList<RoleDiscoveryCandidateResponse>> GetRoleDiscoveryCandidatesAsync(
        AppDbContext db,
        Guid ownerId,
        IContentProtector protector,
        IReadOnlyCollection<Guid>? proposalIds,
        CancellationToken cancellationToken)
    {
        if (proposalIds is { Count: 0 })
        {
            return [];
        }

        var findingsQuery = db.RoleDiscoveryFindings.AsNoTracking()
            .Where(finding => finding.OwnerId == ownerId && finding.State == RoleDiscoveryFindingState.Pending);
        if (proposalIds is { Count: > 0 })
        {
            findingsQuery = findingsQuery.Where(finding => proposalIds.Contains(finding.ProposalId));
        }

        var findings = await findingsQuery
            .OrderByDescending(finding => finding.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        if (findings.Count == 0)
        {
            return [];
        }

        var findingProposalIds = findings.Select(finding => finding.ProposalId).ToArray();
        var proposals = await db.AiActionProposals.AsNoTracking()
            .Where(proposal => proposal.OwnerId == ownerId
                && proposal.Kind == AiProposalKind.CreateLifeRole
                && proposal.Source == "role-discovery"
                && proposal.State == AiProposalState.Pending
                && findingProposalIds.Contains(proposal.Id))
            .ToListAsync(cancellationToken);
        var proposalsById = proposals.ToDictionary(proposal => proposal.Id);
        return findings
            .Where(finding => proposalsById.ContainsKey(finding.ProposalId))
            .Select(finding => ToRoleDiscoveryResponse(finding, proposalsById[finding.ProposalId], ownerId, protector))
            .ToArray();
    }

    private static RoleDiscoveryCandidateResponse ToRoleDiscoveryResponse(
        RoleDiscoveryFinding finding,
        AiActionProposal proposal,
        Guid ownerId,
        IContentProtector protector)
    {
        var decrypted = protector.Unprotect(ownerId, finding.ProtectedEvidence);
        var evidence = JsonSerializer.Deserialize<string[]>(decrypted, JsonOptions)
            ?.Where(value => !string.IsNullOrWhiteSpace(value))
            .Take(3)
            .ToArray()
            ?? [];
        return new RoleDiscoveryCandidateResponse(ToResponse(proposal), evidence);
    }
}