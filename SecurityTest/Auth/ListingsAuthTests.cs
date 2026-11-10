using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-06 / OWASP API1, API2 y API5: publicar, subir fotos y ver "mis productos" exige sesion; la vitrina es
/// publica; un token valido de alguien que no es miembro activo no publica; en la nube la API no sirve /media.
/// </summary>
public sealed class ListingsAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static readonly string[] SomeoneElsesPhoto = ["media/products/6716a00000000000000000ff/0123456789abcdef0123456789abcdef.webp"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> MemberOnly() => new()
    {
        { "POST", "/api/v1/products" },
        { "POST", "/api/v1/products/photos" },
        { "GET", "/api/v1/products/mine" },
        { "GET", "/api/v1/products/price-range?category=libros" },
        { "GET", "/api/v1/favorites" },
        { "PUT", "/api/v1/favorites/6716a0000000000000000099" },
        { "DELETE", "/api/v1/favorites/6716a0000000000000000099" },
    };

    [Theory]
    [MemberData(nameof(MemberOnly))]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Detail_HidesPausedProductsFromOthers_AndProductsOfAnotherCampusFromMembers()
    {
        var owner = AuthIds.New();
        var paused = await InsertAsync(owner, InMemoryCommunityRepository.UtttId, ProductStatuses.Hidden);
        var removed = await InsertAsync(owner, InMemoryCommunityRepository.UtttId, ProductStatuses.Removed);
        var elsewhere = await InsertAsync(AuthIds.New(), OtherCommunity, ProductStatuses.Active);
        using var visitor = production.CreateClient();
        using var stranger = ClientFor(AuthIds.New(), InMemoryCommunityRepository.UtttId);
        using var mine = ClientFor(owner, InMemoryCommunityRepository.UtttId);

        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync(new Uri($"/api/v1/products/{paused}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(new Uri($"/api/v1/products/{paused}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mine.GetAsync(new Uri($"/api/v1/products/{paused}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mine.GetAsync(new Uri($"/api/v1/products/{removed}", UriKind.Relative), Ct)).StatusCode);
        // Con sesion, otro campus es como si no existiera; sin sesion, una publicacion visible es publica (HU-48).
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(new Uri($"/api/v1/products/{elsewhere}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(new Uri($"/api/v1/products/{elsewhere}", UriKind.Relative), Ct)).StatusCode);
    }

    private const string OtherCommunity = "000000000000000000000002";

    private async Task<string> InsertAsync(string sellerId, string communityId, string status)
    {
        var id = AuthIds.New();
        await production.Services.GetRequiredService<IProductRepository>().InsertAsync(
            new ProductDocument
            {
                Id = id,
                CommunityId = communityId,
                SellerId = sellerId,
                Seller = new ProductSeller { Name = "Ana R." },
                Title = "Calculadora Casio",
                CategorySlug = "calculadoras",
                PriceCents = 45_000,
                Condition = ProductConditions.New,
                Stock = 1,
                Status = status,
                Photos = [$"media/products/{sellerId}/{Guid.NewGuid():N}.webp"],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false);
        return id;
    }

    private HttpClient ClientFor(string userId, string communityId)
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, communityId, ["student"]);
        var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    [Fact]
    public async Task Favorites_ArePrivateToTheToken()
    {
        var owner = AuthIds.New();
        var other = AuthIds.New();
        var productId = await InsertAsync(owner, InMemoryCommunityRepository.UtttId, ProductStatuses.Active);
        await production.Services.GetRequiredService<IFavoriteRepository>().InsertIfAbsentAsync(
            new FavoriteDocument
            {
                Id = AuthIds.New(),
                UserId = owner,
                ProductId = productId,
                CommunityId = InMemoryCommunityRepository.UtttId,
                CreatedAt = DateTime.UtcNow,
            },
            Ct);
        using var mine = ClientFor(owner, InMemoryCommunityRepository.UtttId);
        using var stranger = ClientFor(other, InMemoryCommunityRepository.UtttId);

        var mineList = await mine.GetAsync(new Uri("/api/v1/favorites", UriKind.Relative), Ct);
        var strangerList = await stranger.GetAsync(new Uri("/api/v1/favorites", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, mineList.StatusCode);
        Assert.Equal(HttpStatusCode.OK, strangerList.StatusCode);
        using var mineJson = JsonDocument.Parse(await mineList.Content.ReadAsStringAsync(Ct));
        using var strangerJson = JsonDocument.Parse(await strangerList.Content.ReadAsStringAsync(Ct));
        Assert.Equal(productId, Assert.Single(mineJson.RootElement.GetProperty("items").EnumerateArray()).GetProperty("productId").GetString());
        Assert.Empty(strangerJson.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ShowcaseAndCategories_ArePublic()
    {
        using var client = production.CreateClient();

        var showcase = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);
        var categories = await client.GetAsync(new Uri("/api/v1/categories", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, showcase.StatusCode);
        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
    }

    [Fact]
    public async Task ShowcaseFilters_ArePublic_AndRespectTheCampus()
    {
        var seller = AuthIds.New();
        var local = await InsertAsync(seller, InMemoryCommunityRepository.UtttId, ProductStatuses.Active);
        var elsewhere = await InsertAsync(seller, OtherCommunity, ProductStatuses.Active);
        using var visitor = production.CreateClient();

        // HU-10: filtrar y ordenar no exige sesion y nunca cruza el campus por defecto (RNF-21).
        var response = await visitor.GetAsync(
            new Uri("/api/v1/products?category=calculadoras&minPriceCents=1&maxPriceCents=99999900&condition=new&sort=priceAsc", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var ids = page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.Contains(local, ids);
        Assert.DoesNotContain(elsewhere, ids);

        // HU-10 R1: un valor con metacaracteres se compara como texto en $in, no como sintaxis.
        var weird = await visitor.GetAsync(
            new Uri("/api/v1/products?category=calc.*%7Clibros&category=%7B%22%24ne%22%3A%22x%22%7D&sort=priceAsc", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, weird.StatusCode);
    }

    [Fact]
    public async Task ShowcaseSearch_WithMetacharactersOrManyTerms_IsStillAPage()
    {
        using var client = production.CreateClient();
        var manyTerms = "q=" + string.Join('+', Enumerable.Repeat("calc", 16));

        foreach (var query in new[] { "q=a.%2A%7Cb", manyTerms })
        {
            var response = await client.GetAsync(new Uri("/api/v1/products?" + query, UriKind.Relative), Ct);

            // HU-09: el texto se sanea (no hay inyeccion de regex) y los terminos se acotan (OWASP API3/API4).
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task ValidTokenOfSomeoneWhoIsNotAMember_CannotPublish()
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>()
            .Issue("6716a00000000000000000ff", "000000000000000000000001", ["student"]);
        using var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new
            {
                title = "Calculadora",
                category = "calculadoras",
                priceCents = 45_000,
                condition = "new",
                stock = 1,
                photos = SomeoneElsesPhoto,
            },
            Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InTheCloud_TheApiDoesNotServeMedia()
    {
        // En produccion las fotos viven en S3 y CloudFront atiende /media/*; la API nunca las expone.
        using var client = production.CreateClient();

        var response = await client.GetAsync(new Uri("/media/products/x/y.webp", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
