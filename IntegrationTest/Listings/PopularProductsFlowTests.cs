using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using IntegrationTest.Auth;

namespace IntegrationTest.Listings;

/// <summary>HU-49: vista del detalle y ranking público por periodo/campus.</summary>
public sealed class PopularProductsFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Popular_UsesAggregatedViewsAndDoesNotCountSellerOrExposeSellerToVisitor()
    {
        var seller = AuthIds.New();
        var productId = AuthIds.New();
        var repository = factory.Services.GetRequiredService<IProductRepository>();
        var now = factory.Clock.Now.UtcDateTime;
        await repository.InsertAsync(new ProductDocument
        {
            Id = productId,
            CommunityId = InMemoryCommunityRepository.UtttId,
            SellerId = seller,
            Seller = new ProductSeller { Name = "Vendedor privado" },
            Title = "Producto ranking HU-49",
            CategorySlug = "calculadoras",
            PriceCents = 1000,
            Condition = ProductConditions.New,
            Stock = 1,
            Status = ProductStatuses.Active,
            Photos = [$"media/products/{seller}/{Guid.NewGuid():N}.webp"],
            CreatedAt = now.AddMinutes(-1),
            UpdatedAt = now,
        }, Ct);

        using var visitor = factory.CreateClient();
        using var owner = ClientFor(seller, InMemoryCommunityRepository.UtttId);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(new Uri($"/api/v1/products/{productId}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await visitor.GetAsync(new Uri($"/api/v1/products/{productId}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(new Uri($"/api/v1/products/{productId}", UriKind.Relative), Ct)).StatusCode);

        using var response = await visitor.GetAsync(new Uri($"/api/v1/products/popular?period=today&limit=10&test={Guid.NewGuid():N}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(productId, item.GetProperty("product").GetProperty("id").GetString());
        Assert.Equal(2, item.GetProperty("views").GetInt64());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("product").GetProperty("seller").ValueKind);
        Assert.DoesNotContain(item.EnumerateObject(), property => property.Name is "viewerId" or "userId");
    }

    [Fact]
    public async Task Popular_RejectsUnknownPeriodAndScopesAnonymousCampus()
    {
        using var client = factory.CreateClient();
        var invalid = await client.GetAsync(new Uri("/api/v1/products/popular?period=month", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var otherCampus = await client.GetAsync(new Uri("/api/v1/products/popular?period=week&campus=campus-that-does-not-exist", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.NotFound, otherCampus.StatusCode);

        var invalidCursor = await client.GetAsync(new Uri("/api/v1/products/popular?period=week&cursor=999999999999999999", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
    }

    [Fact]
    public async Task Popular_UsesOpaqueKeysetCursorAcrossPages()
    {
        var communityId = InMemoryCommunityRepository.UtttId;
        var now = factory.Clock.Now.UtcDateTime;
        var products = factory.Services.GetRequiredService<IProductRepository>();
        var stats = factory.Services.GetRequiredService<IProductStatsRepository>();
        var ids = new[] { AuthIds.New(), AuthIds.New() };
        for (var index = 0; index < ids.Length; index++)
        {
            await products.InsertAsync(new ProductDocument
            {
                Id = ids[index],
                CommunityId = communityId,
                SellerId = AuthIds.New(),
                Seller = new ProductSeller { Name = "Vendedor" },
                Title = "Producto cursor HU-49",
                CategorySlug = "calculadoras",
                PriceCents = 1000,
                Condition = ProductConditions.New,
                Stock = 1,
                Status = ProductStatuses.Active,
                Photos = [$"media/products/{AuthIds.New()}/{Guid.NewGuid():N}.webp"],
                CreatedAt = now.AddMinutes(-index),
                UpdatedAt = now,
            }, Ct);
            for (var view = 0; view < 1000 - index; view++)
            {
                await stats.IncrementViewAsync(communityId, ids[index], now.Date, Ct);
            }
        }

        using var client = factory.CreateClient();
        var cacheKey = Guid.NewGuid().ToString("N");
        using var firstResponse = await client.GetAsync(new Uri($"/api/v1/products/popular?period=today&limit=1&test={cacheKey}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        using var firstJson = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync(Ct));
        var first = Assert.Single(firstJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(ids[0], first.GetProperty("product").GetProperty("id").GetString());
        var cursor = firstJson.RootElement.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);
        Assert.False(long.TryParse(cursor, out _));

        using var secondResponse = await client.GetAsync(new Uri($"/api/v1/products/popular?period=today&limit=1&cursor={Uri.EscapeDataString(cursor)}&test={cacheKey}", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync(Ct));
        var second = Assert.Single(secondJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(ids[1], second.GetProperty("product").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Popular_PagesEligibleProductsAndExcludesInactiveProductsBeforeCounting()
    {
        var communityId = AuthIds.New();
        var now = factory.Clock.Now.UtcDateTime;
        var repository = factory.Services.GetRequiredService<IProductRepository>();
        var stats = factory.Services.GetRequiredService<IProductStatsRepository>();
        var products = new[]
        {
            (Id: AuthIds.New(), Status: ProductStatuses.Active, Views: 3L),
            (Id: AuthIds.New(), Status: ProductStatuses.Active, Views: 2L),
            (Id: AuthIds.New(), Status: ProductStatuses.Hidden, Views: 10L),
        };

        foreach (var candidate in products)
        {
            await repository.InsertAsync(new ProductDocument
            {
                Id = candidate.Id,
                CommunityId = communityId,
                SellerId = AuthIds.New(),
                Seller = new ProductSeller { Name = "Vendedor" },
                Title = "Producto paginado HU-49",
                CategorySlug = "calculadoras",
                PriceCents = 1000,
                Condition = ProductConditions.New,
                Stock = 1,
                Status = candidate.Status,
                Photos = [$"media/products/{AuthIds.New()}/{Guid.NewGuid():N}.webp"],
                CreatedAt = now,
                UpdatedAt = now,
            }, Ct);
            for (var view = 0; view < candidate.Views; view++)
            {
                await stats.IncrementViewAsync(communityId, candidate.Id, now.Date, Ct);
            }
        }

        var firstPage = await stats.PopularAsync(communityId, now.Date, now.Date, null, 1, Ct);
        Assert.Equal(2, firstPage.Total);
        var first = Assert.Single(firstPage.Items);
        Assert.Equal(products[0].Id, first.Product.Id);
        Assert.Equal(3, first.Views);

        var nextCursor = new PopularProductCursor(first.Views, first.Product.CreatedAt, first.Product.Id);
        var nextPage = await stats.PopularAsync(communityId, now.Date, now.Date, nextCursor, 1, Ct);
        Assert.Equal(2, nextPage.Total);
        var next = Assert.Single(nextPage.Items);
        Assert.Equal(products[1].Id, next.Product.Id);
        Assert.Equal(2, next.Views);
    }

    private HttpClient ClientFor(string userId, string communityId)
    {
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }
        var token = factory.Services.GetRequiredService<Musubi.Api.Platform.Auth.AccessTokenIssuer>()
            .Issue(userId, communityId, ["student"]);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }
}
