using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Caching;

/// <summary>
/// 02-02 / OWASP API3: la cache de salida solo guarda respuestas anonimas. Una peticion con sesion nunca se
/// sirve de la cache compartida (su respuesta incluye datos del vendedor) ni se guarda en ella.
/// </summary>
public sealed class OutputCacheTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage> ShowcaseAsync(string? token, string query)
    {
        var client = production.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client.GetAsync(new Uri("/api/v1/products" + query, UriKind.Relative), Ct);
    }

    [Fact]
    public async Task Anonymous_IsServedFromTheCache_ButASession_NeverIs()
    {
        // Consulta propia de esta prueba: la cache de salida se comparte dentro de la fabrica.
        const string query = "?category=libros&limit=7";
        var token = production.Services.GetRequiredService<AccessTokenIssuer>()
            .Issue("6716a00000000000000000aa", "000000000000000000000001", ["student"]).Token;

        await ShowcaseAsync(null, query);
        var cached = await ShowcaseAsync(null, query);
        var withSession = await ShowcaseAsync(token, query);
        var withSessionAgain = await ShowcaseAsync(token, query);

        Assert.Equal(HttpStatusCode.OK, cached.StatusCode);
        Assert.True(cached.Headers.Age.HasValue, "la segunda consulta anonima deberia salir de la cache");
        Assert.False(withSession.Headers.Age.HasValue);
        Assert.False(withSessionAgain.Headers.Age.HasValue);
        Assert.DoesNotContain("public", withSession.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }
}
