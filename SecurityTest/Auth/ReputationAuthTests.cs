using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-28 y HU-57 / OWASP API1 y API5: reportar y bloquear exigen sesion, el token en la query no sirve fuera del
/// hub, y nadie administra los bloqueos de otro (las rutas no llevan el id de quien bloquea).
/// </summary>
public sealed class ReputationAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string AnyId = "6716a0000000000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> MemberOnly() => new()
    {
        { "POST", "/api/v1/reports" },
        { "GET", "/api/v1/blocks" },
        { "POST", "/api/v1/blocks" },
        { "DELETE", $"/api/v1/blocks/{AnyId}" },
    };

    [Theory]
    [MemberData(nameof(MemberOnly))]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(new { userId = AnyId, targetType = "general", reason = "other" }),
        };

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenInTheQuery_DoesNotOpenReportsOrBlocks()
    {
        using var client = production.CreateClient();
        var token = Uri.EscapeDataString(production.Services.GetRequiredService<AccessTokenIssuer>()
            .Issue(AuthIds.New(), InMemoryCommunityRepository.UtttId, ["student"]).Token);

        var blocks = await client.GetAsync(new Uri($"/api/v1/blocks?access_token={token}", UriKind.Relative), Ct);
        var report = await client.PostAsJsonAsync(new Uri($"/api/v1/reports?access_token={token}", UriKind.Relative), new { targetType = "general", reason = "other" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, blocks.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, report.StatusCode);
    }
}
