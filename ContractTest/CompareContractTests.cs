using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;

namespace ContractTest;

/// <summary>
/// HU-51: forma JSON de "Compara antes de comprar" (<c>GET /products/{id}/compare</c>) y de "Avísame"
/// (<c>/products/{id}/stock-alert</c>) que consume musubi-app. Cambiar el fixture = cambio de contrato: actualizar
/// docs/endpoints.md y avisar al front.
/// </summary>
public sealed class CompareContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
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

    /// <summary>Nombres y tipos, recursivo en los objetos anidados.</summary>
    private static List<string> Shape(JsonElement element, string prefix = "") =>
        [.. element.EnumerateObject()
            .SelectMany(p => p.Value.ValueKind == JsonValueKind.Object
                ? Shape(p.Value, $"{prefix}{p.Name}.")
                : [$"{prefix}{p.Name}:{p.Value.ValueKind}"])
            .Order(StringComparer.Ordinal)];

    private static async Task<(HttpClient Client, string Id)> SignedInAsync(WebApplicationFactory<Program> app, RecordingEmailSender emails, string name)
    {
        var client = app.CreateClient();
        var email = $"{name}.{Guid.NewGuid():N}@uttt.edu.mx";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Ana Sofía Ramírez", password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        var confirmed = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await BodyAsync(confirmed).ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var me = await BodyAsync(await client.GetAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return (client, me.GetProperty("id").GetString()!);
    }

    private static ProductDocument Product(string sellerId, string title, long priceCents, int stock) => new()
    {
        Id = AuthIds.New(),
        CommunityId = InMemoryCommunityRepository.UtttId,
        SellerId = sellerId,
        Seller = new ProductSeller { Name = "Luis M." },
        Title = title,
        Description = "Incluye pilas nuevas.",
        CategorySlug = "calculadoras",
        PriceCents = priceCents,
        Condition = ProductConditions.Used,
        ConditionDetail = "good",
        Stock = stock,
        Status = stock > 0 ? ProductStatuses.Active : ProductStatuses.SoldOut,
        Photos = [$"media/products/{sellerId}/{Guid.NewGuid():N}.webp"],
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task CompareAndStockAlert_MatchTheFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (ana, _) = await SignedInAsync(app, emails, "ana");
        var luis = AuthIds.New();
        using (ana)
        {
            var products = app.Services.GetRequiredService<IProductRepository>();
            var soldOut = Product(luis, "Calculadora Casio fx-991ES Plus", 45_000, stock: 0);
            await products.InsertAsync(soldOut, Ct);
            await products.InsertAsync(Product(luis, "Casio fx-991ES Plus con tapa", 42_000, stock: 1), Ct);
            // Con calificaciones y ventas, para que la forma tenga numeros y no null.
            var owner = new ProfileOwner(luis, InMemoryCommunityRepository.UtttId, "Luis Martínez");
            var profiles = app.Services.GetRequiredService<IProfileRepository>();
            await profiles.AddRatingAsync(owner, 5, onTime: true, DateTime.UtcNow, Ct);
            await profiles.AddTradeAsync(owner, asSeller: true, DateTime.UtcNow, Ct);

            var compare = await BodyAsync(await ana.GetAsync(new Uri($"/api/v1/products/{soldOut.Id}/compare", UriKind.Relative), Ct));
            var alert = await BodyAsync(await ana.PutAsync(new Uri($"/api/v1/products/{soldOut.Id}/stock-alert", UriKind.Relative), null, Ct));
            var status = await BodyAsync(await ana.GetAsync(new Uri($"/api/v1/products/{soldOut.Id}/stock-alert", UriKind.Relative), Ct));

            var fixture = await FixtureAsync("product-comparison.json");
            Assert.Equal(Shape(fixture), Shape(compare));
            Assert.Equal(Shape(fixture.GetProperty("items")[0]), Shape(compare.GetProperty("items")[0]));
            Assert.Equal(Shape(await FixtureAsync("stock-alert.json")), Shape(alert));
            Assert.Equal(Shape(alert), Shape(status));
        }
    }
}
