using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using SkiaSharp;

namespace IntegrationTest.Chat;

/// <summary>
/// HU-20: "Mensaje" en una publicacion abre la conversacion con el vendedor (maqueta <c>Producto</c> →
/// <c>Chat-Valeria</c>), los mensajes se guardan y llegan en tiempo real por <c>/hubs/chat</c>, y solo los
/// participantes leen la conversacion (RN-22).
/// </summary>
public sealed class ChatFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Member(HttpClient Client, string Token, string Id, string Email);

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
        var token = login.GetProperty("accessToken").GetString()!;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await GetJsonAsync(client, "/api/v1/profiles/me").ConfigureAwait(false);
        return new Member(client, token, me.GetProperty("id").GetString()!, email);
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

    /// <summary>El vendedor publica una calculadora y devuelve su id.</summary>
    private static async Task<string> PublishAsync(Member seller)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString();

        var published = await seller.Client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new
            {
                title = "Calculadora Casio fx-991ES Plus",
                description = "La usé un semestre.",
                category = "calculadoras",
                priceCents = 45_000,
                condition = "used",
                conditionDetail = "good",
                stock = 1,
                photos = new[] { key },
            },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static Task<HttpResponseMessage> StartAsync(Member buyer, string productId) =>
        buyer.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId }, Ct);

    private static Task<HttpResponseMessage> SendAsync(Member member, string conversationId, string text) =>
        member.Client.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative), new { text }, Ct);

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    private async Task<(Member Buyer, Member Seller, string ConversationId)> ConversationAsync()
    {
        var seller = await MemberAsync().ConfigureAwait(false);
        var buyer = await MemberAsync().ConfigureAwait(false);
        var product = await PublishAsync(seller).ConfigureAwait(false);
        var started = await StartAsync(buyer, product).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        return (buyer, seller, (await AuthFlow.JsonAsync(started).ConfigureAwait(false)).GetProperty("id").GetString()!);
    }

    /// <summary>Conexion al hub como la app: solo WebSockets, sin negociar y con el token en la query.</summary>
    private HubConnection Hub(Member member) => new HubConnectionBuilder()
        .WithUrl(
            new Uri(factory.Server.BaseAddress, $"/hubs/chat?access_token={Uri.EscapeDataString(member.Token)}"),
            options =>
            {
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
                options.WebSocketFactory = async (context, ct) =>
                    await factory.Server.CreateWebSocketClient().ConnectAsync(context.Uri, ct).ConfigureAwait(false);
            })
        .Build();

    [Fact]
    public async Task Start_FromAProduct_Is201WithTheSeller_AndAgainIs200WithTheSameConversation()
    {
        var seller = await MemberAsync();
        var buyer = await MemberAsync();
        var product = await PublishAsync(seller);

        var first = await StartAsync(buyer, product);
        var again = await StartAsync(buyer, product);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var conversation = await AuthFlow.JsonAsync(first);
        Assert.Equal(conversation.GetProperty("id").GetString(), (await AuthFlow.JsonAsync(again)).GetProperty("id").GetString());
        Assert.Equal("buyer", conversation.GetProperty("role").GetString());
        Assert.Equal(seller.Id, conversation.GetProperty("counterpart").GetProperty("id").GetString());
        Assert.Equal("Ana Sofía Prueba", conversation.GetProperty("counterpart").GetProperty("name").GetString());
        var context = conversation.GetProperty("context");
        Assert.Equal("product", context.GetProperty("type").GetString());
        Assert.Equal(product, context.GetProperty("id").GetString());
        Assert.Equal(45_000, context.GetProperty("priceCents").GetInt64());
        Assert.StartsWith("/media/products/", context.GetProperty("photo").GetString(), StringComparison.Ordinal);
        Assert.True(context.GetProperty("available").GetBoolean());

        // El vendedor ve la misma conversacion desde su lado.
        var sellerSide = await GetJsonAsync(seller.Client, $"/api/v1/conversations/{conversation.GetProperty("id").GetString()}");
        Assert.Equal("seller", sellerSide.GetProperty("role").GetString());
        Assert.Equal(buyer.Id, sellerSide.GetProperty("counterpart").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Start_OnMyOwnProduct_Is400()
    {
        var seller = await MemberAsync();
        var product = await PublishAsync(seller);

        await AuthFlow.AssertCodeAsync(await StartAsync(seller, product), HttpStatusCode.BadRequest, "chat.own_product");
    }

    [Theory]
    [InlineData("6716a0000000000000000001")]
    [InlineData("no-es-un-id")]
    [InlineData("")]
    public async Task Start_OnAnUnknownProduct_Is404(string productId)
    {
        var buyer = await MemberAsync();

        await AuthFlow.AssertCodeAsync(await StartAsync(buyer, productId), HttpStatusCode.NotFound, "chat.product_unavailable");
    }

    [Fact]
    public async Task Messages_AreSaved_AndReadNewestFirstWithACursor()
    {
        var (buyer, seller, conversation) = await ConversationAsync();

        var sent = await SendAsync(buyer, conversation, "  ¡Hola! ¿Sigue disponible la calculadora?  ");
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(seller, conversation, "Claro, sigue disponible.")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(buyer, conversation, "¿Mañana en la biblioteca?")).StatusCode);

        var message = await AuthFlow.JsonAsync(sent);
        Assert.Equal("¡Hola! ¿Sigue disponible la calculadora?", message.GetProperty("text").GetString());
        Assert.Equal(buyer.Id, message.GetProperty("senderId").GetString());

        var page = await GetJsonAsync(seller.Client, $"/api/v1/conversations/{conversation}/messages?limit=2");
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["¿Mañana en la biblioteca?", "Claro, sigue disponible."], items.Select(i => i.GetProperty("text").GetString()));
        var cursor = page.GetProperty("nextCursor").GetString();

        var older = await GetJsonAsync(buyer.Client, $"/api/v1/conversations/{conversation}/messages?limit=2&cursor={cursor}");
        Assert.Single(older.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, older.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task SomeoneElse_CannotReadOrWrite_TheConversation()
    {
        var (_, _, conversation) = await ConversationAsync();
        var outsider = await MemberAsync();

        var get = await outsider.Client.GetAsync(new Uri($"/api/v1/conversations/{conversation}", UriKind.Relative), Ct);
        var read = await outsider.Client.GetAsync(new Uri($"/api/v1/conversations/{conversation}/messages", UriKind.Relative), Ct);
        var write = await SendAsync(outsider, conversation, "Hola");

        // RN-22: la misma respuesta que una conversacion inexistente (no se revela que existe).
        await AuthFlow.AssertCodeAsync(get, HttpStatusCode.NotFound, "chat.conversation_not_found");
        await AuthFlow.AssertCodeAsync(read, HttpStatusCode.NotFound, "chat.conversation_not_found");
        await AuthFlow.AssertCodeAsync(write, HttpStatusCode.NotFound, "chat.conversation_not_found");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Send_AnEmptyMessage_Is400(string text)
    {
        var (buyer, _, conversation) = await ConversationAsync();

        await AuthFlow.AssertCodeAsync(await SendAsync(buyer, conversation, text), HttpStatusCode.BadRequest, "chat.invalid_message");
    }

    [Fact]
    public async Task Send_TooLongOrInappropriate_IsRejectedAndNotSaved()
    {
        var (buyer, _, conversation) = await ConversationAsync();

        var tooLong = await SendAsync(buyer, conversation, new string('a', 1001));
        var rejected = await SendAsync(buyer, conversation, "Te vendo esto [[inapropiado]]");

        await AuthFlow.AssertCodeAsync(tooLong, HttpStatusCode.BadRequest, "chat.invalid_message");
        await AuthFlow.AssertCodeAsync(rejected, HttpStatusCode.UnprocessableEntity, "moderation.text_rejected");
        var page = await GetJsonAsync(buyer.Client, $"/api/v1/conversations/{conversation}/messages");
        Assert.Empty(page.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Messages_ArriveInRealTime_ToBothParticipants_AndNotToOthers()
    {
        var (buyer, seller, conversation) = await ConversationAsync();
        var outsider = await MemberAsync();
        var toSeller = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var toBuyer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var toOutsider = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sellerHub = Hub(seller);
        await using var buyerHub = Hub(buyer);
        await using var outsiderHub = Hub(outsider);
        sellerHub.On<JsonElement>("messageCreated", m => toSeller.TrySetResult(m));
        buyerHub.On<JsonElement>("messageCreated", m => toBuyer.TrySetResult(m));
        outsiderHub.On<JsonElement>("messageCreated", m => toOutsider.TrySetResult(m));
        await sellerHub.StartAsync(Ct);
        await buyerHub.StartAsync(Ct);
        await outsiderHub.StartAsync(Ct);

        Assert.Equal(HttpStatusCode.Created, (await SendAsync(buyer, conversation, "¿Sigue disponible?")).StatusCode);

        var received = await toSeller.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await toBuyer.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal("¿Sigue disponible?", received.GetProperty("text").GetString());
        Assert.Equal(conversation, received.GetProperty("conversationId").GetString());
        Assert.Equal(buyer.Id, received.GetProperty("senderId").GetString());
        Assert.False(toOutsider.Task.IsCompleted);
    }

    [Fact]
    public async Task DeletingMyAccount_RemovesMyConversations_ForBothSides()
    {
        var (buyer, seller, conversation) = await ConversationAsync();
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(seller, conversation, "Claro, sigue disponible.")).StatusCode);

        var deleted = await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/account/delete", UriKind.Relative), new { password = AuthFlow.Password }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var afterwards = await seller.Client.GetAsync(new Uri($"/api/v1/conversations/{conversation}", UriKind.Relative), Ct);
        await AuthFlow.AssertCodeAsync(afterwards, HttpStatusCode.NotFound, "chat.conversation_not_found");
    }

    [Fact]
    public async Task Sending_IsRateLimitedPerUser()
    {
        var (buyer, seller, conversation) = await ConversationAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 31; i++)
        {
            statuses.Add((await SendAsync(buyer, conversation, $"Mensaje {i}")).StatusCode);
        }

        Assert.Equal(30, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        // El limite es por persona: el vendedor sigue escribiendo.
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(seller, conversation, "Aquí sigo")).StatusCode);
    }
}
