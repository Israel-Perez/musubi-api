using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Hosting;

namespace UnitTest.DI;

/// <summary>01-02: registro DI de la plataforma (CORS restringido, MongoDB, Kestrel).</summary>
public sealed class ServiceRegistrationTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddMusubiPlatform(configuration);
        services.AddAuthModule();
        return services.BuildServiceProvider();
    }

    private static CorsPolicy GetCorsPolicy(ServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        return options.GetPolicy(PlatformServiceCollectionExtensions.CorsPolicyName)
            ?? throw new InvalidOperationException("Politica CORS no registrada.");
    }

    [Fact]
    public void Cors_WithoutConfiguredOrigins_AllowsNoOrigin()
    {
        using var provider = BuildProvider([]);

        var policy = GetCorsPolicy(provider);

        Assert.False(policy.AllowAnyOrigin);
        Assert.Empty(policy.Origins);
    }

    [Fact]
    public void Cors_WithConfiguredOrigin_AllowsOnlyThatOrigin()
    {
        using var provider = BuildProvider(new() { ["Cors:AllowedOrigins:0"] = "https://admin.musubi.example" });

        var policy = GetCorsPolicy(provider);

        Assert.False(policy.AllowAnyOrigin);
        Assert.Equal(["https://admin.musubi.example"], policy.Origins);
        Assert.False(policy.AllowAnyMethod);
        Assert.False(policy.AllowAnyHeader);
    }

    [Fact]
    public void Mongo_WithConnectionString_UsesConfiguredDatabaseName()
    {
        using var provider = BuildProvider(new()
        {
            ["UseInMemoryDatabase"] = "false",
            ["ConnectionStrings:Mongo"] = "mongodb://localhost:27017",
            ["Mongo:DatabaseName"] = "musubi-test",
        });

        var database = provider.GetRequiredService<IMongoDatabase>();

        Assert.Equal("musubi-test", database.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void Mongo_WithoutDatabaseName_UsesDefaultDatabase()
    {
        using var provider = BuildProvider(new() { ["ConnectionStrings:Mongo"] = "mongodb://localhost:27017" });

        var database = provider.GetRequiredService<IMongoDatabase>();

        Assert.Equal(MongoServiceCollectionExtensions.DefaultDatabaseName, database.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void Mongo_WithoutConnectionString_FailsFastWithClearMessage()
    {
        using var provider = BuildProvider(new() { ["UseInMemoryDatabase"] = "false" });

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IMongoClient>());

        Assert.Contains("ConnectionStrings:Mongo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthRepositories_WithInMemoryFlag_UseFakesWithoutMongo()
    {
        using var provider = BuildProvider(new() { ["UseInMemoryDatabase"] = "true" });

        Assert.IsType<InMemoryUserRepository>(provider.GetRequiredService<IUserRepository>());
        Assert.IsType<InMemoryCommunityRepository>(provider.GetRequiredService<ICommunityRepository>());
        Assert.IsType<InMemoryVerificationCodeRepository>(provider.GetRequiredService<IVerificationCodeRepository>());
        Assert.IsType<InMemoryRefreshTokenRepository>(provider.GetRequiredService<IRefreshTokenRepository>());
    }

    [Fact]
    public void AuthRepositories_WithMongo_UseMongoImplementations()
    {
        using var provider = BuildProvider(new() { ["ConnectionStrings:Mongo"] = "mongodb://localhost:27017" });

        Assert.IsType<MongoUserRepository>(provider.GetRequiredService<IUserRepository>());
        Assert.IsType<MongoCommunityRepository>(provider.GetRequiredService<ICommunityRepository>());
        Assert.IsType<MongoVerificationCodeRepository>(provider.GetRequiredService<IVerificationCodeRepository>());
        Assert.IsType<MongoRefreshTokenRepository>(provider.GetRequiredService<IRefreshTokenRepository>());
    }

    [Fact]
    public void Kestrel_LimitsAreBoundFromConfiguration()
    {
        using var provider = BuildProvider(new()
        {
            ["Kestrel:Limits:MaxRequestBodySize"] = "10485760",
            ["Kestrel:Limits:KeepAliveTimeout"] = "00:02:00",
        });

        var limits = provider.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits;

        Assert.Equal(10_485_760, limits.MaxRequestBodySize);
        Assert.Equal(TimeSpan.FromMinutes(2), limits.KeepAliveTimeout);
    }
}
