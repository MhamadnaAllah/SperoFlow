using SperoFlow.Application;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static void MapKnowledgeCatalog(RouteGroupBuilder api)
    {
        api.MapGet("/knowledge-datasets", async (
            IKnowledgePlatformGateway knowledgePlatform,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await knowledgePlatform.ListCatalogAsync(currentUser.UserId, cancellationToken));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "Knowledge catalog is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }
}