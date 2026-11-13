using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Matching;

/// <summary>
/// HU-19 y HU-30 de punta a punta: Diego vende una calculadora; Ana publica una solicitud compatible y a Diego le
/// llega "Hay una solicitud compatible con tu producto" (abre la solicitud del tablero). Diego ofrece y a Ana le llega
/// "Alguien tiene lo que buscas" (abre su solicitud). Ana la cierra y a Diego le llega "Esta solicitud ya no está
/// activa".
/// </summary>
public sealed class WantedMatchFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
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

    private static async Task<string> PublishAsync(Member seller, string title, long priceCents)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString();
        var published = await seller.Client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title, description = "La usé un semestre.", category = "calculadoras", priceCents, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { key } },
            Ct).ConfigureAwait(false);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static async Task<string> WantAsync(Member buyer, string title, long budgetCents)
    {
        var created = await buyer.Client.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title, category = "calculadoras", budgetCents, quantity = 1 },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await AuthFlow.JsonAsync(created).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement[]> NoticesAsync(Member member, string category) =>
        [.. (await AuthFlow.JsonAsync(await member.Client.GetAsync(new Uri($"/api/v1/notifications?category={category}", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false))
            .GetProperty("items").EnumerateArray()];

    // La busqueda corre en segundo plano: se espera hasta 5 s a que llegue el aviso.
    private static async Task<JsonElement> NoticeAsync(Member member, string category, string type)
    {
        for (var i = 0; i < 50; i++)
        {
            foreach (var item in await NoticesAsync(member, category).ConfigureAwait(false))
            {
                if (item.GetProperty("type").GetString() == type)
                {
                    return item;
                }
            }

            await Task.Delay(100, Ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"No llego el aviso {type}");
    }

    [Fact]
    public async Task ACompatibleRequest_TellsTheSeller_TheOffer_TellsTheBuyer_AndClosing_TellsWhoOffered()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        var productId = await PublishAsync(diego, "Calculadora Casio fx-991ES Plus", 50_000);

        var requestId = await WantAsync(ana, "Calculadora Casio fx-991ES", 60_000);
        var match = await NoticeAsync(diego, "matches", "wanted_match");

        Assert.Equal("Hay una solicitud compatible con tu producto", match.GetProperty("title").GetString());
        Assert.Contains("«Calculadora Casio fx-991ES Plus» coincide.", match.GetProperty("body").GetString(), StringComparison.Ordinal);
        Assert.Equal(("wanted_board", requestId), (match.GetProperty("target").GetProperty("type").GetString(), match.GetProperty("target").GetProperty("id").GetString()));
        // RN-31: quien busca no recibe aviso de su propia solicitud.
        Assert.Empty(await NoticesAsync(ana, "matches"));

        var offered = await diego.Client.PutAsJsonAsync(new Uri($"/api/v1/wanted/{requestId}/offers/mine", UriKind.Relative), new { productId }, Ct);
        var received = await NoticeAsync(ana, "requests", "offer_received");

        Assert.Equal(HttpStatusCode.Created, offered.StatusCode);
        Assert.Equal(("wanted", requestId), (received.GetProperty("target").GetProperty("type").GetString(), received.GetProperty("target").GetProperty("id").GetString()));

        var closed = await ana.Client.PostAsync(new Uri($"/api/v1/wanted/{requestId}/close", UriKind.Relative), null, Ct);
        var gone = await NoticeAsync(diego, "requests", "offer_closed");

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal("Esta solicitud ya no está activa", gone.GetProperty("title").GetString());
        Assert.Equal("none", gone.GetProperty("target").GetProperty("type").GetString());
    }

    [Fact]
    public async Task AnIncompatibleRequest_TellsNobody()
    {
        var diego = await MemberAsync();
        var ana = await MemberAsync();
        await PublishAsync(diego, "Calculadora graficadora TI-84 Plus", 165_000);

        // Pasa el presupuesto en mas de 20 %.
        await WantAsync(ana, "Calculadora graficadora TI-84 Plus", 100_000);
        // Una segunda solicitud compatible marca cuando la cola ya reviso la primera.
        var second = await WantAsync(ana, "Calculadora graficadora", 165_000);
        var match = await NoticeAsync(diego, "matches", "wanted_match");

        Assert.Equal(second, match.GetProperty("target").GetProperty("id").GetString());
        Assert.Single(await NoticesAsync(diego, "matches"));
    }
}
