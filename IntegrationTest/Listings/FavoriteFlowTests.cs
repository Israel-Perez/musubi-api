using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using SkiaSharp;

namespace IntegrationTest.Listings;

/// <summary>
/// HU-53: guardar desde el corazon, ver "Favoritos", repetir y quitar; los estados "Agotado" y "Ya no disponible";
/// y el bloqueo de HU-57. La app arma el corazon con GET /favorites.
/// </summary>
public sealed class FavoriteFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
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
            canvas.Clear(SKColors.Orange);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<(string Id, string Title)> PublishAsync(Member seller)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await seller.Client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString();
        var title = "Calculadora de favoritos " + Guid.NewGuid().ToString("N")[..6];
        var published = await seller.Client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title, description = "La usé un semestre.", category = "calculadoras", priceCents = 45_000, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { key } },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return ((await AuthFlow.JsonAsync(published).ConfigureAwait(false)).GetProperty("id").GetString()!, title);
    }

    private static ProductDocument Product(string sellerId, string status, string title) => new()
    {
        Id = AuthIds.New(),
        CommunityId = InMemoryCommunityRepository.UtttId,
        SellerId = sellerId,
        Seller = new ProductSeller { Name = "Ana R." },
        Title = title,
        Description = "La usé un semestre.",
        CategorySlug = "calculadoras",
        PriceCents = 45_000,
        Condition = ProductConditions.Used,
        ConditionDetail = "good",
        Stock = status == ProductStatuses.SoldOut ? 0 : 1,
        Status = status,
        Photos = [],
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static Task<HttpResponseMessage> SaveAsync(Member buyer, string productId) =>
        buyer.Client.PutAsync(new Uri($"/api/v1/favorites/{productId}", UriKind.Relative), null, Ct);

    private static Task<HttpResponseMessage> RemoveAsync(Member buyer, string productId) =>
        buyer.Client.DeleteAsync(new Uri($"/api/v1/favorites/{productId}", UriKind.Relative), Ct);

    private static async Task<JsonElement> FavoritesAsync(Member buyer)
    {
        var response = await buyer.Client.GetAsync(new Uri("/api/v1/favorites", UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    [Fact]
    public async Task Save_ShowsInTheList_IsIdempotent_AndCanBeRemoved()
    {
        var seller = await MemberAsync();
        var buyer = await MemberAsync();
        var stranger = await MemberAsync();
        var (productId, title) = await PublishAsync(seller);

        var first = await SaveAsync(buyer, productId);
        var again = await SaveAsync(buyer, productId);
        var list = await FavoritesAsync(buyer);
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        var strangerList = await FavoritesAsync(stranger);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal((productId, "available", title), (
            item.GetProperty("productId").GetString(),
            item.GetProperty("status").GetString(),
            item.GetProperty("product").GetProperty("title").GetString()));
        Assert.Empty(strangerList.GetProperty("items").EnumerateArray());

        var removed = await RemoveAsync(buyer, productId);
        var removedAgain = await RemoveAsync(buyer, productId);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removedAgain.StatusCode);
        Assert.Empty((await FavoritesAsync(buyer)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task List_ShowsSoldOutAndUnavailable_AndABlockedSellerCannotBeSaved()
    {
        var seller = await MemberAsync();
        var buyer = await MemberAsync();
        var products = factory.Services.GetRequiredService<IProductRepository>();
        var favorites = factory.Services.GetRequiredService<IFavoriteRepository>();
        var active = Product(seller.Id, ProductStatuses.Active, "Calculadora activa");
        var soldOut = Product(seller.Id, ProductStatuses.SoldOut, "Calculadora agotada");
        var hidden = Product(seller.Id, ProductStatuses.Hidden, "Calculadora pausada");
        var removed = Product(seller.Id, ProductStatuses.Removed, "Calculadora quitada");
        foreach (var product in new[] { active, soldOut, hidden, removed })
        {
            await products.InsertAsync(product, Ct);
        }

        var missing = AuthIds.New();
        Assert.Equal(HttpStatusCode.NoContent, (await SaveAsync(buyer, active.Id)).StatusCode);
        foreach (var productId in new[] { soldOut.Id, hidden.Id, removed.Id, missing })
        {
            await favorites.InsertIfAbsentAsync(
                new FavoriteDocument
                {
                    Id = AuthIds.New(),
                    UserId = buyer.Id,
                    ProductId = productId,
                    CommunityId = InMemoryCommunityRepository.UtttId,
                    CreatedAt = factory.Clock.Now.AddMinutes(-1).UtcDateTime,
                },
                Ct);
        }

        var list = await FavoritesAsync(buyer);
        var byProduct = list.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("productId").GetString()!);
        Assert.Equal(5, byProduct.Count);
        Assert.Equal("available", byProduct[active.Id].GetProperty("status").GetString());
        Assert.Equal("soldOut", byProduct[soldOut.Id].GetProperty("status").GetString());
        foreach (var unavailable in new[] { hidden.Id, removed.Id, missing })
        {
            Assert.Equal("unavailable", byProduct[unavailable].GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, byProduct[unavailable].GetProperty("product").ValueKind);
        }

        var paused = await SaveAsync(buyer, hidden.Id);
        Assert.Equal(HttpStatusCode.NotFound, paused.StatusCode);
        Assert.Equal("listings.product_not_found", (await AuthFlow.JsonAsync(paused)).GetProperty("code").GetString());

        // HU-57: el bloqueo aplica en las dos direcciones y la lista no revela quien bloqueo.
        var blocked = await buyer.Client.PostAsJsonAsync(new Uri("/api/v1/blocks", UriKind.Relative), new { userId = seller.Id }, Ct);
        var afterBlock = await FavoritesAsync(buyer);
        var blockedItem = afterBlock.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("productId").GetString() == active.Id);
        var blockedSave = await SaveAsync(buyer, active.Id);

        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
        Assert.Equal("unavailable", blockedItem.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, blockedItem.GetProperty("product").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, blockedSave.StatusCode);
    }

    [Fact]
    public async Task DeletingTheSeller_LeavesTheBuyersFavoriteAsUnavailable()
    {
        var seller = await MemberAsync();
        var buyer = await MemberAsync();
        var (productId, _) = await PublishAsync(seller);
        Assert.Equal(HttpStatusCode.NoContent, (await SaveAsync(buyer, productId)).StatusCode);

        var deleted = await seller.Client.PostAsJsonAsync(
            new Uri("/api/v1/account/delete", UriKind.Relative), new { password = AuthFlow.Password }, Ct);
        var item = Assert.Single((await FavoritesAsync(buyer)).GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal("unavailable", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("product").ValueKind);
    }
}
