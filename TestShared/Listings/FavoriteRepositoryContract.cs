using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.TestShared.Listings;

/// <summary>
/// HU-53: comportamiento esperado del repositorio de favoritos. Corre contra el fake en memoria (UnitTest) y
/// contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class FavoriteRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    internal abstract IFavoriteRepository Favorites { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static FavoriteDocument Favorite(string userId, string productId, int minute = 0) => new()
    {
        Id = AuthIds.New(),
        UserId = userId,
        ProductId = productId,
        CommunityId = AuthIds.New(),
        CreatedAt = Now.AddMinutes(minute),
    };

    [Fact]
    public async Task InsertIfAbsent_TheSamePairTwice_KeepsOneFavorite()
    {
        var user = AuthIds.New();
        var product = AuthIds.New();

        var first = await Favorites.InsertIfAbsentAsync(Favorite(user, product), Ct);
        var again = await Favorites.InsertIfAbsentAsync(Favorite(user, product), Ct);

        Assert.True(first);
        Assert.False(again);
        Assert.Single(await Favorites.FindByUserAsync(user, 10, Ct));
    }

    [Fact]
    public async Task FindByUser_IsNewestFirst_OnlyMine_AndLimited()
    {
        var me = AuthIds.New();
        var older = Favorite(me, AuthIds.New(), minute: 1);
        var newer = Favorite(me, AuthIds.New(), minute: 5);
        await Favorites.InsertIfAbsentAsync(older, Ct);
        await Favorites.InsertIfAbsentAsync(newer, Ct);
        await Favorites.InsertIfAbsentAsync(Favorite(AuthIds.New(), AuthIds.New()), Ct);

        var all = await Favorites.FindByUserAsync(me, 10, Ct);
        var one = await Favorites.FindByUserAsync(me, 1, Ct);

        Assert.Equal([newer.ProductId, older.ProductId], all.Select(f => f.ProductId));
        Assert.Equal([newer.ProductId], one.Select(f => f.ProductId));
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatPair_AndIsIdempotent()
    {
        var me = AuthIds.New();
        var other = AuthIds.New();
        var product = AuthIds.New();
        await Favorites.InsertIfAbsentAsync(Favorite(me, product), Ct);
        await Favorites.InsertIfAbsentAsync(Favorite(other, product), Ct);

        Assert.True(await Favorites.DeleteAsync(me, product, Ct));
        Assert.False(await Favorites.DeleteAsync(me, product, Ct));

        Assert.Empty(await Favorites.FindByUserAsync(me, 10, Ct));
        Assert.Single(await Favorites.FindByUserAsync(other, 10, Ct));
    }

    [Fact]
    public async Task Delete_WithAnInvalidProductId_IsIdempotent()
    {
        Assert.False(await Favorites.DeleteAsync(AuthIds.New(), "no-es-un-id", Ct));
    }

    [Fact]
    public async Task DeleteByUser_RemovesOnlyThatUsersFavorites()
    {
        var leaving = AuthIds.New();
        var other = AuthIds.New();
        await Favorites.InsertIfAbsentAsync(Favorite(leaving, AuthIds.New()), Ct);
        await Favorites.InsertIfAbsentAsync(Favorite(leaving, AuthIds.New()), Ct);
        await Favorites.InsertIfAbsentAsync(Favorite(other, AuthIds.New()), Ct);

        Assert.Equal(2, await Favorites.DeleteByUserAsync(leaving, Ct));
        Assert.Empty(await Favorites.FindByUserAsync(leaving, 10, Ct));
        Assert.Single(await Favorites.FindByUserAsync(other, 10, Ct));
    }
}
