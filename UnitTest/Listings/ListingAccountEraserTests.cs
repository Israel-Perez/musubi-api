using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Caching;
using Musubi.Api.Platform.Media;
using UnitTest.PurchaseRequests;

namespace UnitTest.Listings;

/// <summary>HU-46 / HU-53: al borrar la cuenta tambien se borran sus favoritos.</summary>
public sealed class ListingAccountEraserTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Erase_RemovesTheUsersFavorites_AndKeepsOtherPeoples()
    {
        var favorites = new InMemoryFavoriteRepository();
        var me = AuthIds.New();
        var other = AuthIds.New();
        await favorites.InsertIfAbsentAsync(Favorite(me), Ct);
        await favorites.InsertIfAbsentAsync(Favorite(other), Ct);
        var cache = new RecordingOutputCacheStore();
        var eraser = new ListingAccountEraser(new InMemoryProductRepository(), favorites, new InMemoryMediaStorage(), cache);

        await eraser.EraseAsync(me, Ct);

        Assert.Empty(await favorites.FindByUserAsync(me, 10, Ct));
        Assert.Single(await favorites.FindByUserAsync(other, 10, Ct));
        Assert.Contains(CachingServiceCollectionExtensions.ShowcaseTag, cache.EvictedTags);
    }

    private static FavoriteDocument Favorite(string userId) => new()
    {
        Id = AuthIds.New(),
        UserId = userId,
        ProductId = AuthIds.New(),
        CommunityId = InMemoryCommunityRepository.UtttId,
        CreatedAt = DateTime.UtcNow,
    };
}
