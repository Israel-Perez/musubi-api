using System.Net;
using SecurityTest.Common;

namespace SecurityTest.Hosting;

/// <summary>
/// 02-04 requisito 5 / SCRUM-123: la API manda nosniff, CORP y Referrer-Policy en cada respuesta, tambien en los
/// errores, aunque alguien llegue sin pasar por CloudFront (lo encontro OWASP ZAP: reglas 10021 y 90004).
/// </summary>
public sealed class SecurityHeadersTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/v1/ping", HttpStatusCode.OK)]
    [InlineData("/api/v1/test-auth/protected", HttpStatusCode.Unauthorized)]
    [InlineData("/health", HttpStatusCode.OK)]
    public async Task EveryResponse_CarriesTheSecurityHeaders(string path, HttpStatusCode status)
    {
        using var client = production.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(["nosniff"], response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(["same-origin"], response.Headers.GetValues("Cross-Origin-Resource-Policy"));
        Assert.Equal(["no-referrer"], response.Headers.GetValues("Referrer-Policy"));
    }
}
