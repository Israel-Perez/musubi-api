using System.Net;
using System.Net.Http.Json;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>HU-46: solo la persona con sesion borra su propia cuenta (la ruta no lleva id, OWASP API1).</summary>
public sealed class AccountAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeleteAccount_WithoutSession_Is401()
    {
        using var client = production.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/account/delete", UriKind.Relative), new { password = "musubi2026" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_HasNoRouteWithAnId()
    {
        using var client = production.CreateClient();

        var response = await client.PostAsJsonAsync(new Uri("/api/v1/account/6716a0000000000000000001/delete", UriKind.Relative), new { password = "musubi2026" }, Ct);

        // Una ruta inexistente sin sesion responde 401 (02-03): nunca hay un endpoint que borre por id.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
