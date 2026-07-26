using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SperoFlow.Knowledge.Infrastructure;

namespace SperoFlow.Knowledge.Api;

public static class KnowledgePortalAuthentication
{
    public const string CookieScheme = "knowledge-portal-cookie";
    public const string OidcScheme = "knowledge-portal-oidc";

    public static IServiceCollection AddKnowledgePortalAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetRequiredSection(KnowledgeOidcOptions.SectionName).Get<KnowledgeOidcOptions>()
            ?? throw new InvalidOperationException("KnowledgeOidc configuration is required.");

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionKeysDirectory))
            .ProtectKeysWithCertificate(KnowledgePortalCertificateLoader.Load(options))
            .SetApplicationName("SperoFlow.Knowledge.Portal");

        services.AddAuthentication(authentication =>
            {
                authentication.DefaultAuthenticateScheme = CookieScheme;
                authentication.DefaultChallengeScheme = CookieScheme;
                authentication.DefaultSignInScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, cookie =>
            {
                cookie.Cookie.Name = "__Host-speroflow-knowledge";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.Path = "/";
                cookie.SlidingExpiration = true;
                cookie.ExpireTimeSpan = TimeSpan.FromHours(4);
                cookie.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                cookie.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(OidcScheme, oidc =>
            {
                oidc.Authority = options.Authority;
                oidc.ClientId = options.ClientId;
                oidc.CallbackPath = options.CallbackPath;
                oidc.SignedOutCallbackPath = options.SignedOutCallbackPath;
                oidc.ResponseType = OpenIdConnectResponseType.Code;
                oidc.UsePkce = true;
                oidc.SaveTokens = false;
                oidc.GetClaimsFromUserInfoEndpoint = false;
                oidc.MapInboundClaims = false;
                oidc.RequireHttpsMetadata = options.RequireHttpsMetadata;
                oidc.Scope.Clear();
                oidc.Scope.Add("openid");
                oidc.Scope.Add("profile");
                oidc.Scope.Add("email");
                oidc.Scope.Add("roles");
                oidc.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };
                oidc.Events.OnTokenValidated = context =>
                {
                    var subject = context.Principal?.FindFirst("sub")?.Value;
                    if (string.IsNullOrWhiteSpace(subject))
                    {
                        context.Fail("The central identity token did not contain an immutable subject.");
                        return Task.CompletedTask;
                    }

                    var roles = context.Principal!.FindAll("role").Select(claim => claim.Value)
                        .Concat(context.Principal.FindAll(ClaimTypes.Role).Select(claim => claim.Value));
                    if (!roles.Any(role => role is "KnowledgeOwner" or "KnowledgeAdmin" or "Admin"))
                    {
                        context.Fail("Knowledge portal access has not been assigned to this account.");
                    }

                    return Task.CompletedTask;
                };
            });

        return services;
    }

    public static void MapKnowledgePortalSessionEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/login", (HttpRequest request) =>
            Results.Challenge(
                new AuthenticationProperties { RedirectUri = LocalReturnUrl(request.Query["returnUrl"]) },
                [OidcScheme]))
            .AllowAnonymous()
            .RequireRateLimiting("portal");

        app.MapGet("/auth/me", (ClaimsPrincipal user) =>
        {
            var subject = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                return Results.Unauthorized();
            }

            var roles = user.FindAll("role").Select(claim => claim.Value)
                .Concat(user.FindAll(ClaimTypes.Role).Select(claim => claim.Value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return Results.Ok(new KnowledgePortalSession(subject, user.Identity?.Name, roles));
        }).RequireAuthorization().RequireRateLimiting("portal");

        app.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new KnowledgeCsrfToken(tokens.RequestToken ?? string.Empty));
        }).AllowAnonymous().RequireRateLimiting("portal");

        app.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieScheme);
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("portal").AddEndpointFilter<KnowledgeAntiforgeryValidationFilter>();
    }

    private static string LocalReturnUrl(string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        candidate.StartsWith('/') &&
        !candidate.StartsWith("//", StringComparison.Ordinal)
            ? candidate
            : "/";
}

public sealed class KnowledgeAntiforgeryValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
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

public sealed record KnowledgePortalSession(string Subject, string? Name, IReadOnlyList<string> Roles);

public sealed record KnowledgeCsrfToken(string Token);

internal static class KnowledgePortalCertificateLoader
{
    public static X509Certificate2 Load(KnowledgeOidcOptions options)
    {
        if (!File.Exists(options.DataProtectionCertificatePath) || !File.Exists(options.DataProtectionCertificatePasswordPath))
        {
            throw new InvalidOperationException("Knowledge portal data-protection certificate secrets are not mounted.");
        }

        var password = File.ReadAllText(options.DataProtectionCertificatePasswordPath).Trim();
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            options.DataProtectionCertificatePath,
            password,
            X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException("Knowledge portal data-protection certificate must contain a private key.");
        }

        return certificate;
    }
}
