using System.Net;
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
/// HU-36/37/38: forma JSON de "Llevan tiempo sin venderse" (<c>GET /products/mine/slow</c>) y "Quienes la buscan"
/// (<c>GET /products/{id}/demand</c>) y las ideas con IA (<c>POST /products/{id}/ideas</c>) que consume musubi-app. Cambiar el fixture = cambio de contrato: actualizar
/// docs/endpoints.md y avisar al front.
/// </summary>
public sealed class SellerSupportContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
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

    private static async Task<(HttpClient Client, string Id)> SignedInAsync(WebApplicationFactory<Program> app, RecordingEmailSender emails)
    {
        var client = app.CreateClient();
        var email = $"diego.{Guid.NewGuid():N}@uttt.edu.mx";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Diego Hernández", password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        var confirmed = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await BodyAsync(confirmed).ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var me = await BodyAsync(await client.GetAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return (client, me.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task SlowListingsAndDemand_MatchTheFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (diego, diegoId) = await SignedInAsync(app, emails);
        var (fernanda, _) = await SignedInAsync(app, emails);
        using (diego)
        using (fernanda)
        {
            var at = DateTime.UtcNow.AddDays(-34);
            var bata = new ProductDocument
            {
                Id = AuthIds.New(),
                CommunityId = InMemoryCommunityRepository.UtttId,
                SellerId = diegoId,
                Seller = new ProductSeller { Name = "Diego H." },
                Title = "Bata de laboratorio blanca, talla M",
                Description = "Bata corta.",
                CategorySlug = "laboratorio",
                PriceCents = 25_000,
                Condition = ProductConditions.Used,
                ConditionDetail = "good",
                Stock = 1,
                Status = ProductStatuses.Active,
                Photos = [$"media/products/{diegoId}/{Guid.NewGuid():N}.webp"],
                CreatedAt = at,
                UpdatedAt = at,
            };
            await app.Services.GetRequiredService<IProductRepository>().InsertAsync(bata, Ct);
            var wanted = await fernanda.PostAsJsonAsync(
                new Uri("/api/v1/wanted", UriKind.Relative),
                new { title = "Bata de laboratorio talla M", category = "laboratorio", budgetCents = 22_000, quantity = 1 },
                Ct);
            Assert.Equal(HttpStatusCode.Created, wanted.StatusCode);
            var slowFixture = await FixtureAsync("slow-listings.json");
            var demandFixture = await FixtureAsync("product-demand.json");

            var slow = await BodyAsync(await diego.GetAsync(new Uri("/api/v1/products/mine/slow", UriKind.Relative), Ct));
            var demand = await BodyAsync(await diego.GetAsync(new Uri($"/api/v1/products/{bata.Id}/demand", UriKind.Relative), Ct));

            Assert.Equal(Shape(slowFixture), Shape(slow));
            Assert.Equal(Shape(slowFixture.GetProperty("items")[0]), Shape(slow.GetProperty("items")[0]));
            Assert.Equal(Shape(slowFixture.GetProperty("items")[0].GetProperty("tips")[0]), Shape(slow.GetProperty("items")[0].GetProperty("tips")[0]));
            var ideas = await BodyAsync(await diego.PostAsync(new Uri($"/api/v1/products/{bata.Id}/ideas", UriKind.Relative), null, Ct));

            Assert.Equal(Shape(demandFixture), Shape(demand));
            Assert.Equal(Shape(demandFixture.GetProperty("requests")[0]), Shape(demand.GetProperty("requests")[0]));
            Assert.Equal(Shape(await FixtureAsync("listing-ideas.json")), Shape(ideas));
        }
    }
}
