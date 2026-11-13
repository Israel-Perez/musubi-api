using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;
using SkiaSharp;

namespace ContractTest;

/// <summary>
/// HU-06 / HU-48 fase 2 / HU-09 / HU-07 / HU-08: formas JSON de /api/v1/products y /api/v1/categories que consume musubi-app.
/// Cambiar un fixture = cambio de contrato: actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class ListingsContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> FixtureAsync(string name)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    /// <summary>Nombres y tipos, recursivo en los objetos anidados.</summary>
    private static List<string> Shape(JsonElement element, string prefix = "") =>
        [.. element.EnumerateObject()
            .SelectMany(p => p.Value.ValueKind == JsonValueKind.Object
                ? Shape(p.Value, $"{prefix}{p.Name}.")
                : [$"{prefix}{p.Name}:{p.Value.ValueKind}"])
            .Order(StringComparer.Ordinal)];

    private static ByteArrayContent Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Orange);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var content = new ByteArrayContent(data.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    [Fact]
    public async Task PopularProducts_MatchesItsFixtureAndHidesSellerForVisitor()
    {
        var sellerId = Musubi.Api.Modules.Auth.Data.AuthIds.New();
        var productId = Musubi.Api.Modules.Auth.Data.AuthIds.New();
        var now = DateTime.UtcNow;
        await factory.Services.GetRequiredService<IProductRepository>().InsertAsync(new ProductDocument
        {
            Id = productId,
            CommunityId = InMemoryCommunityRepository.UtttId,
            SellerId = sellerId,
            Seller = new ProductSeller { Name = "Ana R." },
            Title = "Producto del contrato de populares",
            Description = "Fixture de HU-49",
            CategorySlug = "calculadoras",
            PriceCents = 45_000,
            Condition = ProductConditions.Used,
            ConditionDetail = "likeNew",
            Stock = 1,
            Status = ProductStatuses.Active,
            Photos = [$"media/products/{sellerId}/0123456789abcdef0123456789abcdef.webp"],
            CreatedAt = now,
            UpdatedAt = now,
        }, Ct);
        await factory.Services.GetRequiredService<IProductStatsRepository>().IncrementViewAsync(
            InMemoryCommunityRepository.UtttId, productId, now.Date, Ct);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri($"/api/v1/products/popular?period=today&campus=uttt&test={Guid.NewGuid():N}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(Shape(await FixtureAsync("popular-product-page.json")), Shape(body));
        Assert.Equal(JsonValueKind.Null, body.GetProperty("items")[0].GetProperty("product").GetProperty("seller").ValueKind);
    }

    [Fact]
    public async Task PhotoProductPageCategoriesEditsAndFavorites_MatchTheirFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        using var client = app.CreateClient();
        var email = $"ana.{Guid.NewGuid():N}@uttt.edu.mx";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Ana Sofía Ramírez", password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct);
        var session = await BodyAsync(await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());

        using var form = new MultipartFormDataContent();
        using var file = Png();
        form.Add(file, "photo", "foto.png");
        var photo = await BodyAsync(await client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct));
        var product = await BodyAsync(await client.PostAsJsonAsync(
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
                photos = new[] { photo.GetProperty("key").GetString() },
            },
            Ct));
        var page = await BodyAsync(await client.GetAsync(new Uri("/api/v1/products?category=calculadoras", UriKind.Relative), Ct));
        // HU-10: los filtros y el orden conservan la forma de la pagina.
        var filtered = await BodyAsync(await client.GetAsync(
            new Uri("/api/v1/products?category=calculadoras&minPriceCents=40000&maxPriceCents=50000&condition=used&sort=priceAsc", UriKind.Relative), Ct));
        // HU-09: la busqueda devuelve la misma forma que la vitrina (mismo fixture).
        var searched = await BodyAsync(await client.GetAsync(new Uri("/api/v1/products?q=calculadora", UriKind.Relative), Ct));
        var categories = await BodyAsync(await client.GetAsync(new Uri("/api/v1/categories", UriKind.Relative), Ct));
        var detail = await BodyAsync(await client.GetAsync(new Uri($"/api/v1/products/{product.GetProperty("id").GetString()}", UriKind.Relative), Ct));
        var productId = product.GetProperty("id").GetString()!;
        var saved = await client.PutAsync(new Uri($"/api/v1/favorites/{productId}", UriKind.Relative), null, Ct);
        await app.Services.GetRequiredService<IFavoriteRepository>().InsertIfAbsentAsync(
            new FavoriteDocument
            {
                Id = AuthIds.New(),
                UserId = session.GetProperty("user").GetProperty("id").GetString()!,
                ProductId = "6716a0000000000000000098",
                CommunityId = InMemoryCommunityRepository.UtttId,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
            },
            Ct);
        var favorites = await BodyAsync(await client.GetAsync(new Uri("/api/v1/favorites", UriKind.Relative), Ct));
        var favoritesFixture = await FixtureAsync("favorites.json");

        Assert.Equal(Shape(await FixtureAsync("photo.json")), Shape(photo));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(product));
        Assert.Equal(Shape(await FixtureAsync("product-page.json")), Shape(page));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(page.GetProperty("items")[0]));
        // HU-10 R13: la forma de ProductPage no cambia con filtros ni orden.
        Assert.Equal(Shape(await FixtureAsync("product-page.json")), Shape(filtered));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(filtered.GetProperty("items")[0]));
        Assert.Equal(Shape(await FixtureAsync("product-page.json")), Shape(searched));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(searched.GetProperty("items")[0]));
        Assert.Equal(Shape(await FixtureAsync("category.json")), Shape(categories[0]));
        Assert.Equal(Shape(await FixtureAsync("product-detail.json")), Shape(detail));

        // HU-07: editar, pausar y reactivar responden con la misma forma de publicacion.
        var id = product.GetProperty("id").GetString();
        var edited = await BodyAsync(await client.PutAsJsonAsync(
            new Uri($"/api/v1/products/{id}", UriKind.Relative),
            new
            {
                title = "Calculadora científica Casio fx-991ES Plus",
                description = "Con su tapa y pilas nuevas.",
                category = "calculadoras",
                priceCents = 42_000,
                condition = "used",
                conditionDetail = "good",
            },
            Ct));
        var paused = await BodyAsync(await client.PostAsync(new Uri($"/api/v1/products/{id}/pause", UriKind.Relative), null, Ct));
        var resumed = await BodyAsync(await client.PostAsync(new Uri($"/api/v1/products/{id}/resume", UriKind.Relative), null, Ct));

        // HU-08: "Mis productos" por pestana con sus conteos.
        var mine = await BodyAsync(await client.GetAsync(new Uri("/api/v1/products/mine?tab=active", UriKind.Relative), Ct));
        Assert.Equal(Shape(await FixtureAsync("my-product-page.json")), Shape(mine));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(mine.GetProperty("items")[0]));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(edited));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(paused));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(resumed));

        // HU-50: "Cambiar piezas" responde la misma forma.
        var restocked = await BodyAsync(await client.PutAsJsonAsync(
            new Uri($"/api/v1/products/{id}/stock", UriKind.Relative), new { stock = 4 }, Ct));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(restocked));
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        Assert.Equal(Shape(favoritesFixture), Shape(favorites));
        Assert.Equal(Shape(favoritesFixture.GetProperty("items")[0]), Shape(favorites.GetProperty("items")[0]));
        Assert.Equal(Shape(favoritesFixture.GetProperty("items")[1]), Shape(favorites.GetProperty("items")[1]));
    }
}
