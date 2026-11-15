using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.TestShared.Listings;

/// <summary>
/// HU-12: comportamiento esperado del repositorio de busquedas guardadas. Corre contra el fake en memoria (UnitTest) y
/// contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class SavedSearchRepositoryContract
{
    // Relativa a hoy: la coleccion tiene TTL y MongoDB borra lo que ya vencio en la fecha real.
    private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(18);

    internal abstract ISavedSearchRepository Searches { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static SavedSearchDocument Search(string user, string community, string key, int days = 30, string query = "calculadora") => new()
    {
        Id = AuthIds.New(),
        UserId = user,
        CommunityId = community,
        Query = query,
        Terms = ["calculadora"],
        Categories = ["calculadoras"],
        MaxPriceCents = 200_000,
        Condition = "used",
        Key = key,
        CreatedAt = Now,
        ExpiresAt = Now.AddDays(days),
    };

    [Fact]
    public async Task TheSameKey_IsOnePerPerson_AndSavingItAgainOnlyRenewsIt()
    {
        var ana = AuthIds.New();
        var campus = AuthIds.New();

        var first = await Searches.SaveAsync(Search(ana, campus, "calculadora||"), Now, Ct);
        var again = await Searches.SaveAsync(Search(ana, campus, "calculadora||", days: 40, query: "Calculadoras"), Now.AddDays(10), Ct);
        var other = await Searches.SaveAsync(Search(AuthIds.New(), campus, "calculadora||"), Now, Ct);

        var mine = await Searches.FindByUserAsync(ana, Now, Ct);
        Assert.Equal((true, false, true), (first.Created, again.Created, other.Created));
        var saved = Assert.Single(mine);
        Assert.Equal((first.Search.Id, "calculadora", Now.AddDays(40)), (saved.Id, saved.Query, saved.ExpiresAt));
        Assert.Equal(["calculadoras"], saved.Categories);
        Assert.Equal(((long?)200_000, "used"), (saved.MaxPriceCents, saved.Condition));
    }

    [Fact]
    public async Task Active_AreOfTheCampus_NotExpired_AndWithoutTheExcluded()
    {
        var campus = AuthIds.New();
        var (ana, luis, diego) = (AuthIds.New(), AuthIds.New(), AuthIds.New());
        var anas = await Searches.SaveAsync(Search(ana, campus, "a"), Now, Ct);
        await Searches.SaveAsync(Search(luis, campus, "b"), Now, Ct);
        await Searches.SaveAsync(Search(diego, campus, "c", days: 1), Now, Ct);
        await Searches.SaveAsync(Search(AuthIds.New(), AuthIds.New(), "d"), Now, Ct);

        var active = await Searches.FindActiveAsync(campus, Now.AddDays(2), [luis], 10, Ct);

        Assert.Equal([anas.Search.Id], active.Select(s => s.Id));
    }

    [Fact]
    public async Task Delete_OnlyMine_AndDeleteByUser_RemovesAll()
    {
        var campus = AuthIds.New();
        var (ana, luis) = (AuthIds.New(), AuthIds.New());
        var one = await Searches.SaveAsync(Search(ana, campus, "a"), Now, Ct);
        await Searches.SaveAsync(Search(ana, campus, "b"), Now, Ct);
        await Searches.SaveAsync(Search(luis, campus, "a"), Now, Ct);

        var notMine = await Searches.DeleteAsync(luis, one.Search.Id, Ct);
        var mine = await Searches.DeleteAsync(ana, one.Search.Id, Ct);
        var all = await Searches.DeleteByUserAsync(ana, Ct);

        Assert.Equal((false, true, 1L), (notMine, mine, all));
        Assert.Empty(await Searches.FindByUserAsync(ana, Now, Ct));
        Assert.Single(await Searches.FindByUserAsync(luis, Now, Ct));
    }
}
