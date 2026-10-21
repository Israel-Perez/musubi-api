using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Musubi.Api.Platform.Hosting;

namespace UnitTest.Data;

/// <summary>01-03: el check "database" de la readiness (modo en memoria y ping a MongoDB).</summary>
public sealed class DatabaseHealthCheckTests
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

    private static Task<HealthReport> CheckReadinessAsync(ServiceProvider provider) =>
        provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(
            registration => registration.Tags.Contains(PlatformServiceCollectionExtensions.ReadyTag),
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task Readiness_WithInMemoryFlag_IsHealthyWithoutMongo()
    {
        await using var provider = BuildProvider(new() { ["UseInMemoryDatabase"] = "true" });

        var report = await CheckReadinessAsync(provider);

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Contains("database", report.Entries.Keys);
    }

    [Fact]
    public async Task Readiness_WithUnreachableMongo_IsUnhealthy()
    {
        // Puerto 1 en loopback: nadie escucha. El ping falla por seleccion de servidor, no por timeout del check.
        await using var provider = BuildProvider(new()
        {
            ["UseInMemoryDatabase"] = "false",
            ["ConnectionStrings:Mongo"] = "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=300&connectTimeoutMS=300",
        });

        var report = await CheckReadinessAsync(provider);

        var entry = report.Entries["database"];
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.StartsWith("MongoDB no responde", entry.Description, StringComparison.Ordinal);
    }
}
