using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;

namespace ContractTest;

/// <summary>
/// HU-58: forma JSON de "Precio sugerido" (<c>POST /products/price-suggestion</c>) y de la estimacion de una publicacion
/// (<c>GET /products/{id}/price-estimate</c>) que consume musubi-app. Cambiar el fixture = cambio de contrato.
/// </summary>
public sealed class PricingContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> FixtureAsync(string name)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    private static List<string> Shape(JsonElement element) =>
        [.. element.EnumerateObject().Select(p => $"{p.Name}:{p.Value.ValueKind}").Order(StringComparer.Ordinal)];

    private static async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> app, RecordingEmailSender emails)
    {
        var client = app.CreateClient();
        var email = $"ana.{Guid.NewGuid():N}@uttt.edu.mx";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Ana Sofía Ramírez", password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        var confirmed = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await BodyAsync(confirmed).ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task SuggestionAndEstimate_MatchTheFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        using var ana = await SignedInAsync(app, emails);
        var products = app.Services.GetRequiredService<IProductRepository>();
        var ids = new List<string>();
        for (var i = 0; i < 24; i++)
        {
            var seller = AuthIds.New();
            var id = AuthIds.New();
            ids.Add(id);
            await products.InsertAsync(
                new ProductDocument
                {
                    Id = id,
                    CommunityId = InMemoryCommunityRepository.UtttId,
                    SellerId = seller,
                    Seller = new ProductSeller { Name = "Luis M." },
                    Title = i % 2 == 0 ? "Calculadora Casio fx-991ES Plus" : "Calculadora básica Casio",
                    CategorySlug = "calculadoras",
                    PriceCents = i % 2 == 0 ? 45_000 + (i * 100) : 15_000 + (i * 100),
                    Condition = ProductConditions.Used,
                    ConditionDetail = "good",
                    Stock = 1,
                    Status = ProductStatuses.Active,
                    Photos = [$"media/products/{seller}/{Guid.NewGuid():N}.webp"],
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
                Ct);
        }

        var suggestion = await BodyAsync(await ana.PostAsJsonAsync(
            new Uri("/api/v1/products/price-suggestion", UriKind.Relative),
            new { title = "Calculadora Casio fx-991ES Plus", category = "calculadoras", condition = "used", conditionDetail = "good" },
            Ct));
        var estimate = await BodyAsync(await ana.GetAsync(new Uri($"/api/v1/products/{ids[0]}/price-estimate", UriKind.Relative), Ct));

        Assert.Equal(Shape(await FixtureAsync("price-suggestion.json")), Shape(suggestion));
        Assert.Equal(Shape(await FixtureAsync("price-estimate.json")), Shape(estimate));
    }
}
