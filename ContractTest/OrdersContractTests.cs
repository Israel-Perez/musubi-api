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
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;

namespace ContractTest;

/// <summary>
/// HU-23: forma JSON de /api/v1/orders (crear, pagar y los puntos de entrega) que consume musubi-app. Cambiar el
/// fixture = cambio de contrato: actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class OrdersContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // HU-27: "Puntual".
    private static readonly string[] OnTime = ["on_time"];

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

    // Una publicacion de otra persona y la conversacion de Ana con ella.
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
    public async Task CreateAndPay_MatchTheOrderFixture()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (ana, anaId) = await SignedInAsync(app, emails);
        using var client = ana;
        var conversationId = await ConversationAsync(app.Services, anaId);
        // Manana a la 1:00 p. m. del campus (UTC-6).
        var at = DateTime.SpecifyKind((DateTime.UtcNow - TimeSpan.FromHours(6)).Date.AddDays(1).AddHours(19), DateTimeKind.Utc);

        var created = await client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative),
            new { conversationId, quantity = 1, meetingPoint = "biblioteca", meetingDetail = "Planta baja, junto a la entrada", deliveryAt = at },
            Ct);
        var order = await BodyAsync(created);
        var paid = await client.PutAsJsonAsync(
            new Uri($"/api/v1/orders/{order.GetProperty("id").GetString()}/payment", UriKind.Relative), new { method = "cash" }, Ct);
        var fixture = await FixtureAsync("order.json");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(JsonValueKind.Null, order.GetProperty("payment").ValueKind);
        Assert.Equal(
            Shape(fixture).Where(p => !p.StartsWith("payment", StringComparison.Ordinal)),
            Shape(order).Where(p => !p.StartsWith("payment", StringComparison.Ordinal)));
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal(Shape(fixture), Shape(await BodyAsync(paid)));
    }

    [Fact]
    public async Task Cancel_MatchesTheCancelledOrderFixture()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (ana, anaId) = await SignedInAsync(app, emails);
        using var client = ana;
        var conversationId = await ConversationAsync(app.Services, anaId);
        var at = DateTime.SpecifyKind((DateTime.UtcNow - TimeSpan.FromHours(6)).Date.AddDays(1).AddHours(19), DateTimeKind.Utc);
        var created = await BodyAsync(await client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative),
            new { conversationId, quantity = 1, meetingPoint = "biblioteca", meetingDetail = "Planta baja, junto a la entrada", deliveryAt = at },
            Ct));
        var orderId = created.GetProperty("id").GetString();
        await client.PutAsJsonAsync(new Uri($"/api/v1/orders/{orderId}/payment", UriKind.Relative), new { method = "cash" }, Ct);

        // HU-24: quien compra cancela con un motivo de la lista; la respuesta trae quien y por que.
        var cancelled = await client.PostAsJsonAsync(new Uri($"/api/v1/orders/{orderId}/cancel", UriKind.Relative), new { reason = "changed_mind" }, Ct);
        var body = await BodyAsync(cancelled);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(Shape(await FixtureAsync("order-cancelled.json")), Shape(body));
        Assert.Equal(("cancelled", "buyer", "changed_mind"), (
            body.GetProperty("status").GetString(),
            body.GetProperty("cancellation").GetProperty("by").GetString(),
            body.GetProperty("cancellation").GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task MyOrders_MatchThePageFixture_AndEachItemIsAnOrder()
    {
        // HU-25: "Mis pedidos" trae la pagina con sus numeros y cada pedido con la forma de order.json.
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (ana, anaId) = await SignedInAsync(app, emails);
        using var client = ana;
        var conversationId = await ConversationAsync(app.Services, anaId);
        var at = DateTime.SpecifyKind((DateTime.UtcNow - TimeSpan.FromHours(6)).Date.AddDays(1).AddHours(19), DateTimeKind.Utc);
        var created = await BodyAsync(await client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative),
            new { conversationId, quantity = 1, meetingPoint = "biblioteca", meetingDetail = "Planta baja, junto a la entrada", deliveryAt = at },
            Ct));
        await client.PutAsJsonAsync(new Uri($"/api/v1/orders/{created.GetProperty("id").GetString()}/payment", UriKind.Relative), new { method = "cash" }, Ct);

        var response = await client.GetAsync(new Uri("/api/v1/orders/mine", UriKind.Relative), Ct);
        var page = await BodyAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Shape(await FixtureAsync("my-order-page.json")), Shape(page));
        Assert.Equal(Shape(await FixtureAsync("order.json")), Shape(page.GetProperty("items")[0]));
    }

    [Fact]
    public async Task Rate_TheOpinions_AndTheSales_MatchTheirFixtures()
    {
        // HU-27/HU-45: Luis califica a Ana al completarse el pedido; la calificacion y las opiniones tienen su forma.
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (ana, anaId) = await SignedInAsync(app, emails);
        var (luis, luisId) = await SignedInAsync(app, emails);
        using var seller = ana;
        using var buyer = luis;
        var at = DateTime.UtcNow.AddHours(-1);
        var product = AuthIds.New();
        var order = new OrderDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            Number = 1001,
            BuyerId = luisId,
            SellerId = anaId,
            BuyerName = "Luis Martínez",
            SellerName = "Ana Sofía Ramírez",
            ConversationId = AuthIds.New(),
            Item = new OrderItem
            {
                Source = OrderSources.Product,
                SourceId = product,
                ProductId = product,
                Title = "Calculadora",
                Photo = $"/media/products/{anaId}/{Guid.NewGuid():N}.webp",
                PriceCents = 50_000,
                Quantity = 1,
                TotalCents = 50_000,
            },
            Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", Detail = "Planta baja", At = at },
            Status = OrderStatuses.Completed,
            History = [new OrderTransition { Status = OrderStatuses.Completed, At = at }],
            CreatedAt = at,
            UpdatedAt = at,
        };
        await app.Services.GetRequiredService<IOrderRepository>().TryInsertAsync(order, Ct);

        var rated = await buyer.PostAsJsonAsync(
            new Uri($"/api/v1/orders/{order.Id}/rating", UriKind.Relative),
            new { stars = 5, tags = OnTime, comment = "Muy puntual en la biblioteca." },
            Ct);
        var page = await BodyAsync(await buyer.GetAsync(new Uri($"/api/v1/profiles/{anaId}/ratings", UriKind.Relative), Ct));
        // HU-26: "Vendidos" de Ana trae la venta con la forma de order.json.
        var sales = await BodyAsync(await seller.GetAsync(new Uri("/api/v1/orders/sales", UriKind.Relative), Ct));
        var seen = await BodyAsync(await buyer.GetAsync(new Uri($"/api/v1/orders/{order.Id}", UriKind.Relative), Ct));

        Assert.Equal(HttpStatusCode.Created, rated.StatusCode);
        Assert.Equal(Shape(await FixtureAsync("rating.json")), Shape(await BodyAsync(rated)));
        Assert.Equal(Shape(await FixtureAsync("profile-rating-page.json")), Shape(page));
        Assert.Equal(Shape(await FixtureAsync("profile-rating.json")), Shape(page.GetProperty("items")[0]));
        Assert.Equal(
            Shape(await FixtureAsync("order.json")).Where(p => !p.StartsWith("payment", StringComparison.Ordinal) && !p.StartsWith("rate", StringComparison.Ordinal)),
            Shape(seen).Where(p => !p.StartsWith("payment", StringComparison.Ordinal) && !p.StartsWith("rate", StringComparison.Ordinal)));
        Assert.Equal(JsonValueKind.String, seen.GetProperty("rateUntil").ValueKind);
        Assert.Equal(Shape(await FixtureAsync("sales-page.json")), Shape(sales));
        Assert.Equal(
            Shape(await FixtureAsync("order.json")).Where(p => !p.StartsWith("payment", StringComparison.Ordinal) && !p.StartsWith("rate", StringComparison.Ordinal)),
            Shape(sales.GetProperty("items")[0]).Where(p => !p.StartsWith("payment", StringComparison.Ordinal) && !p.StartsWith("rate", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task MeetingPoints_MatchTheFixture()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (ana, _) = await SignedInAsync(app, emails);
        using var client = ana;

        var response = await client.GetAsync(new Uri("/api/v1/orders/meeting-points", UriKind.Relative), Ct);
        var body = await BodyAsync(response);
        var fixture = await FixtureAsync("meeting-points.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Shape(fixture), Shape(body));
        Assert.Equal(Shape(fixture.GetProperty("items")[0]), Shape(body.GetProperty("items")[0]));
    }
}
