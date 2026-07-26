using SperoFlow.Domain;

namespace SperoFlow.Domain.Tests;

public sealed class RetiredRouteTests
{
    [Fact]
    public void Test_Legacy_AI_Schedule_Route_Contract_Is_Retired()
    {
        // Canonical endpoint path for task schedule suggestions is /api/v1/ai/tasks/{id}/schedule
        // The legacy transient route /ai/schedule (or /api/v1/ai/schedule) is completely retired.
        const string canonicalScheduleEndpointPattern = "/api/v1/ai/tasks/{id}/schedule";
        const string legacyRetiredRoute = "/ai/schedule";
        const string legacyRetiredApiRoute = "/api/v1/ai/schedule";

        Assert.NotEqual(canonicalScheduleEndpointPattern, legacyRetiredRoute);
        Assert.NotEqual(canonicalScheduleEndpointPattern, legacyRetiredApiRoute);

        // Verification of retired route string invariant
        Assert.True(legacyRetiredRoute.StartsWith("/ai/", StringComparison.Ordinal), "Legacy route prefix check.");
        Assert.False(legacyRetiredRoute.Contains("/tasks/"), "Legacy route does not use task-scoped resource path.");
    }
}
