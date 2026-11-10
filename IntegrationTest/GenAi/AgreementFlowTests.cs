using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace IntegrationTest.GenAi;

/// <summary>
/// HU-32/33/34 de punta a punta, con la IA en memoria de Development: Ana platica con Diego, pide el resumen, el chat
/// avisa a los dos, Diego corrige el lugar, los dos confirman y Ana crea el pedido con lo acordado (RF-47); el acuerdo
/// desaparece. Sin IA, "Resumir acuerdo" lo dice y el chat y los pedidos siguen igual (RNF-15).
/// </summary>
public sealed class AgreementFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Member(HttpClient Client, string Id);

    // Registra, verifica e inicia sesion en el host indicado (los correos se capturan en la fabrica).
    private async Task<Member> MemberAsync(WebApplicationFactory<Program>? host = null)
    {
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var app = host ?? factory;
        var email = AuthFlow.NewEmail();
        using var anonymous = app.CreateClient();
        var registered = await anonymous.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Ana Sofía Prueba", password = AuthFlow.Password, acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var verified = await anonymous.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = factory.Emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await AuthFlow.JsonAsync(verified).ConfigureAwait(false);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var me = await AuthFlow.JsonAsync(await client.GetAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return new Member(client, me.GetProperty("id").GetString()!);
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
                stock = 2,
                photos = new[] { key },
            },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static async Task<string> ChatAsync(Member buyer, Member seller)
    {
        var started = await buyer.Client.PostAsJsonAsync(
            new Uri("/api/v1/conversations", UriKind.Relative), new { productId = await PublishAsync(seller).ConfigureAwait(false) }, Ct).ConfigureAwait(false);
        return (await AuthFlow.JsonAsync(started).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static async Task SayAsync(Member member, string conversation, string text)
    {
        var sent = await member.Client.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversation}/messages", UriKind.Relative), new { text }, Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
    }

    private static async Task<JsonElement> GetJsonAsync(Member member, string path)
    {
        var response = await member.Client.GetAsync(new Uri(path, UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    [Fact]
    public async Task AnaAsks_BothAreTold_DiegoFixesThePlace_BothConfirm_AndAnaCreatesTheOrder()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var conversation = await ChatAsync(ana, diego);
        await SayAsync(ana, conversation, "¿Me la dejas en $450?");
        await SayAsync(diego, conversation, "Va, mañana a la 1 en la Biblioteca");

        var asked = await ana.Client.PostAsJsonAsync(new Uri("/api/v1/agreements", UriKind.Relative), new { conversationId = conversation }, Ct);
        var proposed = await AuthFlow.JsonAsync(asked);
        var id = proposed.GetProperty("id").GetString();
        var chat = await GetJsonAsync(diego, $"/api/v1/conversations/{conversation}/messages");
        var forDiego = await GetJsonAsync(diego, $"/api/v1/agreements?conversationId={conversation}");

        Assert.Equal(HttpStatusCode.Created, asked.StatusCode);
        Assert.Equal("proposed", proposed.GetProperty("status").GetString());
        Assert.Equal(45_000, proposed.GetProperty("proposal").GetProperty("priceCents").GetInt64());
        Assert.Equal("biblioteca", proposed.GetProperty("proposal").GetProperty("meetingPoint").GetString());
        Assert.Equal(JsonValueKind.String, proposed.GetProperty("proposal").GetProperty("deliveryAt").ValueKind);
        var notice = chat.GetProperty("items")[0];
        Assert.Equal(("notice", ana.Id), (notice.GetProperty("kind").GetString(), notice.GetProperty("senderId").GetString()));
        Assert.EndsWith("pidió un resumen con IA", notice.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("seller", forDiego.GetProperty("items")[0].GetProperty("role").GetString());

        var fixedPlace = await diego.Client.PutAsJsonAsync(
            new Uri($"/api/v1/agreements/{id}", UriKind.Relative), new { revision = 1, meetingPoint = "cafeteria", meetingDetail = "Junto a la caja" }, Ct);
        var anaConfirms = await ana.Client.PostAsJsonAsync(new Uri($"/api/v1/agreements/{id}/confirm", UriKind.Relative), new { revision = 2 }, Ct);
        var early = await ana.Client.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new { agreementId = id }, Ct);
        var diegoConfirms = await diego.Client.PostAsJsonAsync(new Uri($"/api/v1/agreements/{id}/confirm", UriKind.Relative), new { revision = 2 }, Ct);
        var confirmed = await AuthFlow.JsonAsync(diegoConfirms);

        Assert.Equal(HttpStatusCode.OK, fixedPlace.StatusCode);
        Assert.Equal(HttpStatusCode.OK, anaConfirms.StatusCode);
        await AuthFlow.AssertCodeAsync(early, HttpStatusCode.Conflict, "orders.agreement_not_confirmed");
        Assert.Equal("confirmed", confirmed.GetProperty("status").GetString());

        var created = await ana.Client.PostAsJsonAsync(new Uri("/api/v1/orders", UriKind.Relative), new { agreementId = id }, Ct);
        var order = await AuthFlow.JsonAsync(created);
        var gone = await GetJsonAsync(ana, $"/api/v1/agreements?conversationId={conversation}");
        var again = await ana.Client.PostAsJsonAsync(new Uri("/api/v1/agreements", UriKind.Relative), new { conversationId = conversation }, Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(45_000, order.GetProperty("item").GetProperty("priceCents").GetInt64());
        Assert.Equal("Cafetería", order.GetProperty("delivery").GetProperty("pointName").GetString());
        Assert.Equal("Junto a la caja", order.GetProperty("delivery").GetProperty("detail").GetString());
        Assert.Empty(gone.GetProperty("items").EnumerateArray());
        await AuthFlow.AssertCodeAsync(again, HttpStatusCode.Conflict, "agreements.order_open");
    }

    [Fact]
    public async Task Discarding_LeavesTheChatAsItWas()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var conversation = await ChatAsync(ana, diego);
        await SayAsync(ana, conversation, "¿Me la dejas en $450?");
        var proposed = await AuthFlow.JsonAsync(await diego.Client.PostAsJsonAsync(new Uri("/api/v1/agreements", UriKind.Relative), new { conversationId = conversation }, Ct));

        var discarded = await ana.Client.DeleteAsync(new Uri($"/api/v1/agreements/{proposed.GetProperty("id").GetString()}", UriKind.Relative), Ct);
        var after = await GetJsonAsync(diego, $"/api/v1/agreements?conversationId={conversation}");

        Assert.Equal(HttpStatusCode.NoContent, discarded.StatusCode);
        Assert.Empty(after.GetProperty("items").EnumerateArray());
        await SayAsync(diego, conversation, "Va, entonces lo platicamos");
    }

    [Fact]
    public async Task WithoutAi_SummarizingSaysSo_AndTheChatAndOrdersKeepWorking()
    {
        using var off = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["GenAi:Provider"] = "Off" })));
        var diego = await MemberAsync(off);
        var ana = await MemberAsync(off);
        var conversation = await ChatAsync(ana, diego);
        await SayAsync(ana, conversation, "¿Me la dejas en $450?");

        var asked = await ana.Client.PostAsJsonAsync(new Uri("/api/v1/agreements", UriKind.Relative), new { conversationId = conversation }, Ct);
        var tomorrow = DateTime.SpecifyKind((DateTime.UtcNow - TimeSpan.FromHours(6)).Date.AddDays(1).AddHours(19), DateTimeKind.Utc);
        var manual = await ana.Client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative), new { conversationId = conversation, quantity = 1, meetingPoint = "biblioteca", deliveryAt = tomorrow }, Ct);

        await AuthFlow.AssertCodeAsync(asked, HttpStatusCode.ServiceUnavailable, "genai.unavailable");
        await SayAsync(diego, conversation, "Sin IA también se puede");
        Assert.Equal(HttpStatusCode.Created, manual.StatusCode);
    }
}
