using System.Net;
using System.Net.Http.Json;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>HU-13 / OWASP API1: las solicitudes de compra exigen sesion (las de otros campus no se ven: WantedServiceTests).</summary>
public sealed class WantedAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> MemberOnly() => new()
    {
        { "POST", "/api/v1/wanted" },
        { "GET", "/api/v1/wanted/mine" },
        { "GET", "/api/v1/wanted/6716a0000000000000000001" },
    };

    [Theory]
    [MemberData(nameof(MemberOnly))]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(new { title = "Calculadora", category = "calculadoras", budgetCents = 60_000, quantity = 1 }),
        };

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
