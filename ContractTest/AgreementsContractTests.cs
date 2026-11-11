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
/// HU-32/33/34: forma JSON de /api/v1/agreements (pedir, ver, corregir y confirmar) y del aviso del chat que consume
/// musubi-app. Cambiar el fixture = cambio de contrato: actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class AgreementsContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
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
        var email = $"ana.{Guid.NewGuid():N}@uttt.edu.mx";
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

    // La conversacion de Ana con la calculadora de Diego (2 piezas).
    private static async Task<string> ConversationAsync(IServiceProvider services, string buyerId)
    {
        var sellerId = AuthIds.New();
        var productId = AuthIds.New();
        await services.GetRequiredService<IProductRepository>().InsertAsync(
            new ProductDocument
            {
                Id = productId,
                CommunityId = InMemoryCommunityRepository.UtttId,
                SellerId = sellerId,
                Seller = new ProductSeller { Name = "Diego H." },
                Title = "Calculadora científica Casio fx-991ES Plus",
                CategorySlug = "calculadoras",
                PriceCents = 50_000,
                Condition = ProductConditions.Used,
                ConditionDetail = "likeNew",
                Stock = 2,
                Status = ProductStatuses.Active,
                Photos = [$"media/products/{sellerId}/0123456789abcdef0123456789abcdef.webp"],
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false);
        var (conversation, _) = await services.GetRequiredService<IConversationRepository>().InsertOrGetAsync(
            new ConversationDocument
            {
                Id = AuthIds.New(),
                CommunityId = InMemoryCommunityRepository.UtttId,
                BuyerId = buyerId,
                SellerId = sellerId,
                SellerName = "Diego Hernández",
                Participants = [buyerId, sellerId],
                Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = productId, Title = "Calculadora" },
                LastMessageAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false);
        return conversation.Id;
    }

    [Fact]
    public async Task SummarizeReadAndConfirm_MatchTheAgreementFixture_AndTheNoticeIsAMessage()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (ana, anaId) = await SignedInAsync(app, emails);
        using var client = ana;
        var conversationId = await ConversationAsync(app.Services, anaId);
        await client.PostAsJsonAsync(
            new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative), new { text = "¿Me la dejas en $450 mañana a la 1 en la Biblioteca?" }, Ct);

        var asked = await client.PostAsJsonAsync(new Uri("/api/v1/agreements", UriKind.Relative), new { conversationId }, Ct);
        var agreement = await BodyAsync(asked);
        var list = await BodyAsync(await client.GetAsync(new Uri($"/api/v1/agreements?conversationId={conversationId}", UriKind.Relative), Ct));
        var confirmed = await client.PostAsJsonAsync(
            new Uri($"/api/v1/agreements/{agreement.GetProperty("id").GetString()}/confirm", UriKind.Relative), new { revision = 1 }, Ct);
        var messages = await BodyAsync(await client.GetAsync(new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative), Ct));
        var fixture = await FixtureAsync("agreement.json");
        var message = await FixtureAsync("message.json");

        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        Assert.Equal(Shape(fixture), Shape(agreement));
        Assert.Equal(Shape(fixture), Shape(list.GetProperty("items")[0]));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var afterConfirm = Shape(await BodyAsync(confirmed));
        Assert.Contains("buyerConfirmedAt:String", afterConfirm);
        var notice = messages.GetProperty("items")[0];
        Assert.Equal(Shape(message), Shape(notice));
        Assert.Equal("notice", notice.GetProperty("kind").GetString());
    }
}
