using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Listings;

/// <summary>
/// HU-06 / HU-50 / HU-48 fase 2: subir fotos, publicar (maqueta <c>Vender-1…4</c>) y ver el producto en la
/// vitrina publica y en "Mis productos".
/// </summary>
public sealed class ListingFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Png(int width = 400, int height = 300)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Teal);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Cuenta activa con sesion: cliente con el access token.</summary>
    private async Task<HttpClient> SignedInClientAsync()
    {
        // El JWT se emite con el reloj de la fabrica, pero JwtBearer lo valida con la hora real:
        // se alinean para que el access token no nazca vencido.
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

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string fileName = "foto.png")
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", fileName);
        return await client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
    }

    private static async Task<string> UploadKeyAsync(HttpClient client)
    {
        var response = await UploadAsync(client, Png()).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await AuthFlow.JsonAsync(response).ConfigureAwait(false)).GetProperty("key").GetString()!;
    }

    private static object Product(IEnumerable<string> photos, string category = "calculadoras", long priceCents = 45_000, string condition = "used", string? detail = "likeNew", int stock = 1) => new
    {
        title = "Calculadora científica Casio fx-991ES Plus",
        description = "La usé un semestre para Cálculo y Física.",
        category,
        priceCents,
        condition,
        conditionDetail = detail,
        stock,
        photos,
    };

    private static Task<HttpResponseMessage> PublishAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync(new Uri("/api/v1/products", UriKind.Relative), body, Ct);

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    [Fact]
    public async Task UploadAndPublish_ShowsUpInTheShowcaseAndInMine()
    {
        using var seller = await SignedInClientAsync();
        var cover = await UploadKeyAsync(seller);
        var second = await UploadKeyAsync(seller);

        var published = await PublishAsync(seller, Product([cover, second]));

        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var product = await AuthFlow.JsonAsync(published);
        var id = product.GetProperty("id").GetString();
        Assert.Equal("active", product.GetProperty("status").GetString());
        Assert.Equal(45_000, product.GetProperty("priceCents").GetInt64());
        Assert.Equal("Ana P.", product.GetProperty("seller").GetProperty("name").GetString());
        Assert.Equal("/" + cover, product.GetProperty("photos")[0].GetString());

        // La foto se sirve en /media/* (en la nube lo hace CloudFront desde S3).
        using var anonymous = factory.CreateClient();
        var image = await anonymous.GetAsync(new Uri("/" + cover, UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/webp", image.Content.Headers.ContentType?.MediaType);

        // Vitrina publica: el visitante no ve al vendedor (RNF-05); con sesion si.
        var showcase = await GetJsonAsync(anonymous, "/api/v1/products");
        var mine = await GetJsonAsync(seller, "/api/v1/products/mine");
        var asMember = await GetJsonAsync(seller, "/api/v1/products");
        Assert.Equal(id, showcase.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, showcase.GetProperty("items")[0].GetProperty("seller").ValueKind);
        Assert.Equal("Ana P.", asMember.GetProperty("items")[0].GetProperty("seller").GetProperty("name").GetString());
        Assert.Contains(mine.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetString() == id);
    }

    [Fact]
    public async Task Publish_ExplainsWhatIsMissing()
    {
        using var seller = await SignedInClientAsync();
        var photo = await UploadKeyAsync(seller);

        await AuthFlow.AssertCodeAsync(await PublishAsync(seller, Product([])), HttpStatusCode.BadRequest, "listings.invalid_photos");
        await AuthFlow.AssertCodeAsync(await PublishAsync(seller, Product([photo], priceCents: 0)), HttpStatusCode.BadRequest, "listings.invalid_price");
        await AuthFlow.AssertCodeAsync(await PublishAsync(seller, Product([photo], category: "autos")), HttpStatusCode.BadRequest, "listings.invalid_category");
        await AuthFlow.AssertCodeAsync(await PublishAsync(seller, Product([photo], detail: null)), HttpStatusCode.BadRequest, "listings.invalid_condition");
        await AuthFlow.AssertCodeAsync(await PublishAsync(seller, Product([photo], stock: 11)), HttpStatusCode.BadRequest, "listings.invalid_stock");
        Assert.Equal(HttpStatusCode.Created, (await PublishAsync(seller, Product([photo], condition: "new", detail: null))).StatusCode);
    }

    [Fact]
    public async Task Publish_WithSomeoneElsesPhoto_IsRejected()
    {
        using var owner = await SignedInClientAsync();
        using var other = await SignedInClientAsync();
        var ownersPhoto = await UploadKeyAsync(owner);

        await AuthFlow.AssertCodeAsync(await PublishAsync(other, Product([ownersPhoto])), HttpStatusCode.BadRequest, "listings.invalid_photos");
    }

    [Fact]
    public async Task Upload_OfSomethingThatIsNotAPhoto_IsRejected()
    {
        using var seller = await SignedInClientAsync();

        var response = await UploadAsync(seller, "no soy una foto"u8.ToArray(), "nota.txt");

        await AuthFlow.AssertCodeAsync(response, HttpStatusCode.UnsupportedMediaType, "listings.photo_unsupported");
    }

    [Fact]
    public async Task Showcase_FiltersByCategoryAndPagesWithACursor()
    {
        using var seller = await SignedInClientAsync();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await PublishAsync(seller, Product([await UploadKeyAsync(seller)], category: "laboratorio"))).StatusCode);
        }

        var first = await GetJsonAsync(seller, "/api/v1/products?category=laboratorio&limit=2");
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await GetJsonAsync(seller, $"/api/v1/products?category=laboratorio&limit=2&cursor={cursor}");

        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("total").GetInt64() >= 3);
        Assert.All(first.GetProperty("items").EnumerateArray(), p => Assert.Equal("laboratorio", p.GetProperty("category").GetString()));
        Assert.NotEqual(first.GetProperty("items")[0].GetProperty("id").GetString(), second.GetProperty("items")[0].GetProperty("id").GetString());
        await AuthFlow.AssertCodeAsync(
            await seller.GetAsync(new Uri("/api/v1/products?cursor=no-es-un-id", UriKind.Relative), Ct),
            HttpStatusCode.BadRequest,
            "listings.invalid_cursor");
    }

    [Fact]
    public async Task Categories_AreTheMockupOnes_AndAnUnknownCampusIs404()
    {
        using var anonymous = factory.CreateClient();

        var categories = await GetJsonAsync(anonymous, "/api/v1/categories");
        var unknown = await anonymous.GetAsync(new Uri("/api/v1/categories?campus=otra", UriKind.Relative), Ct);

        Assert.Equal(6, categories.GetArrayLength());
        Assert.Equal("calculadoras", categories[0].GetProperty("slug").GetString());
        await AuthFlow.AssertCodeAsync(unknown, HttpStatusCode.NotFound, "listings.unknown_campus");
    }

    [Fact]
    public async Task PriceRange_NeedsThreeSimilarProducts()
    {
        using var seller = await SignedInClientAsync();
        var path = new Uri("/api/v1/products/price-range?category=dibujo", UriKind.Relative);
        Assert.Equal(HttpStatusCode.NoContent, (await seller.GetAsync(path, Ct)).StatusCode);

        foreach (var price in new long[] { 18_000, 25_000, 32_000 })
        {
            await PublishAsync(seller, Product([await UploadKeyAsync(seller)], category: "dibujo", priceCents: price));
        }

        var range = await AuthFlow.JsonAsync(await seller.GetAsync(path, Ct));
        // Percentiles 25 y 75 interpolados entre $180, $250 y $320.
        Assert.Equal(21_500, range.GetProperty("lowCents").GetInt64());
        Assert.Equal(28_500, range.GetProperty("highCents").GetInt64());
    }
}
