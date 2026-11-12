using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Reputation;

/// <summary>
/// HU-57: "Bloquear a …" desde el chat corta la conversacion para los dos y oculta las publicaciones y el perfil
/// en las dos direcciones; "Desbloquear" lo regresa. HU-28: "Enviar reporte" desde el chat y desde Ajustes.
/// </summary>
public sealed class ReputationFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
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

    /// <summary>El vendedor publica un producto con un nombre unico y devuelve su id.</summary>
    private static async Task<(string Id, string Title)> PublishAsync(Member seller)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString();
        var title = "Bata de laboratorio " + Guid.NewGuid().ToString("N")[..6];
        var published = await seller.Client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title, description = "Talla M, casi nueva.", category = "laboratorio", priceCents = 25_000, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { key } },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return ((await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!, title);
    }

    private static async Task<bool> InShowcaseAsync(Member viewer, string productId)
    {
        var page = await AuthFlow.JsonAsync(await viewer.Client.GetAsync(new Uri("/api/v1/products?limit=50", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return page.GetProperty("items").EnumerateArray().Any(p => p.GetProperty("id").GetString() == productId);
    }

    private static async Task<bool> InSearchAsync(Member viewer, string productId, string title)
    {
        var page = await AuthFlow.JsonAsync(await viewer.Client.GetAsync(new Uri($"/api/v1/products?q={Uri.EscapeDataString(title)}", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return page.GetProperty("items").EnumerateArray().Any(p => p.GetProperty("id").GetString() == productId);
    }

    private static Task<HttpResponseMessage> SendAsync(Member member, string conversationId, string text) =>
        member.Client.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative), new { text }, Ct);

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await AuthFlow.JsonAsync(response).ConfigureAwait(false)).GetProperty("code").GetString()!;

    [Fact]
    public async Task Block_CutsTheChatForBoth_HidesListingsAndProfileBothWays_AndUnblockRestoresIt()
    {
        var seller = await MemberAsync();
        var buyer = await MemberAsync();
        var (productId, title) = await PublishAsync(seller);
        var started = await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId }, Ct);
        var conversationId = (await AuthFlow.JsonAsync(started)).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(buyer, conversationId, "¿Sigue disponible?")).StatusCode);

        // El vendedor bloquea al comprador desde "Mas opciones".
        var blocked = await seller.Client.PostAsJsonAsync(new Uri("/api/v1/blocks", UriKind.Relative), new { userId = buyer.Id }, Ct);
        var again = await seller.Client.PostAsJsonAsync(new Uri("/api/v1/blocks", UriKind.Relative), new { userId = buyer.Id }, Ct);

        var buyerWrites = await SendAsync(buyer, conversationId, "¿Hola?");
        var sellerWrites = await SendAsync(seller, conversationId, "Hola");
        var reopen = await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId }, Ct);
        var conversation = await AuthFlow.JsonAsync(await buyer.Client.GetAsync(new Uri($"/api/v1/conversations/{conversationId}", UriKind.Relative), Ct));
        var detail = await buyer.Client.GetAsync(new Uri($"/api/v1/products/{productId}", UriKind.Relative), Ct);
        var buyerSeesSeller = await buyer.Client.GetAsync(new Uri($"/api/v1/profiles/{seller.Id}", UriKind.Relative), Ct);
        var sellerSeesBuyer = await seller.Client.GetAsync(new Uri($"/api/v1/profiles/{buyer.Id}", UriKind.Relative), Ct);
        var buyerSeesSellerListings = await buyer.Client.GetAsync(
            new Uri($"/api/v1/products?sellerId={seller.Id}&hideSoldOut=true", UriKind.Relative), Ct);
        var list = await AuthFlow.JsonAsync(await seller.Client.GetAsync(new Uri("/api/v1/blocks", UriKind.Relative), Ct));

        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, buyerWrites.StatusCode);
        Assert.Equal("chat.blocked", await ProblemCodeAsync(buyerWrites));
        Assert.Equal(HttpStatusCode.Forbidden, sellerWrites.StatusCode);
        Assert.Equal("chat.blocked", await ProblemCodeAsync(reopen));
        Assert.False(conversation.GetProperty("canReply").GetBoolean());
        Assert.False(await InShowcaseAsync(buyer, productId));
        Assert.False(await InSearchAsync(buyer, productId, title));
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, buyerSeesSeller.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, sellerSeesBuyer.StatusCode);
        Assert.Equal(0, (await AuthFlow.JsonAsync(buyerSeesSellerListings)).GetProperty("total").GetInt64());
        Assert.Equal(buyer.Id, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());

        // "Desbloquear" en Ajustes → Privacidad.
        var unblocked = await seller.Client.DeleteAsync(new Uri($"/api/v1/blocks/{buyer.Id}", UriKind.Relative), Ct);
        var unblockedAgain = await seller.Client.DeleteAsync(new Uri($"/api/v1/blocks/{buyer.Id}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NoContent, unblocked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unblockedAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(buyer, conversationId, "¿Ya podemos hablar?")).StatusCode);
        Assert.True(await InShowcaseAsync(buyer, productId));
        Assert.Equal(HttpStatusCode.OK, (await buyer.Client.GetAsync(new Uri($"/api/v1/products/{productId}", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Block_Yourself_OrAnUnknownPerson_IsRejected()
    {
        var me = await MemberAsync();

        var self = await me.Client.PostAsJsonAsync(new Uri("/api/v1/blocks", UriKind.Relative), new { userId = me.Id }, Ct);
        var unknown = await me.Client.PostAsJsonAsync(new Uri("/api/v1/blocks", UriKind.Relative), new { userId = "6716a0000000000000000099" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        Assert.Equal("blocks.self", await ProblemCodeAsync(self));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("blocks.user_not_found", await ProblemCodeAsync(unknown));
    }

    [Fact]
    public async Task Report_FromTheChat_AndFromSettings_AreReceived_AndAWrongReasonIsRejected()
    {
        var seller = await MemberAsync();
        var buyer = await MemberAsync();
        var (productId, _) = await PublishAsync(seller);
        var started = await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId }, Ct);
        var conversationId = (await AuthFlow.JsonAsync(started)).GetProperty("id").GetString();

        var fromChat = await buyer.Client.PostAsJsonAsync(
            new Uri("/api/v1/reports", UriKind.Relative),
            new { targetType = "user", targetId = seller.Id, conversationId, reason = "off_platform", details = "Me pidió depositarle antes de vernos." },
            Ct);
        var fromSettings = await buyer.Client.PostAsJsonAsync(
            new Uri("/api/v1/reports", UriKind.Relative), new { targetType = "general", reason = "other", details = "La app se cierra al abrir el chat." }, Ct);
        var wrongReason = await buyer.Client.PostAsJsonAsync(
            new Uri("/api/v1/reports", UriKind.Relative), new { targetType = "general", reason = "spam" }, Ct);
        var outsider = await MemberAsync();
        var notInTheConversation = await outsider.Client.PostAsJsonAsync(
            new Uri("/api/v1/reports", UriKind.Relative), new { targetType = "user", targetId = seller.Id, conversationId, reason = "payment" }, Ct);

        Assert.Equal(HttpStatusCode.Created, fromChat.StatusCode);
        Assert.Equal(24, (await AuthFlow.JsonAsync(fromChat)).GetProperty("id").GetString()!.Length);
        Assert.Equal(HttpStatusCode.Created, fromSettings.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongReason.StatusCode);
        Assert.Equal("reports.invalid_reason", await ProblemCodeAsync(wrongReason));
        Assert.Equal(HttpStatusCode.NotFound, notInTheConversation.StatusCode);
        Assert.Equal("reports.target_not_found", await ProblemCodeAsync(notInTheConversation));
    }
}
