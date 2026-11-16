using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Listings;

/// <summary>
/// HU-51 de punta a punta (maqueta <c>Producto</c>): Ana compara una calculadora con las del campus ("Compara antes de
/// comprar"), pide "Avísame" en una agotada de Diego y, cuando Diego la repone, le llega "¡Volvió lo que esperabas!"
/// que abre la publicacion. El aviso se usa una vez.
/// </summary>
public sealed class CompareAndStockAlertTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
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

    // Una categoria propia por prueba: otras pruebas de la clase tambien publican calculadoras.
    private static async Task<string> PublishAsync(Member seller, string title, long priceCents, string category = "laboratorio")
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString();
        var published = await seller.Client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title, description = "La usé un semestre.", category, priceCents, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { key } },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static async Task SetStockAsync(Member seller, string productId, int stock)
    {
        var changed = await seller.Client.PutAsJsonAsync(new Uri($"/api/v1/products/{productId}/stock", UriKind.Relative), new { stock }, Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
    }

    private static async Task<JsonElement[]> NoticesAsync(Member member, string type) =>
        [.. (await AuthFlow.JsonAsync(await member.Client.GetAsync(new Uri("/api/v1/notifications?category=matches", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false))
            .GetProperty("items").EnumerateArray().Where(n => n.GetProperty("type").GetString() == type)];

    // La revision corre en segundo plano: se espera hasta 5 s a que llegue el aviso.
    private static async Task<JsonElement> NoticeAsync(Member member, string type)
    {
        for (var i = 0; i < 50; i++)
        {
            if ((await NoticesAsync(member, type).ConfigureAwait(false)) is [var first, ..])
            {
                return first;
            }

            await Task.Delay(100, Ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"No llego el aviso {type}");
    }

    [Fact]
    public async Task Compare_ShowsTheSameModelFirst_WithTheSeller_AndNeverMine()
    {
        var ana = await MemberAsync();
        var diego = await MemberAsync();
        var viewed = await PublishAsync(diego, "Calculadora Casio fx-991ES Plus", 45_000, "calculadoras");
        var other = await PublishAsync(diego, "Calculadora Sharp EL-W506", 44_000, "calculadoras");
        var same = await PublishAsync(diego, "Casio fx-991ES Plus con tapa", 60_000, "calculadoras");
        var mine = await PublishAsync(ana, "Calculadora Casio fx-991ES de Ana", 45_000, "calculadoras");

        var response = await ana.Client.GetAsync(new Uri($"/api/v1/products/{viewed}/compare", UriKind.Relative), Ct);
        var items = (await AuthFlow.JsonAsync(response)).GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ids = items.Select(i => i.GetProperty("product").GetProperty("id").GetString()).ToList();
        Assert.True(ids.IndexOf(same) < ids.IndexOf(other));
        Assert.DoesNotContain(mine, ids);
        Assert.DoesNotContain(viewed, ids);
        var first = items[ids.IndexOf(same)];
        Assert.True(first.GetProperty("sameModel").GetBoolean());
        Assert.Equal(diego.Id, first.GetProperty("seller").GetProperty("id").GetString());
        Assert.Equal(0, first.GetProperty("seller").GetProperty("ratingCount").GetInt32());
    }

    [Fact]
    public async Task StockAlert_OnlyOnASoldOutProductThatIsNotMine()
    {
        var ana = await MemberAsync();
        var diego = await MemberAsync();
        var bata = await PublishAsync(diego, "Bata de laboratorio talla M", 28_000);

        var available = await ana.Client.PutAsync(new Uri($"/api/v1/products/{bata}/stock-alert", UriKind.Relative), null, Ct);
        await SetStockAsync(diego, bata, 0);
        var own = await diego.Client.PutAsync(new Uri($"/api/v1/products/{bata}/stock-alert", UriKind.Relative), null, Ct);
        var before = await AuthFlow.JsonAsync(await ana.Client.GetAsync(new Uri($"/api/v1/products/{bata}/stock-alert", UriKind.Relative), Ct));
        var asked = await ana.Client.PutAsync(new Uri($"/api/v1/products/{bata}/stock-alert", UriKind.Relative), null, Ct);
        var removed = await ana.Client.DeleteAsync(new Uri($"/api/v1/products/{bata}/stock-alert", UriKind.Relative), Ct);
        var after = await AuthFlow.JsonAsync(await ana.Client.GetAsync(new Uri($"/api/v1/products/{bata}/stock-alert", UriKind.Relative), Ct));

        Assert.Equal((HttpStatusCode.Conflict, "listings.product_available"), (available.StatusCode, (await AuthFlow.JsonAsync(available)).GetProperty("code").GetString()));
        Assert.Equal((HttpStatusCode.Conflict, "listings.own_product"), (own.StatusCode, (await AuthFlow.JsonAsync(own)).GetProperty("code").GetString()));
        Assert.False(before.GetProperty("active").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.True((await AuthFlow.JsonAsync(asked)).GetProperty("active").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.False(after.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task Restock_TellsWhoAsked_OpensTheProduct_AndOnlyOnce()
    {
        var ana = await MemberAsync();
        var diego = await MemberAsync();
        var matraces = await PublishAsync(diego, "Kit de matraces y tubos", 35_000);
        await SetStockAsync(diego, matraces, 0);
        var asked = await ana.Client.PutAsync(new Uri($"/api/v1/products/{matraces}/stock-alert", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);

        await SetStockAsync(diego, matraces, 2);
        var notice = await NoticeAsync(ana, "back_in_stock");

        Assert.Equal("¡Volvió lo que esperabas!", notice.GetProperty("title").GetString());
        Assert.Contains("repuso «Kit de matraces y tubos». Quedan 2.", notice.GetProperty("body").GetString(), StringComparison.Ordinal);
        Assert.Equal(("product", matraces), (notice.GetProperty("target").GetProperty("type").GetString(), notice.GetProperty("target").GetProperty("id").GetString()));
        Assert.False((await AuthFlow.JsonAsync(await ana.Client.GetAsync(new Uri($"/api/v1/products/{matraces}/stock-alert", UriKind.Relative), Ct))).GetProperty("active").GetBoolean());

        // Se agota y vuelve otra vez: el aviso ya se uso.
        await SetStockAsync(diego, matraces, 0);
        await SetStockAsync(diego, matraces, 1);
        await Task.Delay(500, Ct);
        Assert.Single(await NoticesAsync(ana, "back_in_stock"));
        // Quien vende no recibe aviso de lo suyo.
        Assert.Empty(await NoticesAsync(diego, "back_in_stock"));
    }
}
