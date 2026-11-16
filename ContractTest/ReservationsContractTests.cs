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
/// HU-52: forma JSON de un apartado (<c>POST /reservations</c>) y de "Mis apartados" (<c>GET /reservations/mine</c>) que
/// consume musubi-app. Cambiar el fixture = cambio de
/// contrato: actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class ReservationsContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
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

    private static async Task<(HttpClient Client, string Id)> SignedInAsync(WebApplicationFactory<Program> app, RecordingEmailSender emails, string fullName)
    {
        var client = app.CreateClient();
        var email = $"u.{Guid.NewGuid():N}@uttt.edu.mx";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName, password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        var confirmed = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await BodyAsync(confirmed).ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var me = await BodyAsync(await client.GetAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return (client, me.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task AReservation_AndMyReservations_MatchTheirFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (valeria, valeriaId) = await SignedInAsync(app, emails, "Valeria Cruz");
        var (luis, _) = await SignedInAsync(app, emails, "Luis Martínez");
        using (valeria)
        using (luis)
        {
            var productId = AuthIds.New();
            await app.Services.GetRequiredService<IProductRepository>().InsertAsync(
                new ProductDocument
                {
                    Id = productId,
                    CommunityId = InMemoryCommunityRepository.UtttId,
                    SellerId = valeriaId,
                    Seller = new ProductSeller { Name = "Valeria C." },
                    Title = "Calculadora Casio fx-991ES Plus",
                    CategorySlug = "calculadoras",
                    PriceCents = 45_000,
                    Condition = ProductConditions.Used,
                    ConditionDetail = "likeNew",
                    Stock = 1,
                    Status = ProductStatuses.Active,
                    Photos = [$"media/products/{valeriaId}/0123456789abcdef0123456789abcdef.webp"],
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
                Ct);
            // Una hora dentro de las 24 h, de 7:00 a 21:00 del campus.
            var campus = DateTime.UtcNow - TimeSpan.FromHours(6);
            var slot = campus.Date.AddHours(campus.Hour < 19 ? Math.Max(campus.Hour + 1, 8) : 32);
            var pickupAt = DateTime.SpecifyKind(slot + TimeSpan.FromHours(6), DateTimeKind.Utc);

            var created = await luis.PostAsJsonAsync(new Uri("/api/v1/reservations", UriKind.Relative), new { productId, meetingPoint = "biblioteca", pickupAt }, Ct);
            var page = await BodyAsync(await luis.GetAsync(new Uri("/api/v1/reservations/mine", UriKind.Relative), Ct));

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(Shape(await FixtureAsync("reservation.json")), Shape(await BodyAsync(created)));
            Assert.Equal(Shape(await FixtureAsync("reservation-page.json")), Shape(page));
            Assert.Equal(Shape(await FixtureAsync("reservation.json")), Shape(page.GetProperty("items")[0]));
        }
    }
}
