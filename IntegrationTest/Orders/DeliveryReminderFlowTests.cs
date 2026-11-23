using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Orders;
using SkiaSharp;

namespace IntegrationTest.Orders;

/// <summary>
/// HU-62 por HTTP: con un pedido confirmado para manana, "Ya llegue" todavia no se puede; una hora antes los dos reciben
/// el recordatorio en su centro de avisos (una sola vez); "Ya llegue" le avisa a quien vende (<c>order_arrived</c>),
/// deja el aviso en el chat y no se repite. Tiene su propia fabrica porque adelanta el reloj un dia.
/// </summary>
public sealed class DeliveryReminderFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Member(HttpClient Client, string Id);

    [Fact]
    public async Task OneHourBefore_BothAreReminded_AndArrivedTellsTheSellerInTheChat()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var luis = await MemberAsync();
        var product = await PublishAsync(diego);
        var (order, conversation, deliveryAt) = await PaidOrderAsync(ana, product);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(diego, $"/api/v1/orders/{order}/accept")).StatusCode);

        var early = await PostAsync(ana, $"/api/v1/orders/{order}/arrived");
        var jump = deliveryAt - factory.Clock.GetUtcNow().UtcDateTime - TimeSpan.FromMinutes(50);
        factory.Clock.Advance(jump);
        try
        {
            var job = factory.Services.GetRequiredService<DeliveryReminderJob>();
            await job.RunAsync(factory.Clock.GetUtcNow().UtcDateTime, Ct);
            await job.RunAsync(factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(5), Ct);
            var arrived = await PostAsync(ana, $"/api/v1/orders/{order}/arrived");
            var again = await PostAsync(ana, $"/api/v1/orders/{order}/arrived");
            var stranger = await PostAsync(luis, $"/api/v1/orders/{order}/arrived");
            var messages = await AuthFlow.JsonAsync(await diego.Client.GetAsync(new Uri($"/api/v1/conversations/{conversation}/messages", UriKind.Relative), Ct));

            await AuthFlow.AssertCodeAsync(early, HttpStatusCode.Conflict, "orders.arrival_too_early");
            Assert.Equal(1, (await NoticesAsync(ana)).Count(t => t == "delivery_reminder"));
            Assert.Equal(1, (await NoticesAsync(diego)).Count(t => t == "delivery_reminder"));
            Assert.Equal(HttpStatusCode.OK, arrived.StatusCode);
            Assert.Equal(JsonValueKind.String, (await AuthFlow.JsonAsync(arrived)).GetProperty("handover").GetProperty("buyerArrivedAt").ValueKind);
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            await AuthFlow.AssertCodeAsync(stranger, HttpStatusCode.NotFound, "orders.not_found");
            Assert.Equal(1, (await NoticesAsync(diego)).Count(t => t == "order_arrived"));
            Assert.DoesNotContain("order_arrived", await NoticesAsync(ana));
            var notice = messages.GetProperty("items").EnumerateArray().First();
            Assert.Equal(("notice", ana.Id), (notice.GetProperty("kind").GetString(), notice.GetProperty("senderId").GetString()));
            Assert.EndsWith("ya llegó a Biblioteca", notice.GetProperty("text").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            // Los demas tokens de esta fabrica se emiten con la hora real.
            factory.Clock.Advance(-jump);
        }
    }

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

    // Manana a la 1:00 p. m. del campus.
    private DateTime Tomorrow()
    {
        var campusToday = (factory.Clock.GetUtcNow() - TimeSpan.FromHours(6)).Date;
        return DateTime.SpecifyKind(campusToday.AddDays(1).AddHours(19), DateTimeKind.Utc);
    }

    private static async Task<string> PublishAsync(Member seller)
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Teal);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(png.ToArray());
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
                conditionDetail = "likeNew",
                stock = 1,
                photos = new[] { key },
            },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    // Ana le escribe a Diego, crea el pedido para manana en la Biblioteca y elige pagar en efectivo.
    private async Task<(string Order, string Conversation, DateTime DeliveryAt)> PaidOrderAsync(Member buyer, string productId)
    {
        var started = await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId }, Ct).ConfigureAwait(false);
        var conversationId = (await AuthFlow.JsonAsync(started).ConfigureAwait(false)).GetProperty("id").GetString()!;
        var deliveryAt = Tomorrow();
        var created = await buyer.Client.PostAsJsonAsync(
            new Uri("/api/v1/orders", UriKind.Relative),
            new { conversationId, quantity = 1, meetingPoint = "biblioteca", deliveryAt },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await AuthFlow.JsonAsync(created).ConfigureAwait(false)).GetProperty("id").GetString()!;
        var paid = await buyer.Client.PutAsJsonAsync(new Uri($"/api/v1/orders/{id}/payment", UriKind.Relative), new { method = "cash" }, Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, paid.StatusCode);
        return (id, conversationId, deliveryAt);
    }

    private static Task<HttpResponseMessage> PostAsync(Member member, string path) =>
        member.Client.PostAsync(new Uri(path, UriKind.Relative), null, Ct);

    private static async Task<List<string>> NoticesAsync(Member member)
    {
        var response = await member.Client.GetAsync(new Uri("/api/v1/notifications?category=orders", UriKind.Relative), Ct).ConfigureAwait(false);
        var body = await AuthFlow.JsonAsync(response).ConfigureAwait(false);
        return [.. body.GetProperty("items").EnumerateArray().Select(n => n.GetProperty("type").GetString()!)];
    }
}
