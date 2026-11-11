using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Listings.Data;
using SkiaSharp;

namespace IntegrationTest.Listings;

/// <summary>
/// HU-08: "Mis productos" por pestana (Activos, Vendidos y Agotados) con sus conteos, y "Marcar como vendido"
/// (maqueta <c>Mis-productos</c>).
/// </summary>
public sealed class MyProductsTabsFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Cuenta activa con sesion: el cliente y el id de la persona.</summary>
    private async Task<(HttpClient Client, string UserId)> SignedInAsync()
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
        return (client, login.GetProperty("user").GetProperty("id").GetString()!);
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Navy);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<string> PublishAsync(HttpClient seller, string title)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString()!;
        var published = await seller.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title, description = "", category = "calculadoras", priceCents = 45_000, condition = "new", stock = 1, photos = new[] { key } },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return (await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> MineAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync(new Uri("/api/v1/products/mine" + query, UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    private static List<string?> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();

    [Fact]
    public async Task EachProduct_IsInItsTab_WithTheCountsOfTheThree()
    {
        var (seller, sellerId) = await SignedInAsync();
        using var client = seller;
        var available = await PublishAsync(client, "Calculadora Casio fx-570");
        var paused = await PublishAsync(client, "Calculadora Casio fx-82");
        var sold = await PublishAsync(client, "Calculadora graficadora");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(new Uri($"/api/v1/products/{paused}/pause", UriKind.Relative), null, Ct)).StatusCode);
        var marked = await client.PostAsync(new Uri($"/api/v1/products/{sold}/sold", UriKind.Relative), null, Ct);
        // Sin piezas: hoy solo cambia con los pedidos (HU-23) o "Reponer" (HU-50), asi que se deja asi en la base.
        var soldOut = new ProductDocument
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            CommunityId = Musubi.Api.Modules.Auth.Data.InMemoryCommunityRepository.UtttId,
            SellerId = sellerId,
            Seller = new ProductSeller { Name = "Ana P." },
            Title = "Escuadras de dibujo",
            CategorySlug = "dibujo",
            PriceCents = 12_000,
            Condition = ProductConditions.New,
            Stock = 0,
            Status = ProductStatuses.SoldOut,
            Photos = [$"media/products/{sellerId}/{Guid.NewGuid():N}.webp"],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await factory.Services.GetRequiredService<IProductRepository>().InsertAsync(soldOut, Ct);

        var activeTab = await MineAsync(client, "?tab=active");
        var soldTab = await MineAsync(client, "?tab=sold");
        var soldOutTab = await MineAsync(client, "?tab=soldOut");
        var all = await MineAsync(client);

        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        var markedBody = await AuthFlow.JsonAsync(marked);
        Assert.Equal("sold", markedBody.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, markedBody.GetProperty("soldAt").ValueKind);
        Assert.Equal([paused, available], Ids(activeTab));
        Assert.Equal([sold], Ids(soldTab));
        Assert.Equal([soldOut.Id], Ids(soldOutTab));
        Assert.Equal(4, all.GetProperty("total").GetInt64());
        Assert.Equal(2, activeTab.GetProperty("total").GetInt64());
        var counts = activeTab.GetProperty("counts");
        Assert.Equal((2, 1, 1), (counts.GetProperty("active").GetInt64(), counts.GetProperty("sold").GetInt64(), counts.GetProperty("soldOut").GetInt64()));
    }

    [Fact]
    public async Task ASoldProduct_LeavesTheShowcase_AndCannotBeResumed()
    {
        var (seller, _) = await SignedInAsync();
        using var client = seller;
        var id = await PublishAsync(client, "Calculadora Casio fx-991");
        using var anonymous = factory.CreateClient();
        Assert.Contains(id, Ids(await MineAsync(client)));

        await client.PostAsync(new Uri($"/api/v1/products/{id}/sold", UriKind.Relative), null, Ct);
        var showcase = await AuthFlow.JsonAsync(await anonymous.GetAsync(new Uri("/api/v1/products?limit=50", UriKind.Relative), Ct));
        var resume = await client.PostAsync(new Uri($"/api/v1/products/{id}/resume", UriKind.Relative), null, Ct);
        var detail = await client.GetAsync(new Uri($"/api/v1/products/{id}", UriKind.Relative), Ct);

        Assert.DoesNotContain(id, Ids(showcase));
        await AuthFlow.AssertCodeAsync(resume, HttpStatusCode.Conflict, "listings.product_sold");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task ASoldProduct_CannotStartAChat_NorBeOffered()
    {
        // HU-08 R4: una vendida sale del chat (HU-20) y de las ofertas (HU-16).
        var (seller, _) = await SignedInAsync();
        var (buyer, _) = await SignedInAsync();
        using var sellerClient = seller;
        using var buyerClient = buyer;
        var id = await PublishAsync(sellerClient, "Calculadora Casio fx-82");
        var wanted = await buyerClient.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Calculadora científica", category = "calculadoras", budgetCents = 50_000, quantity = 1 },
            Ct);
        var requestId = (await AuthFlow.JsonAsync(wanted)).GetProperty("id").GetString();
        await sellerClient.PostAsync(new Uri($"/api/v1/products/{id}/sold", UriKind.Relative), null, Ct);

        var chat = await buyerClient.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId = id }, Ct);
        var offer = await sellerClient.PutAsJsonAsync(new Uri($"/api/v1/wanted/{requestId}/offers/mine", UriKind.Relative), new { productId = id }, Ct);

        await AuthFlow.AssertCodeAsync(chat, HttpStatusCode.NotFound, "chat.product_unavailable");
        await AuthFlow.AssertCodeAsync(offer, HttpStatusCode.BadRequest, "offers.product_unavailable");
    }

    [Fact]
    public async Task Mine_PagesWithACursor_AndRejectsAnUnknownTab()
    {
        var (seller, _) = await SignedInAsync();
        using var client = seller;
        var first = await PublishAsync(client, "Libro de Cálculo");
        var second = await PublishAsync(client, "Libro de Física");

        var page1 = await MineAsync(client, "?tab=active&limit=1");
        var page2 = await MineAsync(client, $"?tab=active&limit=1&cursor={page1.GetProperty("nextCursor").GetString()}");
        var unknown = await client.GetAsync(new Uri("/api/v1/products/mine?tab=reservados", UriKind.Relative), Ct);

        Assert.Equal([second], Ids(page1));
        Assert.Equal([first], Ids(page2));
        // La ultima pagina llega justo llena: no hay cursor a una pagina vacia (revision del PR #61).
        Assert.Equal(JsonValueKind.Null, page2.GetProperty("nextCursor").ValueKind);
        await AuthFlow.AssertCodeAsync(unknown, HttpStatusCode.BadRequest, "listings.invalid_tab");
    }
}
