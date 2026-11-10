using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Orders;

/// <summary>
/// HU-23: desde el chat de una publicacion o de una oferta aceptada, quien compra crea el pedido (Pendiente, RN-16),
/// elige pagar en efectivo (RN-18), las dos partes lo ven, uno abierto por conversacion, la publicacion con pedido no
/// se elimina (RN-07) y al borrar la cuenta el pedido se cancela (HU-46).
/// </summary>
public sealed class OrderFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

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

    // Manana a la 1:00 p. m. del campus (UTC-6), con el reloj de la fabrica.
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

    private static async Task<string> PublishAsync(Member seller, int stock = 2)
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

    private static async Task<string> ChatAsync(Member buyer, string productId)
    {
        var started = await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId }, Ct).ConfigureAwait(false);
        return (await AuthFlow.JsonAsync(started).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> CreateAsync(Member buyer, string conversationId, int quantity = 1, string point = "biblioteca", DateTime? at = null) =>
        buyer.Client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative),
            new { conversationId, quantity, meetingPoint = point, meetingDetail = "Junto a la entrada principal", deliveryAt = at ?? Tomorrow() },
            Ct);

    private static async Task<JsonElement> GetJsonAsync(Member member, string path)
    {
        var response = await member.Client.GetAsync(new Uri(path, UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    [Fact]
    public async Task FromAListingChat_TheBuyerCreatesAPendingOrder_PaysCash_AndBothSeeIt()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var product = await PublishAsync(diego);
        var conversation = await ChatAsync(ana, product);

        var created = await CreateAsync(ana, conversation, quantity: 2);
        var order = await AuthFlow.JsonAsync(created);
        var id = order.GetProperty("id").GetString();
        var again = await CreateAsync(ana, conversation);
        var paid = await ana.Client.PutAsJsonAsync(new Uri($"/api/v1/orders/{id}/payment", UriKind.Relative), new { method = "cash" }, Ct);
        var asSeller = await GetJsonAsync(diego, $"/api/v1/orders/{id}");
        var inChat = await GetJsonAsync(ana, $"/api/v1/orders?conversationId={conversation}");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Matches("^MS-\\d{4,}$", order.GetProperty("number").GetString());
        Assert.Equal("pending", order.GetProperty("status").GetString());
        Assert.Equal("buyer", order.GetProperty("role").GetString());
        Assert.Equal(100_000, order.GetProperty("item").GetProperty("totalCents").GetInt64());
        Assert.Equal("Biblioteca", order.GetProperty("delivery").GetProperty("pointName").GetString());
        await AuthFlow.AssertCodeAsync(again, HttpStatusCode.Conflict, "orders.already_open");
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        Assert.Equal("seller", asSeller.GetProperty("role").GetString());
        Assert.Equal("cash", asSeller.GetProperty("payment").GetProperty("method").GetString());
        Assert.Equal(id, inChat.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task TheSeller_CannotCreate_AndAStrangerCannotSeeTheOrder()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var luis = await MemberAsync();
        var conversation = await ChatAsync(ana, await PublishAsync(diego));
        var id = (await AuthFlow.JsonAsync(await CreateAsync(ana, conversation))).GetProperty("id").GetString();

        await AuthFlow.AssertCodeAsync(await CreateAsync(diego, conversation), HttpStatusCode.Forbidden, "orders.only_buyer");
        await AuthFlow.AssertCodeAsync(await luis.Client.GetAsync(new Uri($"/api/v1/orders/{id}", UriKind.Relative), Ct), HttpStatusCode.NotFound, "orders.not_found");
        await AuthFlow.AssertCodeAsync(
            await luis.Client.GetAsync(new Uri($"/api/v1/orders?conversationId={conversation}", UriKind.Relative), Ct),
            HttpStatusCode.NotFound,
            "orders.conversation_not_found");
    }

    [Fact]
    public async Task Quantity_Point_AndTime_AreValidated()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var conversation = await ChatAsync(ana, await PublishAsync(diego, stock: 2));

        await AuthFlow.AssertCodeAsync(await CreateAsync(ana, conversation, quantity: 3), HttpStatusCode.Conflict, "orders.not_enough_stock");
        await AuthFlow.AssertCodeAsync(await CreateAsync(ana, conversation, quantity: 0), HttpStatusCode.BadRequest, "orders.invalid_quantity");
        await AuthFlow.AssertCodeAsync(await CreateAsync(ana, conversation, point: "estacionamiento"), HttpStatusCode.BadRequest, "orders.invalid_meeting_point");
        await AuthFlow.AssertCodeAsync(
            await CreateAsync(ana, conversation, at: factory.Clock.GetUtcNow().UtcDateTime.AddDays(20)),
            HttpStatusCode.BadRequest,
            "orders.invalid_delivery_time");
    }

    [Fact]
    public async Task FromAnAcceptedOffer_TheOrderUsesTheOfferPrice()
    {
        var ana = await MemberAsync();
        var valeria = await MemberAsync();
        var request = await AuthFlow.JsonAsync(await ana.Client.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Bata de laboratorio talla M", category = "laboratorio", budgetCents = 30_000, quantity = 1 },
            Ct));
        var requestId = request.GetProperty("id").GetString();
        var offerId = (await AuthFlow.JsonAsync(await valeria.Client.PutAsJsonAsync(
            new Uri($"/api/v1/wanted/{requestId}/offers/mine", UriKind.Relative),
            new { description = "Bata blanca de algodón, talla M", priceCents = 28_000, condition = "used" },
            Ct))).GetProperty("id").GetString();
        var accepted = await AuthFlow.JsonAsync(await ana.Client.PostAsync(new Uri($"/api/v1/wanted/{requestId}/offers/{offerId}/accept", UriKind.Relative), null, Ct));

        var created = await CreateAsync(ana, accepted.GetProperty("conversationId").GetString()!);
        var order = await AuthFlow.JsonAsync(created);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("offer", order.GetProperty("item").GetProperty("source").GetString());
        Assert.Equal(offerId, order.GetProperty("item").GetProperty("sourceId").GetString());
        Assert.Equal(28_000, order.GetProperty("item").GetProperty("priceCents").GetInt64());
    }

    [Fact]
    public async Task AListingWithAPendingOrder_IsNotDeleted()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var product = await PublishAsync(diego);
        await CreateAsync(ana, await ChatAsync(ana, product));

        var deleted = await diego.Client.DeleteAsync(new Uri($"/api/v1/products/{product}", UriKind.Relative), Ct);

        await AuthFlow.AssertCodeAsync(deleted, HttpStatusCode.Conflict, "listings.product_has_orders");
    }

    [Fact]
    public async Task DeletingTheBuyerAccount_CancelsTheOrder_ForTheSeller()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var id = (await AuthFlow.JsonAsync(await CreateAsync(ana, await ChatAsync(ana, await PublishAsync(diego))))).GetProperty("id").GetString();

        var deleted = await ana.Client.PostAsJsonAsync(new Uri("/api/v1/account/delete", UriKind.Relative), new { password = AuthFlow.Password }, Ct);
        var asSeller = await GetJsonAsync(diego, $"/api/v1/orders/{id}");

        Assert.True(deleted.IsSuccessStatusCode);
        Assert.Equal("cancelled", asSeller.GetProperty("status").GetString());
        Assert.Equal("Usuario eliminado", asSeller.GetProperty("buyer").GetProperty("name").GetString());
    }

    [Fact]
    public async Task MeetingPoints_AreTheFourteenCampusBuildings()
    {
        var ana = await MemberAsync();

        var points = await GetJsonAsync(ana, "/api/v1/orders/meeting-points");

        Assert.Equal(14, points.GetProperty("items").GetArrayLength());
        Assert.Equal("biblioteca", points.GetProperty("items")[0].GetProperty("slug").GetString());
    }
}
