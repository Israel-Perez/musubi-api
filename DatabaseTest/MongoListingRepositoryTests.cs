using MongoDB.Driver;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.TestShared.Listings;

namespace DatabaseTest;

/// <summary>HU-06: los repositorios de Mongo cumplen el contrato contra MongoDB real con db/ aplicado (incluida su validacion).</summary>
public sealed class MongoListingRepositoryTests(MongoContainerFixture mongo) : ListingRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override ICategoryRepository Categories => new MongoCategoryRepository(database!);

    internal override IProductRepository Products => new MongoProductRepository(database!);

    internal override async Task<string> UtttIdAsync() =>
        (await new MongoCommunityRepository(database!).FindBySlugAsync("uttt", TestContext.Current.CancellationToken).ConfigureAwait(false))!.Id;

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("listings_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Popular_AggregatesEligibleProductsAndReturnsOnlyRequestedPage()
    {
        var communityId = AuthIds.New();
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var products = Products;
        var stats = new MongoProductStatsRepository(database!);
        var candidates = new[]
        {
            (Id: AuthIds.New(), Status: ProductStatuses.Active, Views: 3L, CreatedAt: now.AddMinutes(-1)),
            (Id: AuthIds.New(), Status: ProductStatuses.Active, Views: 2L, CreatedAt: now),
            (Id: AuthIds.New(), Status: ProductStatuses.Hidden, Views: 10L, CreatedAt: now.AddMinutes(1)),
        };

        foreach (var candidate in candidates)
        {
            await products.InsertAsync(new ProductDocument
            {
                Id = candidate.Id,
                CommunityId = communityId,
                SellerId = AuthIds.New(),
                Seller = new ProductSeller { Name = "Ana R." },
                Title = "Producto popular",
                Description = string.Empty,
                CategorySlug = "calculadoras",
                PriceCents = 1000,
                Condition = ProductConditions.New,
                Stock = 1,
                Status = candidate.Status,
                Photos = [$"media/products/{AuthIds.New()}/{Guid.NewGuid():N}.webp"],
                CreatedAt = candidate.CreatedAt,
                UpdatedAt = candidate.CreatedAt,
            }, TestContext.Current.CancellationToken);
            for (var view = 0; view < candidate.Views; view++)
            {
                await stats.IncrementViewAsync(communityId, candidate.Id, now.Date, TestContext.Current.CancellationToken);
            }
        }

        var first = await stats.PopularAsync(communityId, now.Date, now.Date, null, 1, TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Total);
        var firstItem = Assert.Single(first.Items);
        Assert.Equal(candidates[0].Id, firstItem.Product.Id);
        Assert.Equal(3, firstItem.Views);
        var cursor = new PopularProductCursor(firstItem.Views, firstItem.Product.CreatedAt, firstItem.Product.Id);
        var second = await stats.PopularAsync(communityId, now.Date, now.Date, cursor, 1, TestContext.Current.CancellationToken);
        Assert.Equal(2, second.Total);
        Assert.Equal(candidates[1].Id, Assert.Single(second.Items).Product.Id);
        Assert.Equal(2, second.Items[0].Views);
    }

    [Fact]
    public async Task ViewsSince_AddsTheDaysOfEachListing_FromTheFirstDay()
    {
        // HU-36: "96 vistas" de los ultimos 30 dias.
        var ct = TestContext.Current.CancellationToken;
        var communityId = AuthIds.New();
        var stats = new MongoProductStatsRepository(database!);
        var bata = AuthIds.New();
        var calculadora = AuthIds.New();
        // productStatsDaily vence a los 30 dias (TTL): con una fecha fija, MongoDB borraria las filas a mitad de la prueba.
        var today = DateTime.UtcNow.Date.AddDays(10);
        await stats.IncrementViewAsync(communityId, bata, today, ct);
        await stats.IncrementViewAsync(communityId, bata, today, ct);
        await stats.IncrementViewAsync(communityId, bata, today.AddDays(-29), ct);
        await stats.IncrementViewAsync(communityId, bata, today.AddDays(-30), ct);
        await stats.IncrementViewAsync(communityId, calculadora, today.AddDays(-3), ct);
        await stats.IncrementViewAsync(AuthIds.New(), calculadora, today, ct);

        var views = await stats.ViewsSinceAsync(communityId, [bata, calculadora, AuthIds.New(), "no-es-un-id"], today.AddDays(-29), ct);

        Assert.Equal(3, views[bata]);
        Assert.Equal(1, views[calculadora]);
        Assert.Equal(2, views.Count);
        Assert.Empty(await stats.ViewsSinceAsync("no-es-un-id", [bata], today, ct));
    }
}
