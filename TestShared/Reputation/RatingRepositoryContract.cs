using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Reputation.Data;

namespace Musubi.TestShared.Reputation;

/// <summary>
/// HU-27 y HU-45: comportamiento esperado del repositorio de calificaciones. Corre contra el fake en memoria
/// (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class RatingRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc);

    internal abstract IRatingRepository Ratings { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RatingDocument Rating(string ratedId, string? orderId = null, string? raterId = null, int stars = 5) => new()
    {
        Id = AuthIds.New(),
        CommunityId = AuthIds.New(),
        OrderId = orderId ?? AuthIds.New(),
        RaterId = raterId ?? AuthIds.New(),
        RaterName = "Ana R.",
        RatedId = ratedId,
        RatedRole = RatedRoles.Seller,
        Stars = stars,
        Tags = [RatingTags.OnTime],
        Comment = "Muy puntual.",
        CreatedAt = Now,
    };

    [Fact]
    public async Task TryInsert_OncePerOrderAndRater()
    {
        var rated = AuthIds.New();
        var order = AuthIds.New();
        var rater = AuthIds.New();

        var first = await Ratings.TryInsertAsync(Rating(rated, order, rater), Ct);
        var twice = await Ratings.TryInsertAsync(Rating(rated, order, rater, stars: 1), Ct);
        var otherSide = await Ratings.TryInsertAsync(Rating(rater, order, rated), Ct);

        Assert.True(first);
        Assert.False(twice);
        Assert.True(otherSide);
        Assert.Equal(5, Assert.Single(await Ratings.FindByRatedAsync(rated, null, 10, Ct)).Stars);
    }

    [Fact]
    public async Task FindByRated_NewestFirst_WithACursor()
    {
        var rated = AuthIds.New();
        var first = Rating(rated);
        var second = Rating(rated);
        var third = Rating(rated);
        foreach (var r in new[] { first, second, third, Rating(AuthIds.New()) })
        {
            await Ratings.TryInsertAsync(r, Ct);
        }

        var page = await Ratings.FindByRatedAsync(rated, null, 2, Ct);
        var rest = await Ratings.FindByRatedAsync(rated, page[^1].Id, 2, Ct);

        Assert.Equal([third.Id, second.Id], page.Select(r => r.Id));
        Assert.Equal([first.Id], rest.Select(r => r.Id));
        Assert.Equal("Muy puntual.", page[0].Comment);
        Assert.Equal([RatingTags.OnTime], page[0].Tags);
        Assert.Empty(await Ratings.FindByRatedAsync("no-es-un-id", null, 10, Ct));
    }

    [Fact]
    public async Task ReleaseAccount_DeletesWhatItReceived_AndKeepsWhatItGaveWithTheReplacementName()
    {
        var leaving = AuthIds.New();
        var other = AuthIds.New();
        await Ratings.TryInsertAsync(Rating(leaving), Ct);
        await Ratings.TryInsertAsync(Rating(other, raterId: leaving), Ct);

        await Ratings.ReleaseAccountAsync(leaving, "Usuario eliminado", Ct);

        Assert.Empty(await Ratings.FindByRatedAsync(leaving, null, 10, Ct));
        Assert.Equal("Usuario eliminado", Assert.Single(await Ratings.FindByRatedAsync(other, null, 10, Ct)).RaterName);
    }
}
