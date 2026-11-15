using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Listings.Data;

namespace IntegrationTest.SellerSupport;

/// <summary>
/// HU-36/37/38 de punta a punta (maqueta <c>Mis-productos</c>): Diego publico una bata hace 34 dias y no la ha vendido.
/// Fernanda publica "Se busca" una bata parecida. En "Llevan tiempo sin venderse" Diego ve su bata, que una persona la
/// busca y las ideas para venderla; en "Quienes la buscan", la solicitud de Fernanda. Nadie mas ve esa demanda.
/// </summary>
public sealed class SellerSupportFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Member(HttpClient Client, string Id);

    private async Task<Member> MemberAsync()
    {
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var email = await flow.RegisterActiveAsync().ConfigureAwait(false);
        var login = await AuthFlow.JsonAsync(await flow.LoginAsync(email).ConfigureAwait(false)).ConfigureAwait(false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        var me = await AuthFlow.JsonAsync(await client.GetAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return new Member(client, me.GetProperty("id").GetString()!);
    }

    private async Task<string> ListedAsync(string sellerId, int daysAgo, string title)
    {
        var at = factory.Clock.Now.UtcDateTime.AddDays(-daysAgo);
        var product = new ProductDocument
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            CommunityId = Musubi.Api.Modules.Auth.Data.InMemoryCommunityRepository.UtttId,
            SellerId = sellerId,
            Seller = new ProductSeller { Name = "Diego H." },
            Title = title,
            Description = "Bata corta.",
            CategorySlug = "laboratorio",
            PriceCents = 25_000,
            Condition = ProductConditions.Used,
            ConditionDetail = "good",
            Stock = 1,
            Status = ProductStatuses.Active,
            Photos = [$"media/products/{sellerId}/{Guid.NewGuid():N}.webp"],
            CreatedAt = at,
            UpdatedAt = at,
        };
        await factory.Services.GetRequiredService<IProductRepository>().InsertAsync(product, Ct).ConfigureAwait(false);
        return product.Id;
    }

    [Fact]
    public async Task AListingWithThreeWeeks_ShowsWhoLooksForIt_AndIdeas_OnlyToItsSeller()
    {
        var diego = await MemberAsync();
        var fernanda = await MemberAsync();
        var bata = await ListedAsync(diego.Id, 34, "Bata de laboratorio quirúrgica, talla M");
        await ListedAsync(diego.Id, 5, "Bata quirúrgica nueva");
        var wanted = await fernanda.Client.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Bata quirúrgica talla M", category = "laboratorio", budgetCents = 22_000, quantity = 1 },
            Ct);
        Assert.Equal(HttpStatusCode.Created, wanted.StatusCode);
        var request = (await AuthFlow.JsonAsync(wanted)).GetProperty("id").GetString();

        var slow = await AuthFlow.JsonAsync(await diego.Client.GetAsync(new Uri("/api/v1/products/mine/slow", UriKind.Relative), Ct));
        var demand = await diego.Client.GetAsync(new Uri($"/api/v1/products/{bata}/demand", UriKind.Relative), Ct);
        var hers = await fernanda.Client.GetAsync(new Uri($"/api/v1/products/{bata}/demand", UriKind.Relative), Ct);
        var hersSlow = await AuthFlow.JsonAsync(await fernanda.Client.GetAsync(new Uri("/api/v1/products/mine/slow", UriKind.Relative), Ct));

        Assert.Equal(21, slow.GetProperty("thresholdDays").GetInt32());
        var item = Assert.Single(slow.GetProperty("items").EnumerateArray());
        Assert.Equal((bata, 34), (item.GetProperty("product").GetProperty("id").GetString(), item.GetProperty("daysListed").GetInt32()));
        Assert.Equal((1, 1), (item.GetProperty("demand").GetProperty("people").GetInt32(), item.GetProperty("demand").GetProperty("requests").GetInt32()));
        // Cuesta mas de lo que ofrece Fernanda, la descripcion es corta y tiene una foto: las tres aplican.
        Assert.Equal(["price", "description", "photos"], item.GetProperty("tips").EnumerateArray().Select(t => t.GetProperty("kind").GetString()));
        Assert.Equal("Quien busca algo así ofrece hasta $220 y tú pides $250.", item.GetProperty("tips")[0].GetProperty("body").GetString());

        Assert.Equal(HttpStatusCode.OK, demand.StatusCode);
        var body = await AuthFlow.JsonAsync(demand);
        Assert.Equal((1, 0), (body.GetProperty("people").GetInt32(), body.GetProperty("alerts").GetInt32()));
        var found = Assert.Single(body.GetProperty("requests").EnumerateArray());
        Assert.Equal((request, 22_000L), (found.GetProperty("id").GetString(), found.GetProperty("budgetCents").GetInt64()));
        Assert.False(string.IsNullOrEmpty(found.GetProperty("requesterName").GetString()));

        // Solo su dueno (OWASP API1): para otra persona, como si no existiera.
        Assert.Equal(HttpStatusCode.NotFound, hers.StatusCode);
        Assert.Equal("listings.product_not_found", (await AuthFlow.JsonAsync(hers)).GetProperty("code").GetString());
        Assert.Empty(hersSlow.GetProperty("items").EnumerateArray());
    }
}
