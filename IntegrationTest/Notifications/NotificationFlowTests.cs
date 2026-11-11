using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Notifications;
using SkiaSharp;

namespace IntegrationTest.Notifications;

/// <summary>
/// HU-31 de punta a punta (push en memoria de Development): Diego registra su telefono, Ana le escribe y el push le
/// llega; Ana crea el pedido y Diego lo ve en su centro de avisos, lo marca leido y al cerrar sesion quita el
/// dispositivo.
/// </summary>
public sealed class NotificationFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Member(HttpClient Client, string Id);

    private RecordingPushSender Pushes => factory.Services.GetRequiredService<RecordingPushSender>();

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

    private static async Task<string> ChatAsync(Member buyer, Member seller)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString();
        var published = await seller.Client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title = "Calculadora Casio fx-991ES Plus", description = "La usé un semestre.", category = "calculadoras", priceCents = 50_000, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { key } },
            Ct).ConfigureAwait(false);
        var productId = (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString();
        var started = await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId }, Ct).ConfigureAwait(false);
        return (await AuthFlow.JsonAsync(started).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    // El push sale en segundo plano: se espera hasta 5 s a que llegue.
    private async Task<(string Token, PushMessage Message)> PushToAsync(string token)
    {
        for (var i = 0; i < 50; i++)
        {
            foreach (var sent in Pushes.Sent)
            {
                if (sent.Token == token)
                {
                    return sent;
                }
            }

            await Task.Delay(100, Ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"No llego el push a {token}");
    }

    [Fact]
    public async Task AMessage_ReachesTheOtherPhone_AndANewOrder_IsInTheSellersNotifications()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var token = $"fcm-diego-{Guid.NewGuid():N}";
        var registered = await diego.Client.PutAsJsonAsync(new Uri("/api/v1/notifications/devices", UriKind.Relative), new { token }, Ct);
        var conversation = await ChatAsync(ana, diego);

        await ana.Client.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversation}/messages", UriKind.Relative), new { text = "¿Sigue disponible?" }, Ct);
        var push = await PushToAsync(token);

        Assert.Equal(HttpStatusCode.NoContent, registered.StatusCode);
        Assert.Equal("¿Sigue disponible?", push.Message.Body);
        Assert.Equal(("message", "conversation", conversation), (push.Message.Data["type"], push.Message.Data["targetType"], push.Message.Data["targetId"]));

        var tomorrow = DateTime.SpecifyKind((factory.Clock.GetUtcNow() - TimeSpan.FromHours(6)).Date.AddDays(1).AddHours(19), DateTimeKind.Utc);
        var created = await ana.Client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative), new { conversationId = conversation, quantity = 1, meetingPoint = "biblioteca", deliveryAt = tomorrow }, Ct);
        var orderId = (await AuthFlow.JsonAsync(created)).GetProperty("id").GetString();
        var page = await AuthFlow.JsonAsync(await diego.Client.GetAsync(new Uri("/api/v1/notifications?category=orders", UriKind.Relative), Ct));
        var item = page.GetProperty("items")[0];

        Assert.Equal(1, page.GetProperty("unread").GetInt64());
        Assert.Equal(("order_created", "Tienes un pedido nuevo", false), (item.GetProperty("type").GetString(), item.GetProperty("title").GetString(), item.GetProperty("read").GetBoolean()));
        Assert.Equal(("order", orderId), (item.GetProperty("target").GetProperty("type").GetString(), item.GetProperty("target").GetProperty("id").GetString()));
        Assert.Empty((await AuthFlow.JsonAsync(await ana.Client.GetAsync(new Uri("/api/v1/notifications", UriKind.Relative), Ct))).GetProperty("items").EnumerateArray());

        var read = await diego.Client.PostAsync(new Uri($"/api/v1/notifications/{item.GetProperty("id").GetString()}/read", UriKind.Relative), null, Ct);
        var after = await AuthFlow.JsonAsync(await diego.Client.GetAsync(new Uri("/api/v1/notifications", UriKind.Relative), Ct));
        var removed = await diego.Client.PostAsJsonAsync(new Uri("/api/v1/notifications/devices/remove", UriKind.Relative), new { token }, Ct);
        var readAll = await diego.Client.PostAsync(new Uri("/api/v1/notifications/read", UriKind.Relative), null, Ct);

        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        Assert.Equal(0, after.GetProperty("unread").GetInt64());
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, readAll.StatusCode);
    }

    [Fact]
    public async Task AnInvalidTokenOrCategory_IsRejected()
    {
        var ana = await MemberAsync();

        var token = await ana.Client.PutAsJsonAsync(new Uri("/api/v1/notifications/devices", UriKind.Relative), new { token = "" }, Ct);
        var category = await ana.Client.GetAsync(new Uri("/api/v1/notifications?category=spam", UriKind.Relative), Ct);

        await AuthFlow.AssertCodeAsync(token, HttpStatusCode.BadRequest, "notifications.invalid_token");
        await AuthFlow.AssertCodeAsync(category, HttpStatusCode.BadRequest, "notifications.invalid_category");
    }
}
