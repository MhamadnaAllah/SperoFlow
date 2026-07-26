using System.Collections.Immutable;
using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static class OidcServerExtensions
{
    public static IServiceCollection AddSperoFlowOidcServer(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(IdentityServerOptions.SectionName).Get<IdentityServerOptions>() ?? new IdentityServerOptions();
        if (!options.Enabled)
        {
            return services;
        }

        var signingCertificate = IdentityServerCertificateLoader.Load(options.SigningCertificatePath, options.SigningCertificatePasswordPath, "signing");
        var encryptionCertificate = IdentityServerCertificateLoader.Load(options.EncryptionCertificatePath, options.EncryptionCertificatePasswordPath, "encryption");

        services.AddOpenIddict()
            .AddCore(openIddict =>
            {
                openIddict.UseEntityFrameworkCore()
                    .UseDbContext<AppDbContext>();
            })
            .AddServer(openIddict =>
            {
                openIddict.SetIssuer(new Uri(options.Issuer, UriKind.Absolute))
                    .SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .AllowAuthorizationCodeFlow()
                    .RequireProofKeyForCodeExchange()
                    .AddSigningCertificate(signingCertificate)
                    .AddEncryptionCertificate(encryptionCertificate);

                openIddict.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough();
            });

        services.AddHostedService<KnowledgePortalOidcClientSeeder>();
        return services;
    }

    public static void MapSperoFlowOidcServer(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<IdentityServerOptions>>().Value;
        if (!options.Enabled)
        {
            return;
        }

        app.MapMethods("/connect/authorize", [HttpMethods.Get, HttpMethods.Post], async (
            HttpContext context,
            UserManager<ApplicationUser> userManager) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                return Results.Redirect("/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
            }

            var user = await userManager.GetUserAsync(context.User);
            if (user is null || !user.IsActive || !user.EmailConfirmed)
            {
                return Results.Forbid();
            }

            var request = context.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("OpenID Connect authorization request is unavailable.");

            var roles = (await userManager.GetRolesAsync(user)).ToImmutableArray();
            var identity = new ClaimsIdentity(
                TokenValidationParameters.DefaultAuthenticationType,
                OpenIddictConstants.Claims.Name,
                OpenIddictConstants.Claims.Role);
            identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture));
            identity.SetClaim(OpenIddictConstants.Claims.Name, user.DisplayName ?? user.Email ?? user.UserName ?? user.Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture));
            identity.SetClaim(OpenIddictConstants.Claims.Email, user.Email);
            identity.SetClaims(OpenIddictConstants.Claims.Role, roles);

            var permittedScopes = request.GetScopes().Where(scope => scope is
                OpenIddictConstants.Scopes.OpenId or
                OpenIddictConstants.Scopes.Profile or
                OpenIddictConstants.Scopes.Email or
                OpenIddictConstants.Scopes.Roles);
            identity.SetScopes(permittedScopes);
            identity.SetDestinations(static _ =>
            [
                OpenIddictConstants.Destinations.AccessToken,
                OpenIddictConstants.Destinations.IdentityToken,
            ]);

            return Results.SignIn(
                new ClaimsPrincipal(identity),
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }).AllowAnonymous();
    }
}

internal sealed class KnowledgePortalOidcClientSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<IdentityServerOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        if (!configuration.Enabled)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = configuration.KnowledgePortalClientId,
            DisplayName = "SperoFlow Knowledge Portal",
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            RedirectUris = { new Uri(configuration.KnowledgePortalRedirectUri, UriKind.Absolute) },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Profile,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Email,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Roles,
            },
            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange,
            },
        };

        var existing = await applications.FindByClientIdAsync(configuration.KnowledgePortalClientId, cancellationToken);
        if (existing is null)
        {
            await applications.CreateAsync(descriptor, cancellationToken);
            return;
        }

        await applications.UpdateAsync(existing, descriptor, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static class IdentityServerCertificateLoader
{
    public static System.Security.Cryptography.X509Certificates.X509Certificate2 Load(string certificatePath, string passwordPath, string purpose)
    {
        if (!File.Exists(certificatePath) || !File.Exists(passwordPath))
        {
            throw new InvalidOperationException($"The OpenID Connect {purpose} certificate or password secret is not mounted.");
        }

        var password = File.ReadAllText(passwordPath).Trim();
        var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
            certificatePath,
            password,
            System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException($"The OpenID Connect {purpose} certificate must contain a private key.");
        }

        return certificate;
    }
}