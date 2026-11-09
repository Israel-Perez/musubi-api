using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Listings;

/// <summary>
/// HU-07: editar, pausar, reactivar y eliminar una publicacion propia (maqueta <c>Mis-productos</c>) y que la
/// vitrina del visitante (cache de salida, 02-02) lo refleje en la siguiente consulta.
/// </summary>
public sealed class MyListingsFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Cuenta activa con sesion: cliente con el access token.</summary>
    private async Task<HttpClient> SignedInClientAsync()
    {
        // El JWT se emite con el reloj de la fabrica y JwtBearer lo valida con la hora real.
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var email = await flow.RegisterActiveAsync().ConfigureAwait(false);
        var login = await AuthFlow.JsonAsync(await flow.LoginAsync(email).ConfigureAwait(false)).ConfigureAwait(false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return client;
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Olive);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Publica un producto con una foto y devuelve su id y la llave de la foto.</summary>
    private static async Task<(string Id, string Photo)> PublishAsync(HttpClient seller)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString()!;
        var published = await seller.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new
            {
                title = "Calculadora científica Casio fx-991ES Plus",
                description = "La usé un semestre para Cálculo y Física.",
                category = "calculadoras",
                priceCents = 45_000,
                condition = "used",
                conditionDetail = "likeNew",
                stock = 1,
                photos = new[] { key },
            },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return ((await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!, key);
    }

    private static object Edit(
        string title = "Calculadora científica Casio fx-991ES Plus",
        string? description = "La usé un semestre para Cálculo y Física.",
        string category = "calculadoras",
        long priceCents = 45_000,
        string condition = "used",
        string? detail = "likeNew") => new { title, description, category, priceCents, condition, conditionDetail = detail };

    private static Task<HttpResponseMessage> EditAsync(HttpClient client, string id, object body) =>
        client.PutAsJsonAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative), body, Ct);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path) =>
        client.PostAsync(new Uri(path, UriKind.Relative), null, Ct);

    /// <summary>La publicacion en la vitrina anonima (sale de la cache de salida si no se borro), o <c>null</c>.</summary>
    private async Task<JsonElement?> InShowcaseAsync(string id)
    {
        using var anonymous = factory.CreateClient();
        var page = await AuthFlow.JsonAsync(await anonymous.GetAsync(new Uri("/api/v1/products?limit=50", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("id").GetString() == id)
            {
                return item;
            }
        }

        return null;
    }

    private static async Task<JsonElement?> InMineAsync(HttpClient seller, string id)
    {
        var page = await AuthFlow.JsonAsync(await seller.GetAsync(new Uri("/api/v1/products/mine", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("id").GetString() == id)
            {
                return item;
            }
        }

        return null;
    }

    [Fact]
    public async Task Edit_ShowsUpInMine_AndInTheVisitorShowcase()
    {
        using var seller = await SignedInClientAsync();
        var (id, _) = await PublishAsync(seller);
        // La vitrina anonima queda en la cache de salida con el nombre viejo.
        Assert.NotNull(await InShowcaseAsync(id));

        var response = await EditAsync(seller, id, Edit(title: "Calculadora Casio fx-570", category: "laboratorio", priceCents: 39_900, condition: "new", detail: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = await AuthFlow.JsonAsync(response);
        Assert.Equal("Calculadora Casio fx-570", edited.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, edited.GetProperty("conditionDetail").ValueKind);
        Assert.Equal("active", edited.GetProperty("status").GetString());
        Assert.Equal(1, edited.GetProperty("stock").GetInt32());
        Assert.Equal("Calculadora Casio fx-570", (await InMineAsync(seller, id))!.Value.GetProperty("title").GetString());
        var visible = await InShowcaseAsync(id);
        Assert.Equal("Calculadora Casio fx-570", visible!.Value.GetProperty("title").GetString());
        Assert.Equal(39_900, visible.Value.GetProperty("priceCents").GetInt64());
    }

    [Fact]
    public async Task Edit_ExplainsWhatIsWrong()
    {
        using var seller = await SignedInClientAsync();
        var (id, _) = await PublishAsync(seller);

        await AuthFlow.AssertCodeAsync(await EditAsync(seller, id, Edit(title: "ab")), HttpStatusCode.BadRequest, "listings.invalid_title");
        await AuthFlow.AssertCodeAsync(await EditAsync(seller, id, Edit(priceCents: 0)), HttpStatusCode.BadRequest, "listings.invalid_price");
        await AuthFlow.AssertCodeAsync(await EditAsync(seller, id, Edit(category: "autos")), HttpStatusCode.BadRequest, "listings.invalid_category");
        await AuthFlow.AssertCodeAsync(await EditAsync(seller, id, Edit(detail: null)), HttpStatusCode.BadRequest, "listings.invalid_condition");
        await AuthFlow.AssertCodeAsync(await EditAsync(seller, "6716a0000000000000000001", Edit()), HttpStatusCode.NotFound, "listings.product_not_found");
    }

    [Fact]
    public async Task Edit_WithInappropriateText_Is422_AndTheProductDoesNotChange()
    {
        using var seller = await SignedInClientAsync();
        var (id, _) = await PublishAsync(seller);

        var response = await EditAsync(seller, id, Edit(description: "Contacto [[inapropiado]]"));

        await AuthFlow.AssertCodeAsync(response, (HttpStatusCode)422, "moderation.text_rejected");
        Assert.Equal("La usé un semestre para Cálculo y Física.", (await InMineAsync(seller, id))!.Value.GetProperty("description").GetString());
    }

    [Fact]
    public async Task PauseHidesItFromTheShowcase_AndResumeBringsItBack()
    {
        using var seller = await SignedInClientAsync();
        var (id, _) = await PublishAsync(seller);
        Assert.NotNull(await InShowcaseAsync(id));

        var paused = await PostAsync(seller, $"/api/v1/products/{id}/pause");

        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        Assert.Equal("hidden", (await AuthFlow.JsonAsync(paused)).GetProperty("status").GetString());
        Assert.Null(await InShowcaseAsync(id));
        Assert.Equal("hidden", (await InMineAsync(seller, id))!.Value.GetProperty("status").GetString());

        var resumed = await PostAsync(seller, $"/api/v1/products/{id}/resume");

        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.Equal("active", (await AuthFlow.JsonAsync(resumed)).GetProperty("status").GetString());
        Assert.NotNull(await InShowcaseAsync(id));
    }

    [Fact]
    public async Task Delete_KeepsTheCoverThatALinkedOfferStillShows()
    {
        using var seller = await SignedInClientAsync();
        using var requester = await SignedInClientAsync();
        var (id, photo) = await PublishAsync(seller);
        var wanted = await requester.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Calculadora científica", category = "calculadoras", budgetCents = 50_000, quantity = 1 },
            Ct);
        var requestId = (await AuthFlow.JsonAsync(wanted)).GetProperty("id").GetString();
        var offer = await seller.PutAsJsonAsync(new Uri($"/api/v1/wanted/{requestId}/offers/mine", UriKind.Relative), new { productId = id }, Ct);
        Assert.Equal(HttpStatusCode.Created, offer.StatusCode);

        var deleted = await seller.DeleteAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative), Ct);

        // HU-16 copio la portada en la oferta: la foto se queda para que la oferta la siga mostrando.
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(new Uri("/" + photo, UriKind.Relative), Ct)).StatusCode);
        var mine = await AuthFlow.JsonAsync(await seller.GetAsync(new Uri($"/api/v1/wanted/{requestId}/offers/mine", UriKind.Relative), Ct));
        Assert.Equal("/" + photo, mine.GetProperty("photo").GetString());
    }

    [Fact]
    public async Task Delete_RemovesItEverywhere_AndTheChatShowsItAsUnavailable()
    {
        using var seller = await SignedInClientAsync();
        using var buyer = await SignedInClientAsync();
        var (id, photo) = await PublishAsync(seller);
        var conversation = await AuthFlow.JsonAsync(await buyer.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId = id }, Ct));
        var conversationId = conversation.GetProperty("id").GetString();
        Assert.NotNull(await InShowcaseAsync(id));

        var deleted = await seller.DeleteAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Null(await InShowcaseAsync(id));
        Assert.Null(await InMineAsync(seller, id));
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(new Uri("/" + photo, UriKind.Relative), Ct)).StatusCode);
        var context = (await AuthFlow.JsonAsync(await buyer.GetAsync(new Uri($"/api/v1/conversations/{conversationId}", UriKind.Relative), Ct))).GetProperty("context");
        Assert.False(context.GetProperty("available").GetBoolean());
        Assert.Equal("Calculadora científica Casio fx-991ES Plus", context.GetProperty("title").GetString());
        await AuthFlow.AssertCodeAsync(await seller.DeleteAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative), Ct), HttpStatusCode.NotFound, "listings.product_not_found");
    }
}
