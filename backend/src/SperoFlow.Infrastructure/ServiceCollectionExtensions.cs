using Amazon;
using Amazon.S3;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using StackExchange.Redis;
using SperoFlow.Application;

namespace SperoFlow.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSperoFlowInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var postgresConnection = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        var objectStorageOptions = configuration.GetSection(ObjectStorageOptions.SectionName).Get<ObjectStorageOptions>()
            ?? new ObjectStorageOptions();

        services.AddOptions<ObjectStorageOptions>()
            .Bind(configuration.GetSection(ObjectStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AiServiceOptions>()
            .Bind(configuration.GetSection(AiServiceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<KnowledgePlatformOptions>()
            .Bind(configuration.GetSection(KnowledgePlatformOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<RedisOptions>()
            .Bind(configuration.GetSection(RedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<LegacyKnowledgeIngestionOptions>()
            .Bind(configuration.GetSection(LegacyKnowledgeIngestionOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<RoleDiscoveryOptions>()
            .Bind(configuration.GetSection(RoleDiscoveryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<ServiceJwtOptions>()
            .Bind(configuration.GetSection(ServiceJwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<AccountOptions>()
            .Bind(configuration.GetSection(AccountOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<IdentityServerOptions>()
            .Bind(configuration.GetSection(IdentityServerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(postgresConnection, npgsql => npgsql.EnableRetryOnFailure());
            options.UseOpenIddict();
            options.EnableDetailedErrors(false);
            options.EnableSensitiveDataLogging(false);
        });

        services.AddDataProtection()
            .PersistKeysToDbContext<AppDbContext>()
            .SetApplicationName("SperoFlow");

        var accountOptions = configuration.GetSection(AccountOptions.SectionName).Get<AccountOptions>() ?? new AccountOptions();
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = accountOptions.RequireConfirmedEmail;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddScoped<IPasswordHasher<ApplicationUser>, Argon2PasswordHasher>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-speroflow";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        if (objectStorageOptions.UsesS3)
        {
            services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.GetBySystemName(objectStorageOptions.Region),
            }));
            services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        }
        else
        {
            services.AddSingleton<IMinioClient>(provider =>
            {
                var options = provider.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;
                var builder = new MinioClient()
                    .WithEndpoint(options.Endpoint)
                    .WithCredentials(options.AccessKey, options.SecretKey);
                if (options.UseSsl)
                {
                    builder = builder.WithSSL();
                }

                return builder.Build();
            });
            services.AddSingleton<IObjectStorage, MinioObjectStorage>();
        }

        services.AddSingleton<IContentProtector, DataProtectionContentProtector>();
        services.AddSingleton<IServiceTokenFactory, RsaServiceTokenFactory>();
        services.AddSingleton<IServiceTokenValidator, RsaServiceTokenValidator>();

        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;
            return ConnectionMultiplexer.Connect(options.ConnectionString);
        });
        services.AddScoped<IOutboxDispatcher, RedisOutboxDispatcher>();
        services.AddScoped<IRoleDiscoveryService, RoleDiscoveryService>();

        services.AddHttpClient<IKnowledgePlatformGateway, KnowledgePlatformGateway>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<KnowledgePlatformOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(20);
            })
            .AddStandardResilienceHandler();

        services.AddHttpClient<IAiGateway, AiGateway>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<AiServiceOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(45);
            })
            .AddStandardResilienceHandler();

        return services;
    }
}
