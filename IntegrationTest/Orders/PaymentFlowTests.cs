using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Orders;

/// <summary>
/// HU-55 por HTTP (D-01). Por transferencia: Diego registra su cuenta (CLABE cifrada, se ve enmascarada), Ana la elige,
/// ve la CLABE completa, captura la referencia, Diego dice que todavia no le llega, Ana la vuelve a mandar y Diego
/// confirma que le llego: el pedido queda Confirmado. En efectivo: al aceptar, Ana ve su codigo de 4 digitos y, cuando
/// Diego lo escribe, el pedido se completa. Cada paso le llega a la otra parte en su centro de avisos.
/// </summary>
public sealed class PaymentFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    // CLABE de BBVA México con su dígito verificador.
    private const string Clabe = "012180015488214829";

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

    private static async Task<string> PublishAsync(Member seller)
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
                stock = 1,
                photos = new[] { key },
            },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private async Task<string> OrderAsync(Member buyer, string productId)
    {
        var started = await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId }, Ct).ConfigureAwait(false);
        var conversationId = (await AuthFlow.JsonAsync(started).ConfigureAwait(false)).GetProperty("id").GetString();
        var created = await buyer.Client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative),
            new { conversationId, quantity = 1, meetingPoint = "biblioteca", deliveryAt = Tomorrow() },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await AuthFlow.JsonAsync(created).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> PostAsync(Member member, string path, object? body = null) =>
        body is null
            ? member.Client.PostAsync(new Uri(path, UriKind.Relative), null, Ct)
            : member.Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct);

    private static async Task<JsonElement> GetAsync(Member member, string path) =>
        await AuthFlow.JsonAsync(await member.Client.GetAsync(new Uri(path, UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);

    private static async Task<List<string>> NoticesAsync(Member member)
    {
        var body = await GetAsync(member, "/api/v1/notifications?category=orders").ConfigureAwait(false);
        return [.. body.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()!)];
    }

    [Fact]
    public async Task ByTransfer_TheBuyerSeesTheAccount_ReportsIt_AndTheSellerConfirms()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var product = await PublishAsync(diego);
        var order = await OrderAsync(ana, product);

        // Sin cuenta, la transferencia no se puede elegir.
        Assert.False((await GetAsync(ana, $"/api/v1/orders/{order}")).GetProperty("transferAvailable").GetBoolean());
        await AuthFlow.AssertCodeAsync(
            await ana.Client.PutAsJsonAsync(new Uri($"/api/v1/orders/{order}/payment", UriKind.Relative), new { method = "transfer" }, Ct),
            HttpStatusCode.Conflict,
            "orders.payment_unavailable");

        var mismatch = await diego.Client.PutAsJsonAsync(new Uri("/api/v1/payouts/account", UriKind.Relative), new { holderName = "Diego Hernández", clabe = Clabe }, Ct);
        var saved = await diego.Client.PutAsJsonAsync(new Uri("/api/v1/payouts/account", UriKind.Relative), new { holderName = "ana sofia prueba", clabe = "012 180 0154 8821 4829" }, Ct);
        await AuthFlow.AssertCodeAsync(mismatch, HttpStatusCode.UnprocessableEntity, "payouts.holder_mismatch");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var mine = await GetAsync(diego, "/api/v1/payouts/account");
        Assert.Equal(("BBVA México", "4829"), (mine.GetProperty("account").GetProperty("bankName").GetString(), mine.GetProperty("account").GetProperty("clabeLast4").GetString()));
        Assert.DoesNotContain(Clabe, mine.GetRawText(), StringComparison.Ordinal);

        Assert.True((await GetAsync(ana, $"/api/v1/orders/{order}")).GetProperty("transferAvailable").GetBoolean());
        var chosen = await ana.Client.PutAsJsonAsync(new Uri($"/api/v1/orders/{order}/payment", UriKind.Relative), new { method = "transfer" }, Ct);
        Assert.Equal(HttpStatusCode.OK, chosen.StatusCode);
        var forAna = await GetAsync(ana, $"/api/v1/orders/{order}/transfer");
        var forDiego = await GetAsync(diego, $"/api/v1/orders/{order}/transfer");
        Assert.Equal(("012 180 0154 8821 4829", 50_000L), (forAna.GetProperty("clabe").GetString(), forAna.GetProperty("amountCents").GetInt64()));
        Assert.Equal("•••• 4829", forDiego.GetProperty("clabe").GetString());

        await AuthFlow.AssertCodeAsync(await PostAsync(diego, $"/api/v1/orders/{order}/accept"), HttpStatusCode.Conflict, "orders.transfer_not_reported");
        var reported = await PostAsync(ana, $"/api/v1/orders/{order}/transfer", new { reference = "4829173" });
        Assert.Equal(HttpStatusCode.OK, reported.StatusCode);
        Assert.Contains("transfer_reported", await NoticesAsync(diego));

        var notYet = await PostAsync(diego, $"/api/v1/orders/{order}/transfer/not-received");
        Assert.Equal(HttpStatusCode.OK, notYet.StatusCode);
        Assert.Contains("transfer_not_received", await NoticesAsync(ana));
        await PostAsync(ana, $"/api/v1/orders/{order}/transfer", new { reference = "4829174" });

        var confirmed = await AuthFlow.JsonAsync(await PostAsync(diego, $"/api/v1/orders/{order}/accept"));
        Assert.Equal("confirmed", confirmed.GetProperty("status").GetString());
        Assert.Equal("4829174", confirmed.GetProperty("payment").GetProperty("reference").GetString());
        Assert.Equal(JsonValueKind.String, confirmed.GetProperty("payment").GetProperty("validatedAt").ValueKind);
        Assert.Contains("order_confirmed", await NoticesAsync(ana));
    }

    [Fact]
    public async Task InCash_OnlyTheBuyerSeesTheCode_AndTheSellerCompletesTheOrderWithIt()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var product = await PublishAsync(diego);
        var order = await OrderAsync(ana, product);
        await ana.Client.PutAsJsonAsync(new Uri($"/api/v1/orders/{order}/payment", UriKind.Relative), new { method = "cash" }, Ct);
        Assert.Equal(JsonValueKind.Null, (await GetAsync(ana, $"/api/v1/orders/{order}")).GetProperty("payment").GetProperty("deliveryCode").ValueKind);

        await PostAsync(diego, $"/api/v1/orders/{order}/accept");
        var code = (await GetAsync(ana, $"/api/v1/orders/{order}")).GetProperty("payment").GetProperty("deliveryCode").GetString()!;
        var forDiego = (await GetAsync(diego, $"/api/v1/orders/{order}")).GetProperty("payment");
        var wrong = await PostAsync(diego, $"/api/v1/orders/{order}/delivery-code", new { code = code == "0000" ? "1111" : "0000" });
        var byAna = await PostAsync(ana, $"/api/v1/orders/{order}/delivery-code", new { code });
        var completed = await PostAsync(diego, $"/api/v1/orders/{order}/delivery-code", new { code });

        Assert.Matches("^[0-9]{4}$", code);
        Assert.Equal((JsonValueKind.Null, 5), (forDiego.GetProperty("deliveryCode").ValueKind, forDiego.GetProperty("codeAttemptsLeft").GetInt32()));
        await AuthFlow.AssertCodeAsync(wrong, HttpStatusCode.UnprocessableEntity, "orders.invalid_code");
        await AuthFlow.AssertCodeAsync(byAna, HttpStatusCode.Forbidden, "orders.only_seller");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("completed", (await AuthFlow.JsonAsync(completed)).GetProperty("status").GetString());
        Assert.Contains("order_completed", await NoticesAsync(ana));
        Assert.Equal("sold", (await GetAsync(diego, $"/api/v1/products/{product}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task PayoutAccount_BanksRegisterAndRemove_LeavesOnlyCash()
    {
        var diego = await MemberAsync();

        var banks = await GetAsync(diego, "/api/v1/payouts/banks");
        var saved = await diego.Client.PutAsJsonAsync(new Uri("/api/v1/payouts/account", UriKind.Relative), new { holderName = "Ana Sofía Prueba", clabe = Clabe }, Ct);
        var before = await GetAsync(diego, "/api/v1/payouts/account");
        var removed = await diego.Client.DeleteAsync(new Uri("/api/v1/payouts/account", UriKind.Relative), Ct);
        var again = await diego.Client.DeleteAsync(new Uri("/api/v1/payouts/account", UriKind.Relative), Ct);
        var after = await GetAsync(diego, "/api/v1/payouts/account");

        Assert.Contains(banks.GetProperty("items").EnumerateArray(), b => b.GetProperty("code").GetString() == "012" && b.GetProperty("name").GetString() == "BBVA México");
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("4829", before.GetProperty("account").GetProperty("clabeLast4").GetString());
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        // Idempotente: quitarla otra vez no es error.
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(JsonValueKind.Null, after.GetProperty("account").ValueKind);
    }
}
