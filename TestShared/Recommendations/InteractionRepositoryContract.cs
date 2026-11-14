using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Recommendations;
using Musubi.Api.Modules.Recommendations.Data;

namespace Musubi.TestShared.Recommendations;

/// <summary>
/// HU-35: comportamiento esperado del repositorio de interacciones. Corre contra el fake en memoria (UnitTest) y contra
/// MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class InteractionRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 14, 18, 0, 0, DateTimeKind.Utc);

    internal abstract IInteractionRepository Interactions { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Record_IsOnePerPersonProductAndKind_AndRepeatingItUpdatesAndCounts()
    {
        var ana = AuthIds.New();
        var campus = AuthIds.New();
        var product = AuthIds.New();

        await Interactions.RecordAsync(ana, campus, product, InteractionKinds.View, Now, Now.AddDays(90), Ct);
        await Interactions.RecordAsync(ana, campus, product, InteractionKinds.View, Now.AddHours(1), Now.AddDays(91), Ct);
        await Interactions.RecordAsync(ana, campus, product, InteractionKinds.Favorite, Now.AddHours(2), Now.AddDays(92), Ct);

        var mine = await Interactions.FindByUserAsync(ana, 10, Ct);

        Assert.Equal([InteractionKinds.Favorite, InteractionKinds.View], mine.Select(i => i.Kind));
        Assert.Equal((2, Now.AddHours(1), Now.AddDays(91)), (mine[1].Count, mine[1].At, mine[1].ExpiresAt));
        Assert.Equal((campus, product), (mine[1].CommunityId, mine[1].ProductId));
    }

    [Fact]
    public async Task ByCommunity_IsSinceAMoment_NewestFirst_AndOnlyThatCampus()
    {
        var campus = AuthIds.New();
        var ana = AuthIds.New();
        var luis = AuthIds.New();
        var old = AuthIds.New();
        var recent = AuthIds.New();
        await Interactions.RecordAsync(ana, campus, old, InteractionKinds.View, Now.AddDays(-10), Now.AddDays(80), Ct);
        await Interactions.RecordAsync(luis, campus, recent, InteractionKinds.Chat, Now, Now.AddDays(90), Ct);
        await Interactions.RecordAsync(ana, AuthIds.New(), AuthIds.New(), InteractionKinds.View, Now, Now.AddDays(90), Ct);

        var all = await Interactions.FindByCommunityAsync(campus, Now.AddDays(-30), 10, Ct);
        var lastWeek = await Interactions.FindByCommunityAsync(campus, Now.AddDays(-7), 10, Ct);

        Assert.Equal([recent, old], all.Select(i => i.ProductId));
        Assert.Equal([recent], lastWeek.Select(i => i.ProductId));
        Assert.Empty(await Interactions.FindByCommunityAsync("no-es-un-id", Now.AddDays(-30), 10, Ct));
    }

    [Fact]
    public async Task DeleteByUser_OnlyErasesThatPerson()
    {
        var campus = AuthIds.New();
        var ana = AuthIds.New();
        var luis = AuthIds.New();
        await Interactions.RecordAsync(ana, campus, AuthIds.New(), InteractionKinds.View, Now, Now.AddDays(90), Ct);
        await Interactions.RecordAsync(luis, campus, AuthIds.New(), InteractionKinds.View, Now, Now.AddDays(90), Ct);

        Assert.Equal(1, await Interactions.DeleteByUserAsync(ana, Ct));

        Assert.Empty(await Interactions.FindByUserAsync(ana, 10, Ct));
        Assert.Single(await Interactions.FindByUserAsync(luis, 10, Ct));
    }
}
