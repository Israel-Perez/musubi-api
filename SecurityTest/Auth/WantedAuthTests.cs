using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-13 / OWASP API1: publicar y "Mis solicitudes" exigen sesion. HU-15 / RNF-05: el tablero es publico, pero un
/// visitante no recibe el id, la foto ni la carrera de quien busca; con sesion manda el campus del token (RNF-21).
/// </summary>
public sealed class WantedAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> MemberOnly() => new()
    {
        { "POST", "/api/v1/wanted" },
        { "GET", "/api/v1/wanted/mine" },
        { "GET", "/api/v1/wanted/6716a0000000000000000001" },
        { "GET", "/api/v1/wanted/6716a0000000000000000001/offers/mine" },
        { "PUT", "/api/v1/wanted/6716a0000000000000000001/offers/mine" },
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

    private const string OtherCommunity = "000000000000000000000002";

    private async Task<string> InsertAsync(string communityId, string title)
    {
        var id = AuthIds.New();
        await production.Services.GetRequiredService<IWantedRepository>().InsertAsync(
            new WantedRequestDocument
            {
                Id = id,
                CommunityId = communityId,
                RequesterId = AuthIds.New(),
                RequesterName = "Fernanda O.",
                Title = title,
                CategorySlug = "laboratorio",
                CategoryName = "Laboratorio",
                BudgetCents = 30_000,
                ExpiresAt = DateTime.UtcNow.AddDays(10),
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false);
        return id;
    }

    private static async Task<List<JsonElement>> ItemsAsync(HttpResponseMessage response)
    {
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct).ConfigureAwait(false);
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    [Fact]
    public async Task Board_IsPublic_WithoutThePersonalDataOfWhoAsks()
    {
        var local = await InsertAsync(InMemoryCommunityRepository.UtttId, "Bata de laboratorio");
        using var visitor = production.CreateClient();

        var response = await visitor.GetAsync(new Uri("/api/v1/wanted?limit=50", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = (await ItemsAsync(response)).Single(r => r.GetProperty("id").GetString() == local);
        var requester = item.GetProperty("requester");
        Assert.Equal(JsonValueKind.Null, requester.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Null, requester.GetProperty("photo").ValueKind);
        Assert.Equal(JsonValueKind.Null, requester.GetProperty("career").ValueKind);
    }

    [Fact]
    public async Task Board_WithSession_TheTokenCampusWins_OverTheQuery()
    {
        var local = await InsertAsync(InMemoryCommunityRepository.UtttId, "Kit de disección");
        var elsewhere = await InsertAsync(OtherCommunity, "Kit de disección");
        var token = production.Services.GetRequiredService<AccessTokenIssuer>().Issue(AuthIds.New(), OtherCommunity, ["student"]);
        using var member = production.CreateClient();
        member.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var ids = (await ItemsAsync(await member.GetAsync(new Uri("/api/v1/wanted?campus=uttt&limit=50", UriKind.Relative), Ct)))
            .Select(r => r.GetProperty("id").GetString())
            .ToList();

        Assert.Contains(elsewhere, ids);
        Assert.DoesNotContain(local, ids);
    }

    [Fact]
    public async Task Board_MetacharactersAndOperators_AreJustText()
    {
        using var visitor = production.CreateClient();

        foreach (var query in new[] { "q=a.%2A%7Cb", "q=" + string.Join('+', Enumerable.Repeat("bata", 16)), "category=%7B%22%24ne%22%3A%22x%22%7D" })
        {
            var response = await visitor.GetAsync(new Uri("/api/v1/wanted?" + query, UriKind.Relative), Ct);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
