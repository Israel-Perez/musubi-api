using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Reputation.Data;

namespace Musubi.TestShared.Reputation;

/// <summary>
/// HU-28 y HU-57: comportamiento esperado de los repositorios de reportes y bloqueos. Corre contra los fakes en
/// memoria (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids:
/// la base de Mongo se comparte en la clase.
/// </summary>
public abstract class ReputationRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    internal abstract IReportRepository Reports { get; }

    internal abstract IBlockRepository Blocks { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BlockDocument Block(string blocker, string blocked, int minute = 0) => new()
    {
        Id = AuthIds.New(),
        CommunityId = AuthIds.New(),
        BlockerId = blocker,
        BlockedId = blocked,
        BlockedName = "Diego Hernández",
        CreatedAt = Now.AddMinutes(minute),
    };

    private static ReportDocument Report(string reporter, ReportTarget target, string? details = "Me pidió depositarle antes de vernos.") => new()
    {
        Id = AuthIds.New(),
        CommunityId = AuthIds.New(),
        ReporterId = reporter,
        Target = target,
        Reason = ReportReasons.OffPlatform,
        Details = details,
        CreatedAt = Now,
    };

    [Fact]
    public async Task InsertIfAbsent_TheSamePairTwice_KeepsOneBlock()
    {
        var me = AuthIds.New();
        var other = AuthIds.New();

        var first = await Blocks.InsertIfAbsentAsync(Block(me, other), Ct);
        var again = await Blocks.InsertIfAbsentAsync(Block(me, other), Ct);

        Assert.True(first);
        Assert.False(again);
        Assert.Single(await Blocks.FindByBlockerAsync(me, 10, Ct));
    }

    [Fact]
    public async Task Related_AndExistsBetween_WorkInBothDirections()
    {
        var me = AuthIds.New();
        var iBlocked = AuthIds.New();
        var blockedMe = AuthIds.New();
        var stranger = AuthIds.New();
        await Blocks.InsertIfAbsentAsync(Block(me, iBlocked), Ct);
        await Blocks.InsertIfAbsentAsync(Block(blockedMe, me), Ct);
        await Blocks.InsertIfAbsentAsync(Block(stranger, AuthIds.New()), Ct);

        var related = await Blocks.FindRelatedAsync(me, Ct);

        Assert.Equal(new[] { blockedMe, iBlocked }.Order(StringComparer.Ordinal), related.Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.True(await Blocks.ExistsBetweenAsync(me, iBlocked, Ct));
        Assert.True(await Blocks.ExistsBetweenAsync(iBlocked, me, Ct));
        Assert.True(await Blocks.ExistsBetweenAsync(me, blockedMe, Ct));
        Assert.False(await Blocks.ExistsBetweenAsync(me, stranger, Ct));
        Assert.Empty(await Blocks.FindRelatedAsync(AuthIds.New(), Ct));
    }

    [Fact]
    public async Task FindByBlocker_IsNewestFirst_OnlyMine_AndLimited()
    {
        var me = AuthIds.New();
        var older = Block(me, AuthIds.New(), minute: 1);
        var newer = Block(me, AuthIds.New(), minute: 5);
        await Blocks.InsertIfAbsentAsync(older, Ct);
        await Blocks.InsertIfAbsentAsync(newer, Ct);
        await Blocks.InsertIfAbsentAsync(Block(AuthIds.New(), me), Ct);

        var all = await Blocks.FindByBlockerAsync(me, 10, Ct);
        var one = await Blocks.FindByBlockerAsync(me, 1, Ct);

        Assert.Equal([newer.BlockedId, older.BlockedId], all.Select(b => b.BlockedId));
        Assert.Equal("Diego Hernández", all[0].BlockedName);
        Assert.Equal([newer.BlockedId], one.Select(b => b.BlockedId));
    }

    [Fact]
    public async Task Delete_RemovesOnlyThatDirection_AndIsIdempotent()
    {
        var me = AuthIds.New();
        var other = AuthIds.New();
        await Blocks.InsertIfAbsentAsync(Block(me, other), Ct);
        await Blocks.InsertIfAbsentAsync(Block(other, me), Ct);

        Assert.True(await Blocks.DeleteAsync(me, other, Ct));
        Assert.False(await Blocks.DeleteAsync(me, other, Ct));

        Assert.Empty(await Blocks.FindByBlockerAsync(me, 10, Ct));
        Assert.True(await Blocks.ExistsBetweenAsync(me, other, Ct));
    }

    [Fact]
    public async Task DeleteByUser_RemovesBothDirections_AndNothingElse()
    {
        var leaving = AuthIds.New();
        var other = AuthIds.New();
        await Blocks.InsertIfAbsentAsync(Block(leaving, other), Ct);
        await Blocks.InsertIfAbsentAsync(Block(AuthIds.New(), leaving), Ct);
        await Blocks.InsertIfAbsentAsync(Block(other, AuthIds.New()), Ct);

        Assert.Equal(2, await Blocks.DeleteByUserAsync(leaving, Ct));
        Assert.Empty(await Blocks.FindRelatedAsync(leaving, Ct));
        Assert.Single(await Blocks.FindByBlockerAsync(other, 10, Ct));
    }

    [Fact]
    public async Task Reports_AreSavedWithTheirTarget_AndDeletedByReporter()
    {
        var reporter = AuthIds.New();
        var other = AuthIds.New();
        var user = Report(reporter, new ReportTarget { Type = ReportTargetTypes.User, Id = other });
        var general = Report(reporter, new ReportTarget { Type = ReportTargetTypes.General }, details: null);
        await Reports.InsertAsync(user, Ct);
        await Reports.InsertAsync(general, Ct);
        await Reports.InsertAsync(Report(other, new ReportTarget { Type = ReportTargetTypes.User, Id = reporter }), Ct);

        var mine = await Reports.FindByReporterAsync(reporter, Ct);

        Assert.Equal([general.Id, user.Id], mine.Select(r => r.Id));
        Assert.Equal((ReportTargetTypes.User, other, ReportStatuses.Open), (mine[1].Target.Type, mine[1].Target.Id, mine[1].Status));
        Assert.Null(mine[0].Target.Id);
        Assert.Null(mine[0].Details);
        Assert.Equal(2, await Reports.DeleteByReporterAsync(reporter, Ct));
        Assert.Empty(await Reports.FindByReporterAsync(reporter, Ct));
        Assert.Single(await Reports.FindByReporterAsync(other, Ct));
    }
}
