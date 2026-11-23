using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Auth;

namespace IntegrationTest.Recommendations;

/// <summary>
/// HU-58 por HTTP: con los precios de un campus, "Precio sugerido" da un rango al publicar y la estimacion de una
/// publicacion trae su veredicto; con una categoria sin precios no sugiere nada; los datos invalidos dan 400 y una
/// publicacion que no existe, 404. Cada prueba usa su propio campus (el modelo es por campus).
/// </summary>
public sealed class PriceSuggestionFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static readonly string[] Kinds = ["Calculadora científica Casio fx-991", "Calculadora básica Casio", "Calculadora graficadora TI-84"];

    private static readonly long[] Prices = [30_000, 12_000, 90_000];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithCampusPrices_SuggestsARange_AndEstimatesAListingWithItsVerdict()
    {
        var campus = AuthIds.New();
        var products = await SeedAsync(campus);
        using var client = ClientFor(campus);

        var suggested = await client.PostAsJsonAsync(
            new Uri("/api/v1/products/price-suggestion", UriKind.Relative),
            new { title = "Calculadora científica Casio fx-991", category = "calculadoras", condition = "used", conditionDetail = "good" },
            Ct);
        var other = await client.PostAsJsonAsync(
            new Uri("/api/v1/products/price-suggestion", UriKind.Relative),
            new { title = "Kalimba de 17 notas", category = "musica", condition = "new" },
            Ct);
        var estimate = await client.GetAsync(new Uri($"/api/v1/products/{products[^1]}/price-estimate", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, suggested.StatusCode);
        var range = await AuthFlow.JsonAsync(suggested);
        var (min, max) = (range.GetProperty("minCents").GetInt64(), range.GetProperty("maxCents").GetInt64());
        Assert.InRange(30_000L, min, max);
        Assert.True(max < 90_000, $"{min}–{max}");
        Assert.Equal(31, range.GetProperty("samples").GetInt32());
        var none = await AuthFlow.JsonAsync(other);
        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (none.GetProperty("minCents").ValueKind, none.GetProperty("maxCents").ValueKind));
        Assert.Equal(HttpStatusCode.OK, estimate.StatusCode);
        // La ultima es una cientifica de $900: muy arriba de lo que se paga en el campus.
        Assert.Equal("high", (await AuthFlow.JsonAsync(estimate)).GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task InvalidData_Is400_AndAnUnknownListing_Is404()
    {
        using var client = ClientFor(AuthIds.New());

        var invalid = await client.PostAsJsonAsync(
            new Uri("/api/v1/products/price-suggestion", UriKind.Relative),
            new { title = "ab", category = "calculadoras", condition = "used" },
            Ct);
        var missing = await client.GetAsync(new Uri($"/api/v1/products/{AuthIds.New()}/price-estimate", UriKind.Relative), Ct);

        await AuthFlow.AssertCodeAsync(invalid, HttpStatusCode.BadRequest, "recommendations.invalid_price_request");
        await AuthFlow.AssertCodeAsync(missing, HttpStatusCode.NotFound, "listings.product_not_found");
    }

    // 30 calculadoras de tres tipos (10 de cada una) y, al final, una cientifica cara.
    private async Task<List<string>> SeedAsync(string campus)
    {
        var repository = factory.Services.GetRequiredService<IProductRepository>();
        var now = factory.Clock.Now.UtcDateTime;
        var ids = new List<string>();
        for (var i = 0; i < 31; i++)
        {
            var kind = i % Kinds.Length;
            var seller = AuthIds.New();
            var id = AuthIds.New();
            await repository.InsertAsync(
                new ProductDocument
                {
                    Id = id,
                    CommunityId = campus,
                    SellerId = seller,
                    Seller = new ProductSeller { Name = "Vendedor" },
                    Title = i == 30 ? Kinds[0] : Kinds[kind],
                    CategorySlug = "calculadoras",
                    PriceCents = i == 30 ? 90_000 : Prices[kind] + ((i % 5) - 2) * 500,
                    Condition = ProductConditions.Used,
                    ConditionDetail = "good",
                    Stock = 1,
                    Status = ProductStatuses.Active,
                    Photos = [$"media/products/{seller}/{Guid.NewGuid():N}.webp"],
                    CreatedAt = now.AddMinutes(-60 + i),
                    UpdatedAt = now,
                },
                Ct).ConfigureAwait(false);
            ids.Add(id);
        }

        return ids;
    }

    private HttpClient ClientFor(string communityId)
    {
        // El JWT se emite con el reloj de la fabrica, pero JwtBearer lo valida con la hora real.
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var token = factory.Services.GetRequiredService<AccessTokenIssuer>().Issue(AuthIds.New(), communityId, ["student"]);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }
}
