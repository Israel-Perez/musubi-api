using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Profiles;

/// <summary>
/// HU-64 de punta a punta: Diego vende y contesta en el chat a tres compradores; su perfil publico dice "Responde en
/// ~N" con la mediana de lo que tardo. Sin suficientes respuestas no hay insignia, y "Vendedor confiable" pide 4.5
/// estrellas con 5 ventas.
/// </summary>
public sealed class SellerBadgesFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
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
            new { title = "Calculadora Casio fx-991ES Plus", description = "La usé un semestre.", category = "calculadoras", priceCents = 45_000, condition = "used", conditionDetail = "good", stock = 3, photos = new[] { key } },
            Ct).ConfigureAwait(false);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static async Task SendAsync(Member who, string conversationId, string text)
    {
        var sent = await who.Client.PostAsJsonAsync(new Uri($"/api/v1/conversations/{conversationId}/messages", UriKind.Relative), new { text }, Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
    }

    // La pregunta de quien compra y, despues de unos minutos, la respuesta de quien vende.
    private async Task ConversationAsync(Member buyer, Member seller, string productId, int minutes)
    {
        var started = await AuthFlow.JsonAsync(await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId }, Ct).ConfigureAwait(false)).ConfigureAwait(false);
        var id = started.GetProperty("id").GetString()!;
        await SendAsync(buyer, id, "¿Sigue disponible?").ConfigureAwait(false);
        factory.Clock.Advance(TimeSpan.FromMinutes(minutes));
        await SendAsync(seller, id, "Sí, aquí la tengo").ConfigureAwait(false);
    }

    private static async Task<JsonElement> StatsAsync(Member viewer, Member seller) =>
        (await AuthFlow.JsonAsync(await viewer.Client.GetAsync(new Uri($"/api/v1/profiles/{seller.Id}", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false))
            .GetProperty("stats");

    [Fact]
    public async Task ThreeReplies_ShowTheMedianResponseTime_ButNotTrustedYet()
    {
        // Una conversacion por publicacion: Luis pregunta por tres de Diego (pocas cuentas: el registro tiene limite).
        var diego = await MemberAsync();
        var luis = await MemberAsync();
        var products = new[] { await PublishAsync(diego), await PublishAsync(diego), await PublishAsync(diego) };

        // Pocos minutos: el reloj de la prueba tambien vence los access tokens.
        await ConversationAsync(luis, diego, products[0], 1);
        await ConversationAsync(luis, diego, products[1], 4);
        var before = await StatsAsync(luis, diego);
        await ConversationAsync(luis, diego, products[2], 2);
        var after = await StatsAsync(luis, diego);

        Assert.Equal(JsonValueKind.Null, before.GetProperty("responseMinutes").ValueKind);
        Assert.Equal(2, after.GetProperty("responseMinutes").GetInt32());
        Assert.False(after.GetProperty("trusted").GetBoolean());
    }
}
