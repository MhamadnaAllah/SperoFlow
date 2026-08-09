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

if (builder.Environment.IsProduction() ||
    string.Equals(builder.Configuration["LOG_FORMAT"], "json", StringComparison.OrdinalIgnoreCase))
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
    // Trust only the private/Docker networks the reverse proxy (Caddy) lives on.
    // The default loopback-only list does not include the container proxy, so its
    // X-Forwarded-Proto was ignored and requests looked like plain HTTP, which broke
    // the antiforgery SecurePolicy=Always check (500 on /auth/csrf).
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse("10.0.0.0"), 8));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse("172.16.0.0"), 12));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse("192.168.0.0"), 16));
    options.KnownIPNetworks.Add(new System.Net.IPNetwork(System.Net.IPAddress.Parse("169.254.0.0"), 16));
    options.ForwardLimit = null;
});
builder.Services.AddAntiforgery(options =>
{
    var relaxSecureCookie = string.Equals(
        builder.Configuration["Security:RelaxAntiforgerySecureCookie"],
        "true",
        StringComparison.OrdinalIgnoreCase);

    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = relaxSecureCookie ? "speroflow-xsrf" : "__Host-speroflow-xsrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = relaxSecureCookie ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
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
var trustedHttpsHosts = GetTrustedHttpsHosts(app.Configuration);

app.UseForwardedHeaders();
app.Use((context, next) =>
{
    if (!context.Request.IsHttps &&
        (IsTrustedHttpsHost(context.Request.Host.Host, trustedHttpsHosts) ||
         string.Equals(context.Request.Headers["X-Forwarded-Proto"], "https", StringComparison.OrdinalIgnoreCase)))
    {
        context.Request.Scheme = Uri.UriSchemeHttps;
    }

    return next(context);
});
app.UseExceptionHandler();
app.UseSperoFlowRequestObservability();
app.UseHttpsRedirection();
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

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "healthy",
    service = "speroflow-api",
    utc = DateTimeOffset.UtcNow
})).AllowAnonymous();
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var connected = await db.Database.CanConnectAsync(cancellationToken);
    return connected
        ? Results.Ok(new { status = "ready", service = "speroflow-api", checks = new { postgres = "up" } })
        : Results.Problem(title: "PostgreSQL is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
}).AllowAnonymous();

// Private-network scrape only. Caddy must not publish /metrics on the public host.
app.MapGet("/metrics", () => Results.Text(RequestMetrics.RenderPrometheus("speroflow-api"), "text/plain; version=0.0.4; charset=utf-8"))
    .AllowAnonymous();

app.MapSperoFlowEndpoints();
app.MapSperoFlowOidcServer();

app.Run();

public partial class Program
{
    private static HashSet<string> GetTrustedHttpsHosts(IConfiguration configuration)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddConfiguredHost(hosts, configuration["Accounts:PublicWebOrigin"]);
        AddConfiguredHost(hosts, configuration["IdentityServer:Issuer"]);
        AddConfiguredHostList(hosts, configuration["PublicIngress:TrustedHttpsHosts"]);
        return hosts;
    }

    private static bool IsTrustedHttpsHost(string? host, HashSet<string> trustedHosts) =>
        !string.IsNullOrWhiteSpace(host) && trustedHosts.Contains(host);

    private static void AddConfiguredHostList(HashSet<string> hosts, string? value)
    {
        foreach (var item in (value ?? string.Empty).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddConfiguredHost(hosts, item);
        }
    }

    private static void AddConfiguredHost(HashSet<string> hosts, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            candidate = uri.Host;
        }

        hosts.Add(candidate);
    }
}
