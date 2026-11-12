using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Orders;

/// <summary>
/// HU-24 por HTTP: quien compra pide y elige pagar en efectivo, quien vende acepta (se apartan las piezas), los dos
/// confirman la entrega y el pedido queda completado con la publicacion vendida; y cancelar un pedido confirmado
/// regresa las piezas. Cada cambio le llega a la otra parte en su centro de notificaciones (HU-31).
/// </summary>
public sealed class OrderLifecycleFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    // HU-27/HU-45: los chips de cada parte (el orden de llegada no importa).
    private static readonly string[] SellerTags = ["as_described", "on_time"];
    private static readonly string[] WrongBuyerTags = ["as_described"];
    private static readonly string[] BuyerTags = ["paid_as_agreed"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Member(HttpClient Client, string Id);

    private async Task<Member> MemberAsync()
    {
        // El JWT se emite con el reloj de la fabrica, pero JwtBearer lo valida con la hora real.
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var email = await flow.RegisterActiveAsync().ConfigureAwait(false);
        var login = await AuthFlow.JsonAsync(await flow.LoginAsync(email).ConfigureAwait(false)).ConfigureAwait(false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        var me = await AuthFlow.JsonAsync(await client.GetAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return new Member(client, me.GetProperty("id").GetString()!);
    }

    private DateTime Tomorrow()
    {
        var campusToday = (factory.Clock.GetUtcNow() - TimeSpan.FromHours(6)).Date;
        return DateTime.SpecifyKind(campusToday.AddDays(1).AddHours(19), DateTimeKind.Utc);
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Teal);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<string> PublishAsync(Member seller, int stock)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString();
        var published = await seller.Client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new
            {
                title = "Calculadora Casio fx-991ES Plus",
                description = "La usé un semestre.",
                category = "calculadoras",
                priceCents = 50_000,
                condition = "used",
                conditionDetail = "good",
                stock,
                photos = new[] { key },
            },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    // Ana le escribe a Diego, crea el pedido y elige pagar en efectivo.
    private async Task<string> PaidOrderAsync(Member buyer, string productId, int quantity)
    {
        var started = await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId }, Ct).ConfigureAwait(false);
        var conversationId = (await AuthFlow.JsonAsync(started).ConfigureAwait(false)).GetProperty("id").GetString();
        var created = await buyer.Client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative),
            new { conversationId, quantity, meetingPoint = "biblioteca", deliveryAt = Tomorrow() },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await AuthFlow.JsonAsync(created).ConfigureAwait(false)).GetProperty("id").GetString()!;
        var paid = await buyer.Client.PutAsJsonAsync(new Uri($"/api/v1/orders/{id}/payment", UriKind.Relative), new { method = "cash" }, Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        return id;
    }

    private static Task<HttpResponseMessage> PostAsync(Member member, string path, object? body = null) =>
        body is null
            ? member.Client.PostAsync(new Uri(path, UriKind.Relative), null, Ct)
            : member.Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct);

    private static async Task<JsonElement> ProductAsync(Member member, string id)
    {
        var response = await member.Client.GetAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    private static async Task<List<string>> NoticesAsync(Member member)
    {
        var response = await member.Client.GetAsync(new Uri("/api/v1/notifications?category=orders", UriKind.Relative), Ct).ConfigureAwait(false);
        var body = await AuthFlow.JsonAsync(response).ConfigureAwait(false);
        return [.. body.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()!)];
    }

    private static async Task<JsonElement> MineAsync(Member member, string query = "")
    {
        var response = await member.Client.GetAsync(new Uri("/api/v1/orders/mine" + query, UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    private static List<string> Ids(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("id").GetString()!)];

    [Fact]
    public async Task BothRateACompletedOrder_OnceEach_AndTheProfileShowsTheReputation()
    {
        // HU-27/HU-45: al completarse, quien compra califica a quien vende y al reves; el perfil muestra el promedio,
        // el numero, "A tiempo" y las opiniones.
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var product = await PublishAsync(diego, stock: 1);
        var order = await PaidOrderAsync(ana, product, quantity: 1);
        var early = await PostAsync(ana, $"/api/v1/orders/{order}/rating", new { stars = 5 });
        await PostAsync(diego, $"/api/v1/orders/{order}/accept");
        await PostAsync(diego, $"/api/v1/orders/{order}/handover");
        var completed = await AuthFlow.JsonAsync(await PostAsync(ana, $"/api/v1/orders/{order}/handover"));

        var byAna = await PostAsync(ana, $"/api/v1/orders/{order}/rating", new { stars = 5, tags = SellerTags, comment = "Muy puntual en la biblioteca." });
        var again = await PostAsync(ana, $"/api/v1/orders/{order}/rating", new { stars = 1 });
        var wrongTag = await PostAsync(diego, $"/api/v1/orders/{order}/rating", new { stars = 4, tags = WrongBuyerTags });
        var byDiego = await PostAsync(diego, $"/api/v1/orders/{order}/rating", new { stars = 4, tags = BuyerTags });
        var profile = await AuthFlow.JsonAsync(await ana.Client.GetAsync(new Uri($"/api/v1/profiles/{diego.Id}", UriKind.Relative), Ct));
        var opinions = await AuthFlow.JsonAsync(await ana.Client.GetAsync(new Uri($"/api/v1/profiles/{diego.Id}/ratings", UriKind.Relative), Ct));
        var seen = await AuthFlow.JsonAsync(await ana.Client.GetAsync(new Uri($"/api/v1/orders/{order}", UriKind.Relative), Ct));

        await AuthFlow.AssertCodeAsync(early, HttpStatusCode.Conflict, "ratings.not_completed");
        Assert.False(completed.GetProperty("ratedByMe").GetBoolean());
        Assert.Equal(JsonValueKind.String, completed.GetProperty("rateUntil").ValueKind);
        Assert.Equal(HttpStatusCode.Created, byAna.StatusCode);
        Assert.Equal(["on_time", "as_described"], (await AuthFlow.JsonAsync(byAna)).GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        await AuthFlow.AssertCodeAsync(again, HttpStatusCode.Conflict, "ratings.already_rated");
        await AuthFlow.AssertCodeAsync(wrongTag, HttpStatusCode.BadRequest, "ratings.invalid_tags");
        Assert.Equal(HttpStatusCode.Created, byDiego.StatusCode);
        var stats = profile.GetProperty("stats");
        Assert.Equal((5.0, 1, 100), (stats.GetProperty("ratingAvg").GetDouble(), stats.GetProperty("ratingCount").GetInt32(), stats.GetProperty("onTime").GetInt32()));
        var opinion = Assert.Single(opinions.GetProperty("items").EnumerateArray());
        Assert.Equal(("Muy puntual en la biblioteca.", "seller"), (opinion.GetProperty("comment").GetString(), opinion.GetProperty("role").GetString()));
        Assert.True(seen.GetProperty("ratedByMe").GetBoolean());
    }

    [Fact]
    public async Task MyOrders_ShowMyPurchasesByTab_WithTheirCounts_ButNotMySales()
    {
        // HU-25: "Mis pedidos" de Perfil y "Ver todos" con las pestanas Activos, Completados y Cancelados.
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var kept = await PaidOrderAsync(ana, await PublishAsync(diego, stock: 1), quantity: 1);
        var dropped = await PaidOrderAsync(ana, await PublishAsync(diego, stock: 1), quantity: 1);
        await PostAsync(ana, $"/api/v1/orders/{dropped}/cancel", new { reason = "changed_mind" });

        var all = await MineAsync(ana);
        var active = await MineAsync(ana, "?tab=active");
        var cancelled = await MineAsync(ana, "?tab=cancelled");
        var latest = await MineAsync(ana, "?limit=1");
        var sales = await MineAsync(diego);
        var wrongTab = await ana.Client.GetAsync(new Uri("/api/v1/orders/mine?tab=sold", UriKind.Relative), Ct);

        Assert.Equal([dropped, kept], Ids(all));
        Assert.Equal((2L, 1L, 0L, 1L), (
            all.GetProperty("total").GetInt64(),
            all.GetProperty("counts").GetProperty("active").GetInt64(),
            all.GetProperty("counts").GetProperty("completed").GetInt64(),
            all.GetProperty("counts").GetProperty("cancelled").GetInt64()));
        Assert.Equal([kept], Ids(active));
        Assert.Equal([dropped], Ids(cancelled));
        Assert.Equal("changed_mind", cancelled.GetProperty("items")[0].GetProperty("cancellation").GetProperty("reason").GetString());
        Assert.Equal(dropped, latest.GetProperty("nextCursor").GetString());
        Assert.Equal("buyer", all.GetProperty("items")[0].GetProperty("role").GetString());
        Assert.Equal(0, sales.GetProperty("total").GetInt64());
        await AuthFlow.AssertCodeAsync(wrongTab, HttpStatusCode.BadRequest, "orders.invalid_tab");
    }

    [Fact]
    public async Task AcceptThenBothConfirm_CompletesTheOrder_AndTheListingIsSold()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var product = await PublishAsync(diego, stock: 1);
        var order = await PaidOrderAsync(ana, product, quantity: 1);

        var byBuyer = await PostAsync(ana, $"/api/v1/orders/{order}/accept");
        var accepted = await PostAsync(diego, $"/api/v1/orders/{order}/accept");
        var reserved = await ProductAsync(diego, product);
        var delivered = await AuthFlow.JsonAsync(await PostAsync(diego, $"/api/v1/orders/{order}/handover"));
        var received = await PostAsync(ana, $"/api/v1/orders/{order}/handover");
        var completed = await AuthFlow.JsonAsync(received);
        var sold = await ProductAsync(diego, product);

        await AuthFlow.AssertCodeAsync(byBuyer, HttpStatusCode.Forbidden, "orders.only_seller");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("confirmed", (await AuthFlow.JsonAsync(accepted)).GetProperty("status").GetString());
        Assert.Equal((0, "soldOut"), (reserved.GetProperty("stock").GetInt32(), reserved.GetProperty("status").GetString()));
        Assert.Equal(JsonValueKind.String, delivered.GetProperty("handover").GetProperty("sellerConfirmedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, delivered.GetProperty("handover").GetProperty("buyerConfirmedAt").ValueKind);
        Assert.Equal(HttpStatusCode.OK, received.StatusCode);
        Assert.Equal("completed", completed.GetProperty("status").GetString());
        Assert.Equal("sold", sold.GetProperty("status").GetString());
        Assert.Equal(["order_handover", "order_confirmed"], await NoticesAsync(ana));
        Assert.Equal(["order_completed", "order_created"], await NoticesAsync(diego));
    }

    [Fact]
    public async Task CancelAConfirmedOrder_ReturnsThePieces_WithItsReason()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var product = await PublishAsync(diego, stock: 2);
        var order = await PaidOrderAsync(ana, product, quantity: 2);
        await PostAsync(diego, $"/api/v1/orders/{order}/accept");

        var wrongReason = await PostAsync(ana, $"/api/v1/orders/{order}/cancel", new { reason = "unavailable" });
        var cancelled = await PostAsync(ana, $"/api/v1/orders/{order}/cancel", new { reason = "schedule" });
        var body = await AuthFlow.JsonAsync(cancelled);
        var again = await PostAsync(diego, $"/api/v1/orders/{order}/cancel", new { reason = "other" });
        var handover = await PostAsync(diego, $"/api/v1/orders/{order}/handover");
        var listing = await ProductAsync(diego, product);

        await AuthFlow.AssertCodeAsync(wrongReason, HttpStatusCode.BadRequest, "orders.invalid_reason");
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(("cancelled", "buyer", "schedule"), (
            body.GetProperty("status").GetString(),
            body.GetProperty("cancellation").GetProperty("by").GetString(),
            body.GetProperty("cancellation").GetProperty("reason").GetString()));
        await AuthFlow.AssertCodeAsync(again, HttpStatusCode.Conflict, "orders.closed");
        await AuthFlow.AssertCodeAsync(handover, HttpStatusCode.Conflict, "orders.closed");
        Assert.Equal((2, "active"), (listing.GetProperty("stock").GetInt32(), listing.GetProperty("status").GetString()));
        Assert.Equal("order_cancelled", (await NoticesAsync(diego))[0]);
    }

    [Fact]
    public async Task TheSeller_RejectsAPendingOrder_AndCannotAcceptBeforeThePayment()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var luis = await MemberAsync();
        var product = await PublishAsync(diego, stock: 1);
        var started = await ana.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId = product }, Ct);
        var conversationId = (await AuthFlow.JsonAsync(started)).GetProperty("id").GetString();
        var created = await ana.Client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative),
            new { conversationId, quantity = 1, meetingPoint = "biblioteca", deliveryAt = Tomorrow() },
            Ct);
        var order = (await AuthFlow.JsonAsync(created)).GetProperty("id").GetString();

        var early = await PostAsync(diego, $"/api/v1/orders/{order}/accept");
        var notConfirmed = await PostAsync(ana, $"/api/v1/orders/{order}/handover");
        var stranger = await PostAsync(luis, $"/api/v1/orders/{order}/cancel", new { reason = "other" });
        var rejected = await PostAsync(diego, $"/api/v1/orders/{order}/cancel", new { reason = "unavailable" });

        await AuthFlow.AssertCodeAsync(early, HttpStatusCode.Conflict, "orders.payment_missing");
        await AuthFlow.AssertCodeAsync(notConfirmed, HttpStatusCode.Conflict, "orders.not_confirmed");
        await AuthFlow.AssertCodeAsync(stranger, HttpStatusCode.NotFound, "orders.not_found");
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("seller", (await AuthFlow.JsonAsync(rejected)).GetProperty("cancellation").GetProperty("by").GetString());
        Assert.Equal(1, (await ProductAsync(diego, product)).GetProperty("stock").GetInt32());
    }
}
