using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Hosting;

namespace UnitTest.DI;

/// <summary>01-02: registro DI de la plataforma (CORS restringido, datos, Kestrel).</summary>
public sealed class ServiceRegistrationTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddMusubiPlatform(configuration);
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
    public void DbContext_WithInMemoryFlag_UsesInMemoryProvider()
    {
        using var provider = BuildProvider(new() { ["UseInMemoryDatabase"] = "true" });
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", context.Database.ProviderName);
    }

    [Fact]
    public void DbContext_WithPostgresConnectionString_UsesNpgsqlProvider()
    {
        using var provider = BuildProvider(new()
        {
            ["UseInMemoryDatabase"] = "false",
            ["ConnectionStrings:Default"] = "Host=localhost;Port=5432;Database=musubi;Username=musubi",
        });
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
    }

    [Fact]
    public void DbContext_WithoutConnectionString_FailsFastWithClearMessage()
    {
        using var provider = BuildProvider(new() { ["UseInMemoryDatabase"] = "false" });
        using var scope = provider.CreateScope();

        var ex = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<AppDbContext>());

        Assert.Contains("ConnectionStrings:Default", ex.Message, StringComparison.Ordinal);
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
