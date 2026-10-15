using System.Net;
using System.Text.Json;
using IntegrationTest.Common;

namespace IntegrationTest.Ping;

/// <summary>01-03: <c>GET /api/v1/ping</c> (fila canonica en docs/endpoints.md).</summary>
public sealed class PingTests(MusubiApiFactory factory) : IClassFixture<MusubiApiFactory>
{
    [Fact]
    public async Task PingV1_Returns200WithPongAndVersion()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("pong", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("v1", doc.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task PingV1_ReportsSupportedApiVersions()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.True(response.Headers.TryGetValues("api-supported-versions", out var versions));
        Assert.Contains("1.0", versions);
    }

    [Fact]
    public async Task PingUnsupportedVersion_IsNotServed()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v9/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(response.IsSuccessStatusCode);
    }
}
