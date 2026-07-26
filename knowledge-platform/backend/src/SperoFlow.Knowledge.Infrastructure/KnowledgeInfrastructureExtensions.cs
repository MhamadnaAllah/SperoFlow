using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace SperoFlow.Knowledge.Infrastructure;

public static class KnowledgeInfrastructureExtensions
{
    public static IServiceCollection AddKnowledgeInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KnowledgeDatabaseOptions>()
            .Bind(configuration.GetSection(KnowledgeDatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<KnowledgeStorageOptions>()
            .Bind(configuration.GetSection(KnowledgeStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<KnowledgeRedisOptions>()
            .Bind(configuration.GetSection(KnowledgeRedisOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<KnowledgeInternalAuthOptions>()
            .Bind(configuration.GetSection(KnowledgeInternalAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<KnowledgeGrantOptions>()
            .Bind(configuration.GetSection(KnowledgeGrantOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<KnowledgeOidcOptions>()
            .Bind(configuration.GetSection(KnowledgeOidcOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var database = configuration.GetRequiredSection(KnowledgeDatabaseOptions.SectionName).Get<KnowledgeDatabaseOptions>()!;
        services.AddDbContext<KnowledgeDbContext>(options => options.UseNpgsql(database.ConnectionString));
        services.AddSingleton<IAmazonS3>(provider =>
        {
            var storage = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KnowledgeStorageOptions>>().Value;
            return S3KnowledgeObjectStorage.CreateClient(storage);
        });
        services.AddSingleton<IKnowledgeObjectStorage, S3KnowledgeObjectStorage>();
        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var redis = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<KnowledgeRedisOptions>>().Value;
            return ConnectionMultiplexer.Connect(redis.ConnectionString);
        });
        services.AddSingleton<KnowledgeInternalTokenService>();
        services.AddSingleton<KnowledgeAccessGrantService>();
        services.AddScoped<KnowledgeOutboxDispatcher>();
        return services;
    }
}