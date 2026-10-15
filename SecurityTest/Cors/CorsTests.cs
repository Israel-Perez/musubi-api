using SecurityTest.Common;

namespace SecurityTest.Cors;

/// <summary>01-02: CORS restringido a los origenes de <c>Cors:AllowedOrigins</c>, nunca comodin.</summary>
public sealed class CorsTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static async Task<HttpResponseMessage> PreflightAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, new Uri("/api/v1/ping", UriKind.Relative));
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        return await client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    [Fact]
    public async Task Preflight_FromUnknownOrigin_IsNotAllowed()
    {
        using var client = production.CreateClient();

        using var response = await PreflightAsync(client, "https://evil.example.com");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_FromConfiguredOrigin_EchoesExactOrigin()
    {
        using var client = production.CreateClient();

        using var response = await PreflightAsync(client, SecurityApiFactory.AllowedOrigin);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal([SecurityApiFactory.AllowedOrigin], values);
    }

    [Fact]
    public async Task SimpleRequest_FromUnknownOrigin_GetsNoCorsHeader()
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/v1/ping", UriKind.Relative));
        request.Headers.Add("Origin", "https://evil.example.com");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
