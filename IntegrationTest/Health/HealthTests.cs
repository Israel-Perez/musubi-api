using System.Net;
using IntegrationTest.Common;

namespace IntegrationTest.Health;

/// <summary>01-03: liveness y readiness (readiness incluye el check "database"; aqui en modo en memoria).</summary>
public sealed class HealthTests(MusubiApiFactory factory) : IClassFixture<MusubiApiFactory>
{
    [Fact]
    public async Task Health_Returns200Healthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task HealthReady_WithReachableDatabase_Returns200Healthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
