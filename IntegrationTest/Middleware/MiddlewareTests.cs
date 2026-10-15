using System.Net;
using IntegrationTest.Common;

namespace IntegrationTest.Middleware;

/// <summary>01-02: comportamiento del pipeline en Development.</summary>
public sealed class MiddlewareTests(MusubiApiFactory factory) : IClassFixture<MusubiApiFactory>
{
    [Fact]
    public async Task Development_DoesNotSendHsts()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Development_ExposesOpenApiDocumentV1()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/v1/ping", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/no-existe", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
