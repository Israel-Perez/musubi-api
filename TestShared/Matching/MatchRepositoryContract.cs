using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Matching.Data;

namespace Musubi.TestShared.Matching;

/// <summary>
/// RF-25 (HU-18, HU-19, HU-29): comportamiento esperado del registro de coincidencias. Corre contra el fake en memoria
/// (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class MatchRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 14, 18, 0, 0, DateTimeKind.Utc);

    internal abstract IMatchRepository Matches { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MatchRecord Match(string request, string product, string requester, string seller, bool requesterNotified = false, bool sellerNotified = false) =>
        new(AuthIds.New(), request, product, requester, seller, requesterNotified, sellerNotified);

    [Fact]
    public async Task OnePerRequestAndProduct_AndANoticeGivenIsNeverTurnedOff()
    {
        var (request, product, ana, diego) = (AuthIds.New(), AuthIds.New(), AuthIds.New(), AuthIds.New());

        // HU-19 avisa a quien vende; despues HU-29 encuentra la misma pareja sin poder avisar (limite diario).
        await Matches.RecordAsync(Match(request, product, ana, diego, sellerNotified: true), Now, Now.AddDays(90), Ct);
        await Matches.RecordAsync(Match(request, product, ana, diego), Now.AddHours(1), Now.AddDays(91), Ct);
        await Matches.RecordAsync(Match(request, product, ana, diego, requesterNotified: true), Now.AddHours(2), Now.AddDays(92), Ct);

        var found = Assert.Single(await Matches.FindByRequestAsync(request, Ct));
        Assert.Equal((true, true), (found.NotifiedRequester, found.NotifiedSeller));
        Assert.Equal((Now, Now.AddDays(90), product, ana, diego), (found.CreatedAt, found.ExpiresAt, found.ProductId, found.RequesterId, found.SellerId));
        Assert.Null(found.OrderedAt);
    }

    [Fact]
    public async Task Ordered_MarksOnlyThatBuyerWithThatProduct_AndOnlyTheFirstTime()
    {
        var (product, other, ana, luis, diego) = (AuthIds.New(), AuthIds.New(), AuthIds.New(), AuthIds.New(), AuthIds.New());
        var (anaRequest, luisRequest) = (AuthIds.New(), AuthIds.New());
        await Matches.RecordAsync(Match(anaRequest, product, ana, diego), Now, Now.AddDays(90), Ct);
        await Matches.RecordAsync(Match(anaRequest, other, ana, diego), Now, Now.AddDays(90), Ct);
        await Matches.RecordAsync(Match(luisRequest, product, luis, diego), Now, Now.AddDays(90), Ct);

        var marked = await Matches.MarkOrderedAsync(ana, product, Now.AddDays(1), Ct);
        var again = await Matches.MarkOrderedAsync(ana, product, Now.AddDays(2), Ct);

        var anas = await Matches.FindByRequestAsync(anaRequest, Ct);
        Assert.Equal((1, 0), (marked, again));
        Assert.Equal(Now.AddDays(1), anas.Single(m => m.ProductId == product).OrderedAt);
        Assert.Null(anas.Single(m => m.ProductId == other).OrderedAt);
        Assert.Null(Assert.Single(await Matches.FindByRequestAsync(luisRequest, Ct)).OrderedAt);
    }

    [Fact]
    public async Task DeleteByUser_RemovesWhereTheyLookOrSell()
    {
        var (ana, diego, luis) = (AuthIds.New(), AuthIds.New(), AuthIds.New());
        var (asBuyer, asSeller, others) = (AuthIds.New(), AuthIds.New(), AuthIds.New());
        await Matches.RecordAsync(Match(asBuyer, AuthIds.New(), ana, diego), Now, Now.AddDays(90), Ct);
        await Matches.RecordAsync(Match(asSeller, AuthIds.New(), luis, ana), Now, Now.AddDays(90), Ct);
        await Matches.RecordAsync(Match(others, AuthIds.New(), luis, diego), Now, Now.AddDays(90), Ct);

        var deleted = await Matches.DeleteByUserAsync(ana, Ct);

        Assert.Equal(2, deleted);
        Assert.Empty(await Matches.FindByRequestAsync(asBuyer, Ct));
        Assert.Empty(await Matches.FindByRequestAsync(asSeller, Ct));
        Assert.Single(await Matches.FindByRequestAsync(others, Ct));
    }
}
