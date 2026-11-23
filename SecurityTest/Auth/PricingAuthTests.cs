using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Recommendations;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-58: el precio sugerido y la estimacion exigen sesion (los precios del campus no son publicos, RNF-03); una
/// publicacion de otro campus o pausada es 404 sin revelar nada (OWASP API1); un id invalido es 404 y el cuerpo tiene
/// su propio tope (API4).
/// </summary>
public sealed class PricingAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string OtherCommunity = "000000000000000000000002";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("POST", "/api/v1/products/price-suggestion")]
    [InlineData("GET", "/api/v1/products/6716a0000000000000000001/price-estimate")]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = method == "POST" ? JsonContent.Create(new { title = "Calculadora", category = "calculadoras", condition = "new" }) : null,
        };

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AListingOfAnotherCampus_OrPaused_IsNotFound_AndAnInvalidIdToo()
    {
        var elsewhere = await InsertAsync(OtherCommunity, ProductStatuses.Active);
        var paused = await InsertAsync(InMemoryCommunityRepository.UtttId, ProductStatuses.Hidden);
        using var client = ClientFor(AuthIds.New(), InMemoryCommunityRepository.UtttId);

        foreach (var path in new[] { $"/api/v1/products/{elsewhere}/price-estimate", $"/api/v1/products/{paused}/price-estimate", "/api/v1/products/no-es-un-id/price-estimate" })
        {
            var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains("listings.product_not_found", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ABadBody_Is400_WithoutInternalDetail()
    {
        using var client = ClientFor(AuthIds.New(), InMemoryCommunityRepository.UtttId);

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/products/price-suggestion", UriKind.Relative),
            new { title = new string('x', 500), category = "calculadoras", condition = "used" },
            Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("recommendations.invalid_price_request", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBody_HasItsOwnSizeLimit()
    {
        // Kestrel aplica el tope en la nube; TestServer no, asi que se verifica el atributo (OWASP API4).
        var limit = typeof(RecommendationsController).GetMethod(nameof(RecommendationsController.PriceSuggestion))!
            .GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: false);

        Assert.Single(limit);
    }

    private async Task<string> InsertAsync(string communityId, string status)
    {
        var id = AuthIds.New();
        var seller = AuthIds.New();
        await production.Services.GetRequiredService<IProductRepository>().InsertAsync(
            new ProductDocument
            {
                Id = id,
                CommunityId = communityId,
                SellerId = seller,
                Seller = new ProductSeller { Name = "Ana R." },
                Title = "Calculadora Casio",
                CategorySlug = "calculadoras",
                PriceCents = 45_000,
                Condition = ProductConditions.New,
                Stock = 1,
                Status = status,
                Photos = [$"media/products/{seller}/{Guid.NewGuid():N}.webp"],
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
}
