using Microsoft.AspNetCore.Antiforgery;

namespace SperoFlow.Api;

public sealed class AntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return await next(context);
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
            return await next(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                title: "Invalid CSRF token.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://speroflow.dev/problems/invalid-csrf-token");
        }
    }
}
