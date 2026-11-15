using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-36/37/38: "Llevan tiempo sin venderse" y "Quienes la buscan" exigen sesion y son solo de lo propio (RNF-03, OWASP
/// API1): la demanda de una publicacion ajena responde como si no existiera, y lo ajeno nunca sale en mi lista.
/// </summary>
public sealed class SellerSupportAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync()
    {
        var id = AuthIds.New();
        Assert.True(await production.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"diego.{id}@uttt.edu.mx",
                EmailNormalized = $"diego.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Diego Hernández",
                Status = UserStatuses.Active,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private HttpClient ClientFor(string userId)
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, InMemoryCommunityRepository.UtttId, ["student"]);
        var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    private async Task<string> OldListingAsync(string sellerId)
    {
        var at = DateTime.UtcNow.AddDays(-30);
        var product = new ProductDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            SellerId = sellerId,
            Seller = new ProductSeller { Name = "Diego H." },
            Title = "Bata de laboratorio blanca, talla M",
            Description = "Bata de manga larga.",
            CategorySlug = "laboratorio",
            PriceCents = 25_000,
            Condition = ProductConditions.Used,
            Stock = 1,
            Status = ProductStatuses.Active,
            Photos = [$"media/products/{sellerId}/{Guid.NewGuid():N}.webp"],
            CreatedAt = at,
            UpdatedAt = at,
        };
        await production.Services.GetRequiredService<IProductRepository>().InsertAsync(product, Ct).ConfigureAwait(false);
        return product.Id;
    }

    [Theory]
    [InlineData("/api/v1/products/mine/slow")]
    [InlineData("/api/v1/products/6716a0000000000000000001/demand")]
    public async Task WithoutSession_Is401(string path)
    {
        using var client = production.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SomeoneElse_NeitherSeesTheDemandNorTheListing()
    {
        var diego = await MemberAsync();
        var bata = await OldListingAsync(diego);
        using var owner = ClientFor(diego);
        using var luis = ClientFor(await MemberAsync());

        var theirs = await luis.GetAsync(new Uri($"/api/v1/products/{bata}/demand", UriKind.Relative), Ct);
        var luisSlow = await JsonAsync(await luis.GetAsync(new Uri("/api/v1/products/mine/slow", UriKind.Relative), Ct));
        var mine = await owner.GetAsync(new Uri($"/api/v1/products/{bata}/demand", UriKind.Relative), Ct);
        var mySlow = await JsonAsync(await owner.GetAsync(new Uri("/api/v1/products/mine/slow", UriKind.Relative), Ct));

        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal("listings.product_not_found", (await JsonAsync(theirs)).GetProperty("code").GetString());
        Assert.Equal(0, luisSlow.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal(1, mySlow.GetProperty("items").GetArrayLength());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false)).RootElement;
}
