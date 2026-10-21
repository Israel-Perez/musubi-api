using System.Net;
using System.Text.Json;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>02-03 / OWASP API4: los endpoints de /auth limitan intentos por cliente y responden 429 con Retry-After.</summary>
public sealed class RateLimitTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    [Fact]
    public async Task AuthEndpoint_OverPermitLimit_Returns429ProblemWithRetryAfter()
    {
        using var client = production.CreateClient();
        var uri = new Uri("/api/v1/test-auth/login", UriKind.Relative);

        for (var i = 0; i < SecurityApiFactory.AuthPermitLimit; i++)
        {
            using var allowed = await client.PostAsync(uri, content: null, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var rejected = await client.PostAsync(uri, content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        using var doc = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("rate_limit.exceeded", doc.RootElement.GetProperty("code").GetString());
    }
}
