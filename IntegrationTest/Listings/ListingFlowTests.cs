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

    private static object Product(
        IEnumerable<string> photos,
        string category = "calculadoras",
        long priceCents = 45_000,
        string condition = "used",
        string? detail = "likeNew",
        int stock = 1,
        string? title = null,
        string? description = null) => new
    {
        title = title ?? "Calculadora científica Casio fx-991ES Plus",
        description = description ?? "La usé un semestre para Cálculo y Física.",
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

    private static async Task<string> PublishIdAsync(HttpClient client, object body)
    {
        var response = await PublishAsync(client, body).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await AuthFlow.JsonAsync(response).ConfigureAwait(false)).GetProperty("id").GetString()!;
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
    public async Task Detail_ShowsEverything_AndTheSellerOnlyWithASession()
    {
        using var seller = await SignedInClientAsync();
        var published = await PublishAsync(seller, Product([await UploadKeyAsync(seller), await UploadKeyAsync(seller)]));
        var id = (await AuthFlow.JsonAsync(published)).GetProperty("id").GetString();
        using var visitor = factory.CreateClient();
        using var buyer = await SignedInClientAsync();

        var anonymous = await GetJsonAsync(visitor, $"/api/v1/products/{id}");
        var member = await GetJsonAsync(buyer, $"/api/v1/products/{id}");

        Assert.Equal("Calculadora científica Casio fx-991ES Plus", anonymous.GetProperty("title").GetString());
        Assert.Equal("La usé un semestre para Cálculo y Física.", anonymous.GetProperty("description").GetString());
        Assert.Equal("Calculadoras", anonymous.GetProperty("category").GetProperty("name").GetString());
        Assert.Equal(2, anonymous.GetProperty("photos").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, anonymous.GetProperty("seller").ValueKind);
        Assert.Equal("Ana P.", member.GetProperty("seller").GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(member.GetProperty("seller").GetProperty("id").GetString()));
    }

    [Theory]
    [InlineData("6716a0000000000000000099")]
    [InlineData("no-es-un-id")]
    public async Task Detail_OfAnUnknownProduct_Is404(string id)
    {
        using var visitor = factory.CreateClient();

        var response = await visitor.GetAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative), Ct);

        await AuthFlow.AssertCodeAsync(response, HttpStatusCode.NotFound, "listings.product_not_found");
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
    public async Task Search_FindsProductsByTitleAndDescription_NewestFirst()
    {
        using var seller = await SignedInClientAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var first = await PublishAsync(seller, Product([await UploadKeyAsync(seller)], title: $"Microscopio {tag}"));
        var second = await PublishAsync(seller, Product([await UploadKeyAsync(seller)], title: "Libro de biología", description: $"Incluye el código {tag}."));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstId = (await AuthFlow.JsonAsync(first)).GetProperty("id").GetString();
        var secondId = (await AuthFlow.JsonAsync(second)).GetProperty("id").GetString();

        // HU-09 R3 y R5: por el titulo o por la descripcion, de la mas nueva a la mas vieja.
        var page = await GetJsonAsync(seller, $"/api/v1/products?q={tag}");

        Assert.Equal(2, page.GetProperty("total").GetInt64());
        Assert.Equal(new[] { secondId, firstId }, page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Search_WithoutMatches_ReturnsAnEmptyPage()
    {
        using var visitor = factory.CreateClient();

        var page = await GetJsonAsync(visitor, "/api/v1/products?q=zzzzqqqq");

        // HU-09 R7: sin resultados no hay error; la app muestra las opciones de HU-12.
        Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        Assert.Equal(0, page.GetProperty("total").GetInt64());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Search_WithAnInvalidQuery_IsRejected_AndAnEmptyOneIsTheShowcase()
    {
        using var visitor = factory.CreateClient();

        await AuthFlow.AssertCodeAsync(
            await visitor.GetAsync(new Uri($"/api/v1/products?q={new string('a', 81)}", UriKind.Relative), Ct),
            HttpStatusCode.BadRequest,
            "listings.invalid_query");
        await AuthFlow.AssertCodeAsync(
            await visitor.GetAsync(new Uri("/api/v1/products?q=%21%3F%2D", UriKind.Relative), Ct),
            HttpStatusCode.BadRequest,
            "listings.invalid_query");

        // HU-09 R8: q vacio equivale a no buscar.
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(new Uri("/api/v1/products?q=", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task Search_MatchesWordPrefixes()
    {
        using var seller = await SignedInClientAsync();
        var published = await PublishAsync(seller, Product([await UploadKeyAsync(seller)], title: "Calculadora Casio fx-991ES"));
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        var id = (await AuthFlow.JsonAsync(published)).GetProperty("id").GetString();

        // HU-09: "calc" y "calcula" encuentran "Calculadora"; el prefijo no distingue mayusculas.
        foreach (var q in new[] { "calc", "calcula", "CASI" })
        {
            var page = await GetJsonAsync(seller, $"/api/v1/products?q={q}");

            Assert.Contains(page.GetProperty("items").EnumerateArray(), p => p.GetProperty("id").GetString() == id);
        }
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

    // HU-10: filtros de la hoja "Busqueda-filtros" (categorias varias, precio, condicion y orden).

    [Fact]
    public async Task Showcase_FiltersByCategoriesPriceAndCondition()
    {
        using var seller = await SignedInClientAsync();
        var usedBook = await PublishIdAsync(seller, Product([await UploadKeyAsync(seller)], category: "libros", priceCents: 10_000));
        var usedLaptop = await PublishIdAsync(seller, Product([await UploadKeyAsync(seller)], category: "laptops", priceCents: 30_000));
        await PublishIdAsync(seller, Product([await UploadKeyAsync(seller)], category: "laptops", priceCents: 80_000, condition: "new", detail: null));
        await PublishIdAsync(seller, Product([await UploadKeyAsync(seller)], category: "calculadoras", priceCents: 20_000));

        var page = await GetJsonAsync(
            seller,
            "/api/v1/products?category=libros&category=laptops&minPriceCents=10000&maxPriceCents=50000&condition=used&sort=priceAsc");
        var items = page.GetProperty("items").EnumerateArray().ToList();

        // HU-10 R1, R2, R4 y R7: OR de categorias, rango inclusivo, condicion y orden por precio.
        Assert.Equal([usedBook, usedLaptop], items.Select(p => p.GetProperty("id").GetString()));
        Assert.All(items, p => Assert.Equal("used", p.GetProperty("condition").GetString()));
        Assert.All(items, p => Assert.InRange(p.GetProperty("priceCents").GetInt64(), 10_000, 50_000));
        Assert.True(page.GetProperty("total").GetInt64() >= 2);
    }

    [Fact]
    public async Task Showcase_SortsByPrice_AndPagesWithTheCursor()
    {
        using var seller = await SignedInClientAsync();
        var cheap = await PublishIdAsync(seller, Product([await UploadKeyAsync(seller)], category: "electronica", priceCents: 10_000));
        var middle = await PublishIdAsync(seller, Product([await UploadKeyAsync(seller)], category: "electronica", priceCents: 20_000));
        var expensive = await PublishIdAsync(seller, Product([await UploadKeyAsync(seller)], category: "electronica", priceCents: 30_000));

        var first = await GetJsonAsync(seller, "/api/v1/products?category=electronica&sort=priceAsc&limit=2");
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await GetJsonAsync(seller, $"/api/v1/products?category=electronica&sort=priceAsc&limit=2&cursor={Uri.EscapeDataString(cursor!)}");
        var descending = await GetJsonAsync(seller, "/api/v1/products?category=electronica&sort=priceDesc");

        // HU-10 R7 y R8: el cursor es opaco, lleva el orden y conserva los filtros.
        Assert.StartsWith("priceAsc:", cursor, StringComparison.Ordinal);
        Assert.Equal([cheap, middle], first.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(expensive, Assert.Single(second.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.Equal(3, second.GetProperty("total").GetInt64());
        Assert.Equal(
            [expensive, middle, cheap],
            descending.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Showcase_PagesByNumber()
    {
        using var seller = await SignedInClientAsync();
        var cheap = await PublishIdAsync(seller, Product([await UploadKeyAsync(seller)], category: "dibujo", priceCents: 10_000));
        var middle = await PublishIdAsync(seller, Product([await UploadKeyAsync(seller)], category: "dibujo", priceCents: 20_000));
        var expensive = await PublishIdAsync(seller, Product([await UploadKeyAsync(seller)], category: "dibujo", priceCents: 30_000));

        var first = await GetJsonAsync(seller, "/api/v1/products?category=dibujo&sort=priceAsc&limit=2&page=1");
        var second = await GetJsonAsync(seller, "/api/v1/products?category=dibujo&sort=priceAsc&limit=2&page=2");

        // HU-10: la pagina ignora el cursor y no devuelve uno.
        Assert.Equal([cheap, middle], first.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()));
        Assert.Equal(expensive, Assert.Single(second.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("nextCursor").ValueKind);
        Assert.Equal(3, second.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Showcase_RejectsInvalidFilters()
    {
        using var visitor = factory.CreateClient();

        await AuthFlow.AssertCodeAsync(
            await visitor.GetAsync(new Uri("/api/v1/products?minPriceCents=50000&maxPriceCents=10000", UriKind.Relative), Ct),
            HttpStatusCode.BadRequest,
            "listings.invalid_price_range");
        await AuthFlow.AssertCodeAsync(
            await visitor.GetAsync(new Uri("/api/v1/products?minPriceCents=-1", UriKind.Relative), Ct),
            HttpStatusCode.BadRequest,
            "listings.invalid_price_range");
        await AuthFlow.AssertCodeAsync(
            await visitor.GetAsync(new Uri("/api/v1/products?maxPriceCents=99999901", UriKind.Relative), Ct),
            HttpStatusCode.BadRequest,
            "listings.invalid_price_range");
        // HU-10 R4 y R5: condicion y orden tienen lista blanca.
        await AuthFlow.AssertCodeAsync(
            await visitor.GetAsync(new Uri("/api/v1/products?condition=roto", UriKind.Relative), Ct),
            HttpStatusCode.BadRequest,
            "listings.invalid_condition");
        await AuthFlow.AssertCodeAsync(
            await visitor.GetAsync(new Uri("/api/v1/products?sort=roto", UriKind.Relative), Ct),
            HttpStatusCode.BadRequest,
            "listings.invalid_sort");
        await AuthFlow.AssertCodeAsync(
            await visitor.GetAsync(new Uri("/api/v1/products?page=0", UriKind.Relative), Ct),
            HttpStatusCode.BadRequest,
            "listings.invalid_page");
        // HU-10 R8: el ObjectId crudo solo se acepta para newest y relevance.
        await AuthFlow.AssertCodeAsync(
            await visitor.GetAsync(new Uri("/api/v1/products?sort=priceAsc&cursor=6716a0000000000000000099", UriKind.Relative), Ct),
            HttpStatusCode.BadRequest,
            "listings.invalid_cursor");
    }
}
