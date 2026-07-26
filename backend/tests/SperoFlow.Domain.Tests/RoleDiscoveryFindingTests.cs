using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class RoleDiscoveryFindingTests
{
    [Fact]
    public void Finding_tracks_the_linked_proposal_and_requires_explicit_resolution()
    {
        var finding = new RoleDiscoveryFinding(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "protected-evidence");
        var originalToken = finding.ConcurrencyToken;

        finding.Cancel();

        Assert.Equal(RoleDiscoveryFindingState.Cancelled, finding.State);
        Assert.NotNull(finding.ResolvedAt);
        Assert.NotEqual(originalToken, finding.ConcurrencyToken);
        Assert.Throws<DomainValidationException>(() => finding.Approve());
    }

    [Fact]
    public void Finding_rejects_missing_proposal_link()
    {
        Assert.Throws<DomainValidationException>(() => new RoleDiscoveryFinding(
            Guid.CreateVersion7(),
            Guid.Empty,
            "protected-evidence"));
    }
}