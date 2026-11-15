using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;

namespace ContractTest;

/// <summary>
/// HU-55: forma JSON de la cuenta para recibir pagos (<c>GET /payouts/account</c>, <c>GET /payouts/banks</c>) y de los
/// datos de la transferencia (<c>GET /orders/{id}/transfer</c>) que consume musubi-app. Cambiar el fixture = cambio de
/// contrato: actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class PaymentsContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
{
    // CLABE de BBVA México con su dígito verificador (012 180 0154 8821 482 + 9).
    private const string Clabe = "012180015488214829";

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
    public async Task TheAccount_TheBanks_AndTheTransfer_MatchTheirFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (diego, diegoId) = await SignedInAsync(app, emails, "Diego Hernández");
        var (ana, anaId) = await SignedInAsync(app, emails, "Ana Sofía Ramírez");
        using (diego)
        using (ana)
        {
            var saved = await diego.PutAsJsonAsync(new Uri("/api/v1/payouts/account", UriKind.Relative), new { holderName = "Diego Hernández", clabe = Clabe }, Ct);
            var account = await BodyAsync(await diego.GetAsync(new Uri("/api/v1/payouts/account", UriKind.Relative), Ct));
            var banks = await BodyAsync(await diego.GetAsync(new Uri("/api/v1/payouts/banks", UriKind.Relative), Ct));

            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(Shape(await FixtureAsync("payout-account.json")), Shape(account));
            Assert.Equal(["code:String", "name:String"], Shape(banks.GetProperty("items")[0]));

            // Ana le compra a Diego y paga por transferencia.
            var productId = AuthIds.New();
            await app.Services.GetRequiredService<IProductRepository>().InsertAsync(
                new ProductDocument
                {
                    Id = productId,
                    CommunityId = InMemoryCommunityRepository.UtttId,
                    SellerId = diegoId,
                    Seller = new ProductSeller { Name = "Diego H." },
                    Title = "Calculadora científica Casio fx-991ES Plus",
                    CategorySlug = "calculadoras",
                    PriceCents = 50_000,
                    Condition = ProductConditions.Used,
                    ConditionDetail = "likeNew",
                    Stock = 1,
                    Status = ProductStatuses.Active,
                    Photos = [$"media/products/{diegoId}/0123456789abcdef0123456789abcdef.webp"],
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                },
                Ct);
            var (conversation, _) = await app.Services.GetRequiredService<IConversationRepository>().InsertOrGetAsync(
                new ConversationDocument
                {
                    Id = AuthIds.New(),
                    CommunityId = InMemoryCommunityRepository.UtttId,
                    BuyerId = anaId,
                    SellerId = diegoId,
                    SellerName = "Diego Hernández",
                    Participants = [anaId, diegoId],
                    Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = productId, Title = "Calculadora" },
                    LastMessageAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                },
                Ct);
            var at = DateTime.SpecifyKind((DateTime.UtcNow - TimeSpan.FromHours(6)).Date.AddDays(1).AddHours(19), DateTimeKind.Utc);
            var order = await BodyAsync(await ana.PostAsJsonAsync(
                new Uri("/api/v1/orders", UriKind.Relative),
                new { conversationId = conversation.Id, quantity = 1, meetingPoint = "biblioteca", deliveryAt = at },
                Ct));
            var orderId = order.GetProperty("id").GetString();
            await ana.PutAsJsonAsync(new Uri($"/api/v1/orders/{orderId}/payment", UriKind.Relative), new { method = "transfer" }, Ct);
            var reported = await ana.PostAsJsonAsync(new Uri($"/api/v1/orders/{orderId}/transfer", UriKind.Relative), new { reference = "4829173" }, Ct);
            var transfer = await BodyAsync(await ana.GetAsync(new Uri($"/api/v1/orders/{orderId}/transfer", UriKind.Relative), Ct));

            Assert.Equal(HttpStatusCode.OK, reported.StatusCode);
            Assert.Equal(Shape(await FixtureAsync("transfer-details.json")), Shape(transfer));
            // El pedido con la referencia tiene los mismos campos que order.json (ahi vienen en null).
            Assert.Equal(
                Shape(await FixtureAsync("order.json")).Select(p => p.Split(':')[0]),
                Shape(await BodyAsync(reported)).Select(p => p.Split(':')[0]));
        }
    }
}
