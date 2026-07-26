using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using SperoFlow.Knowledge.Api;
using SperoFlow.Knowledge.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsProduction() || string.Equals(builder.Configuration["LOG_FORMAT"], "json", StringComparison.OrdinalIgnoreCase))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole();
}

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
    options.Cookie.Name = "__Host-speroflow-knowledge-xsrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("portal", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});
builder.Services.AddKnowledgeInfrastructure(builder.Configuration);
builder.Services.AddKnowledgePortalAuthentication(builder.Configuration);
builder.Services.AddScoped<KnowledgeAntiforgeryValidationFilter>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("knowledge-owner", policy => policy.RequireRole("KnowledgeOwner", "KnowledgeAdmin", "Admin"));
    options.AddPolicy("knowledge-admin", policy => policy.RequireRole("KnowledgeAdmin", "Admin"));
});

var app = builder.Build();
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<KnowledgeDbContext>();
    await db.Database.MigrateAsync();
    return;
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy", service = "speroflow-knowledge-api" })).AllowAnonymous();
app.MapGet("/health/ready", async (KnowledgeDbContext db, CancellationToken cancellationToken) =>
{
    var connected = await db.Database.CanConnectAsync(cancellationToken);
    return connected ? Results.Ok(new { status = "ready" }) : Results.Problem(title: "Knowledge PostgreSQL is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

app.MapKnowledgePortalSessionEndpoints();
KnowledgeEndpoints.Map(app);
app.Run();

public partial class Program;
