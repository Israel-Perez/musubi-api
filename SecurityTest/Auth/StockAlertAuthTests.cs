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
/// HU-51: el comparador y "Avísame" exigen sesion (RNF-05: el comparador muestra a quienes venden), una publicacion de
/// otro campus no existe (RNF-21, OWASP API1) y el aviso es privado: nadie ve ni quita el de otra persona (RNF-07).
/// </summary>
public sealed class StockAlertAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string OtherCampus = "6716a00000000000000000ff";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync()
    {
        var id = AuthIds.New();
        Assert.True(await production.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"ana.{id}@uttt.edu.mx",
                EmailNormalized = $"ana.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Ana Sofía Ramírez",
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

    private async Task<string> SoldOutAsync(string community = InMemoryCommunityRepository.UtttId)
    {
        var seller = AuthIds.New();
        var product = new ProductDocument
        {
            Id = AuthIds.New(),
            CommunityId = community,
            SellerId = seller,
            Seller = new ProductSeller { Name = "Valeria C." },
            Title = "Calculadora Casio fx-991ES Plus",
            CategorySlug = "calculadoras",
            PriceCents = 45_000,
            Condition = ProductConditions.Used,
            ConditionDetail = "good",
            Stock = 0,
            Status = ProductStatuses.SoldOut,
            Photos = [$"media/products/{seller}/{Guid.NewGuid():N}.webp"],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await production.Services.GetRequiredService<IProductRepository>().InsertAsync(product, Ct).ConfigureAwait(false);
        return product.Id;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("GET", "/api/v1/products/6716a0000000000000000001/compare")]
    [InlineData("GET", "/api/v1/products/6716a0000000000000000001/stock-alert")]
    [InlineData("PUT", "/api/v1/products/6716a0000000000000000001/stock-alert")]
    [InlineData("DELETE", "/api/v1/products/6716a0000000000000000001/stock-alert")]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnotherCampus_ProductDoesNotExist()
    {
        using var ana = ClientFor(await MemberAsync());
        var elsewhere = await SoldOutAsync(OtherCampus);

        var compare = await ana.GetAsync(new Uri($"/api/v1/products/{elsewhere}/compare", UriKind.Relative), Ct);
        var alert = await ana.PutAsync(new Uri($"/api/v1/products/{elsewhere}/stock-alert", UriKind.Relative), null, Ct);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (compare.StatusCode, alert.StatusCode));
        Assert.Equal("listings.product_not_found", (await JsonAsync(alert)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task SomeoneElse_NeitherSeesNorRemovesMine()
    {
        using var ana = ClientFor(await MemberAsync());
        using var luis = ClientFor(await MemberAsync());
        var product = await SoldOutAsync();
        await ana.PutAsync(new Uri($"/api/v1/products/{product}/stock-alert", UriKind.Relative), null, Ct);

        var removed = await luis.DeleteAsync(new Uri($"/api/v1/products/{product}/stock-alert", UriKind.Relative), Ct);
        var luisSees = await JsonAsync(await luis.GetAsync(new Uri($"/api/v1/products/{product}/stock-alert", UriKind.Relative), Ct));
        var anaSees = await JsonAsync(await ana.GetAsync(new Uri($"/api/v1/products/{product}/stock-alert", UriKind.Relative), Ct));

        // Idempotente: no revela si existe.
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.False(luisSees.GetProperty("active").GetBoolean());
        Assert.True(anaSees.GetProperty("active").GetBoolean());
    }
}
