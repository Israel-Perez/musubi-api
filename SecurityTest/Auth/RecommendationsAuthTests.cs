using System.Net;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-35: "Recomendado para ti" exige sesion (el historial es privado, RNF-07); "Ver parecidos" funciona sin sesion como
/// el detalle, y una publicacion que no existe es 404 sin revelar nada (OWASP API1).
/// </summary>
public sealed class RecommendationsAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ForYou_WithoutSession_Is401()
    {
        using var client = production.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/recommendations", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/products/6716a0000000000000000001/similar")]
    [InlineData("/api/v1/products/no-es-un-id/similar")]
    public async Task Similar_WithoutSession_IsAllowed_AndAnUnknownProductIs404(string path)
    {
        using var client = production.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
