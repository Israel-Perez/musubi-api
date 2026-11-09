using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.PurchaseRequests.Data;

namespace Musubi.TestShared.PurchaseRequests;

/// <summary>
/// HU-13: comportamiento esperado del repositorio de solicitudes. Corre contra el fake en memoria (UnitTest) y
/// contra MongoDB real con db/ aplicado (DatabaseTest).
/// </summary>
public abstract class WantedRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    internal abstract IWantedRepository Requests { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WantedRequestDocument Request(string requester, DateTime? expiresAt = null, string status = WantedStatuses.Active) => new()
    {
        Id = AuthIds.New(),
        CommunityId = AuthIds.New(),
        RequesterId = requester,
        RequesterName = "Ana R.",
        Title = "Calculadora Casio fx-991ES Plus",
        CategorySlug = "calculadoras",
        CategoryName = "Calculadoras",
        Description = "La necesito para Cálculo Integral.",
        Features = ["Con tapa", "Pantalla sin rayones"],
        BudgetCents = 60_000,
        Quantity = 1,
        NeededBy = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc),
        ExpiresAt = expiresAt ?? Now.AddDays(5),
        Status = status,
        CreatedAt = Now,
    };

    [Fact]
    public async Task Insert_ThenFindById_RoundTripsAllFields()
    {
        var request = Request(AuthIds.New());
        await Requests.InsertAsync(request, Ct);

        var read = (await Requests.FindByIdAsync(request.Id, Ct))!;

        Assert.Equal((request.Title, request.CategorySlug, request.CategoryName, request.RequesterName), (read.Title, read.CategorySlug, read.CategoryName, read.RequesterName));
        Assert.Equal(request.Features, read.Features);
        Assert.Equal((60_000L, 1, request.NeededBy, request.ExpiresAt), (read.BudgetCents, read.Quantity, read.NeededBy, read.ExpiresAt));
        Assert.Equal(WantedStatuses.Active, read.Status);
        Assert.Null(await Requests.FindByIdAsync(AuthIds.New(), Ct));
        Assert.Null(await Requests.FindByIdAsync("no-es-un-id", Ct));
    }

    [Fact]
    public async Task Optionals_CanBeEmpty()
    {
        var bare = new WantedRequestDocument
        {
            Id = AuthIds.New(),
            CommunityId = AuthIds.New(),
            RequesterId = AuthIds.New(),
            RequesterName = "Ana R.",
            Title = "Bata",
            CategorySlug = "otro",
            CategoryName = "Otro",
            BudgetCents = 100,
            ExpiresAt = Now.AddDays(30),
            CreatedAt = Now,
        };
        await Requests.InsertAsync(bare, Ct);

        var read = (await Requests.FindByIdAsync(bare.Id, Ct))!;

        Assert.Null(read.Description);
        Assert.Null(read.NeededBy);
        Assert.Empty(read.Features);
    }

    [Fact]
    public async Task CountActive_LeavesOutExpiredClosedAndOthers()
    {
        var me = AuthIds.New();
        await Requests.InsertAsync(Request(me), Ct);
        await Requests.InsertAsync(Request(me), Ct);
        await Requests.InsertAsync(Request(me, expiresAt: Now.AddMinutes(-1)), Ct);
        await Requests.InsertAsync(Request(me, status: WantedStatuses.Closed), Ct);
        await Requests.InsertAsync(Request(AuthIds.New()), Ct);

        Assert.Equal(2, await Requests.CountActiveAsync(me, Now, Ct));
    }

    [Fact]
    public async Task FindByRequester_IsNewestFirst_OnlyMine_AndLimited()
    {
        var me = AuthIds.New();
        var older = Request(me);
        var newer = Request(me);
        await Requests.InsertAsync(older, Ct);
        await Requests.InsertAsync(newer, Ct);
        await Requests.InsertAsync(Request(AuthIds.New()), Ct);

        Assert.Equal([newer.Id, older.Id], (await Requests.FindByRequesterAsync(me, 10, Ct)).Select(r => r.Id));
        Assert.Equal([newer.Id], (await Requests.FindByRequesterAsync(me, 1, Ct)).Select(r => r.Id));
    }

    [Fact]
    public async Task DeleteByRequester_RemovesOnlyTheirs()
    {
        var leaving = AuthIds.New();
        var other = AuthIds.New();
        await Requests.InsertAsync(Request(leaving), Ct);
        await Requests.InsertAsync(Request(leaving), Ct);
        await Requests.InsertAsync(Request(other), Ct);

        Assert.Equal(2, await Requests.DeleteByRequesterAsync(leaving, Ct));
        Assert.Empty(await Requests.FindByRequesterAsync(leaving, 10, Ct));
        Assert.Single(await Requests.FindByRequesterAsync(other, 10, Ct));
    }
}
