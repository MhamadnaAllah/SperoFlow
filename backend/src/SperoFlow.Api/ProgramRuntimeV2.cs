using System.Data;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SperoFlow.Api;
using SperoFlow.Application;
using SperoFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.ForwardLimit = 1;
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-speroflow-xsrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 8;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.AddFixedWindowLimiter("api", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<AntiforgeryValidationFilter>();
builder.Services.AddSperoFlowInfrastructure(builder.Configuration);
builder.Services.AddSperoFlowAccountMessaging(builder.Configuration);
builder.Services.AddSperoFlowOidcServer(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("admin", policy => policy.RequireRole("Admin"));
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseHttpsRedirection();

// Endpoint filters protect the authenticated group. This closes the same
// browser-CSRF requirement over anonymous mutations such as email confirmation.
app.Use(async (context, next) =>
{
    var request = context.Request;
    if (request.Path.StartsWithSegments("/api/v1") &&
        !HttpMethods.IsGet(request.Method) &&
        !HttpMethods.IsHead(request.Method) &&
        !HttpMethods.IsOptions(request.Method))
    {
        try
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            await Results.Problem(
                title: "Invalid CSRF token.",
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://speroflow.dev/problems/invalid-csrf-token").ExecuteAsync(context);
            return;
        }
    }

    await next(context);
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Identity's EF stores and the application context are scoped to this request.
// Wrap confirmation so role creation, membership, bootstrap completion, and the
// audit record commit together or all roll back. Registration itself only reserves
// the one-time bootstrap record; promotion happens after confirmed email.
app.Use(async (context, next) =>
{
    if (!HttpMethods.IsPost(context.Request.Method) ||
        !string.Equals(context.Request.Path.Value, "/api/v1/auth/confirm-email", StringComparison.OrdinalIgnoreCase))
    {
        await next(context);
        return;
    }

    var db = context.RequestServices.GetRequiredService<AppDbContext>();
    await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, context.RequestAborted);
    try
    {
        await next(context);
        if (context.Response.StatusCode is >= StatusCodes.Status200OK and < StatusCodes.Status400BadRequest)
        {
            await transaction.CommitAsync(context.RequestAborted);
        }
        else
        {
            await transaction.RollbackAsync(context.RequestAborted);
        }
    }
    catch
    {
        await transaction.RollbackAsync(CancellationToken.None);
        throw;
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy", service = "speroflow-api" })).AllowAnonymous();
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var connected = await db.Database.CanConnectAsync(cancellationToken);
    return connected
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(title: "PostgreSQL is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapSperoFlowEndpoints();
app.MapSperoFlowOidcServer();

app.Run();

public partial class Program
{
}
