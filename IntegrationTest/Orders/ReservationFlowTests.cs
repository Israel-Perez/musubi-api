using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Orders;

/// <summary>
/// HU-52 por HTTP: Luis aparta el juego de escuadras de Valeria (la pieza se guarda y se abre su chat), ve su QR y su
/// codigo; Valeria lo ve en "Me apartaron", verifica el codigo y confirma la entrega: nace un pedido completado que
/// Luis puede calificar. Uno activo a la vez.
/// </summary>
public sealed class ReservationFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Member(HttpClient Client, string Id);

    private async Task<Member> MemberAsync()
    {
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

    // Dentro de las 24 h y entre las 7:00 y las 21:00 del campus.
    private DateTime PickupAt()
    {
        var campus = factory.Clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(6);
        var slot = campus.Date.AddHours(campus.Hour < 19 ? Math.Max(campus.Hour + 1, 8) : 32);
        return DateTime.SpecifyKind(slot + TimeSpan.FromHours(6), DateTimeKind.Utc);
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

    private static async Task<string> PublishAsync(Member seller, string title)
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
                title,
                description = "Completo, con su estuche.",
                category = "calculadoras",
                priceCents = 18_000,
                condition = "used",
                conditionDetail = "good",
                stock = 1,
                photos = new[] { key },
            },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> PostAsync(Member member, string path, object? body = null) =>
        body is null
            ? member.Client.PostAsync(new Uri(path, UriKind.Relative), null, Ct)
            : member.Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct);

    private static async Task<JsonElement> GetAsync(Member member, string path) =>
        await AuthFlow.JsonAsync(await member.Client.GetAsync(new Uri(path, UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);

    [Fact]
    public async Task ReserveVerifyAndDeliver_EndsInACompletedOrder()
    {
        var valeria = await MemberAsync();
        var luis = await MemberAsync();
        var product = await PublishAsync(valeria, "Juego de escuadras y compás");
        var another = await PublishAsync(valeria, "Calculadora Casio");

        var created = await PostAsync(luis, "/api/v1/reservations", new { productId = product, meetingPoint = "biblioteca", pickupAt = PickupAt() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var reservation = await AuthFlow.JsonAsync(created);
        var id = reservation.GetProperty("id").GetString();
        var code = reservation.GetProperty("code").GetString()!;
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(0, (await GetAsync(valeria, $"/api/v1/products/{product}")).GetProperty("stock").GetInt32());

        // Uno activo a la vez.
        await AuthFlow.AssertCodeAsync(
            await PostAsync(luis, "/api/v1/reservations", new { productId = another, meetingPoint = "biblioteca", pickupAt = PickupAt() }),
            HttpStatusCode.Conflict,
            "reservations.limit_reached");

        var sold = await GetAsync(valeria, "/api/v1/reservations/mine?tab=seller");
        var mine = sold.GetProperty("items")[0];
        Assert.Equal((id, JsonValueKind.Null, 5), (mine.GetProperty("id").GetString(), mine.GetProperty("code").ValueKind, mine.GetProperty("codeAttemptsLeft").GetInt32()));

        await AuthFlow.AssertCodeAsync(await PostAsync(valeria, $"/api/v1/reservations/{id}/complete"), HttpStatusCode.Conflict, "reservations.not_verified");
        await AuthFlow.AssertCodeAsync(
            await PostAsync(valeria, $"/api/v1/reservations/{id}/verify", new { code = code == "000000" ? "111111" : "000000" }),
            HttpStatusCode.UnprocessableEntity,
            "reservations.invalid_code");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(valeria, $"/api/v1/reservations/{id}/verify", new { code })).StatusCode);
        var collected = await AuthFlow.JsonAsync(await PostAsync(valeria, $"/api/v1/reservations/{id}/complete"));

        Assert.Equal("collected", collected.GetProperty("status").GetString());
        var order = await GetAsync(luis, $"/api/v1/orders/{collected.GetProperty("orderId").GetString()}");
        Assert.Equal(("completed", "buyer"), (order.GetProperty("status").GetString(), order.GetProperty("role").GetString()));
        Assert.Equal(JsonValueKind.String, order.GetProperty("rateUntil").ValueKind);
        Assert.Equal("sold", (await GetAsync(valeria, $"/api/v1/products/{product}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancelling_ReturnsThePiece_AndAnotherCanBeReserved()
    {
        var valeria = await MemberAsync();
        var luis = await MemberAsync();
        var product = await PublishAsync(valeria, "Juego de escuadras y compás");
        var id = (await AuthFlow.JsonAsync(await PostAsync(luis, "/api/v1/reservations", new { productId = product, meetingPoint = "biblioteca", pickupAt = PickupAt() })))
            .GetProperty("id").GetString();

        var cancelled = await AuthFlow.JsonAsync(await PostAsync(valeria, $"/api/v1/reservations/{id}/cancel"));
        var again = await PostAsync(luis, "/api/v1/reservations", new { productId = product, meetingPoint = "cafeteria", pickupAt = PickupAt() });

        Assert.Equal(("cancelled", "seller"), (cancelled.GetProperty("status").GetString(), cancelled.GetProperty("cancelledBy").GetString()));
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Contains(
            "reservation_cancelled",
            (await GetAsync(luis, "/api/v1/notifications?category=orders")).GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()));
    }
}
