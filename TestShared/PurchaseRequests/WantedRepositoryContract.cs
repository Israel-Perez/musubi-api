using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.PurchaseRequests.Data;

namespace Musubi.TestShared.PurchaseRequests;

/// <summary>
/// HU-13, HU-14 (editar, cerrar y cancelar) y HU-15 (tablero): comportamiento esperado del repositorio de solicitudes. Corre contra el fake en memoria (UnitTest) y
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

    private static WantedRequestDocument OnBoard(
        string community,
        string title,
        string category = "calculadoras",
        long budget = 60_000,
        DateTime? neededBy = null,
        string? description = null,
        string? requester = null,
        DateTime? expiresAt = null,
        string status = WantedStatuses.Active) => new()
    {
        Id = AuthIds.New(),
        CommunityId = community,
        RequesterId = requester ?? AuthIds.New(),
        RequesterName = "Ana R.",
        Title = title,
        CategorySlug = category,
        CategoryName = category,
        Description = description,
        BudgetCents = budget,
        Quantity = 1,
        NeededBy = neededBy,
        ExpiresAt = expiresAt ?? Now.AddDays(20),
        Status = status,
        CreatedAt = Now,
    };

    private static WantedBoardQuery Board(
        string community,
        IReadOnlyCollection<string>? categories = null,
        long? min = null,
        long? max = null,
        DateTime? until = null,
        string? search = null,
        IReadOnlyCollection<string>? exclude = null,
        string? before = null,
        int limit = 20) =>
        new(community, Now, categories, min, max, until, search, exclude, before, limit);

    [Fact]
    public async Task FindBoard_OnlyActiveAndCurrent_FromTheCampus_NewestFirst()
    {
        var campus = AuthIds.New();
        var older = OnBoard(campus, "Bata de laboratorio");
        var expired = OnBoard(campus, "Escuadras", expiresAt: Now.AddMinutes(-1));
        var closed = OnBoard(campus, "Compás", status: WantedStatuses.Closed);
        var elsewhere = OnBoard(AuthIds.New(), "Laptop");
        var newer = OnBoard(campus, "Kit de disección");
        foreach (var r in new[] { older, expired, closed, elsewhere, newer })
        {
            await Requests.InsertAsync(r, Ct);
        }

        var page = await Requests.FindBoardAsync(Board(campus), Ct);

        Assert.Equal([newer.Id, older.Id], page.Select(r => r.Id));
    }

    [Fact]
    public async Task FindBoard_Filters_AndPaginates()
    {
        var campus = AuthIds.New();
        var me = AuthIds.New();
        var bata = OnBoard(campus, "Bata de laboratorio talla M", "laboratorio", 30_000, new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc));
        var laptop = OnBoard(campus, "Laptop para programación", "laptops", 1_200_000, new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc), "Con 16 GB de RAM");
        var fisica = OnBoard(campus, "Física Universitaria", "libros", 40_000);
        var mine = OnBoard(campus, "Calculadora graficadora", requester: me);
        foreach (var r in new[] { bata, laptop, fisica, mine })
        {
            await Requests.InsertAsync(r, Ct);
        }

        async Task<IEnumerable<string>> Ids(WantedBoardQuery query) =>
            (await Requests.FindBoardAsync(query, Ct).ConfigureAwait(false)).Select(r => r.Id);

        Assert.Equal([fisica.Id, bata.Id], await Ids(Board(campus, categories: ["libros", "laboratorio"])));
        Assert.Equal([fisica.Id, bata.Id], await Ids(Board(campus, min: 30_000, max: 40_000)));
        Assert.Equal([laptop.Id, bata.Id], await Ids(Board(campus, until: new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc))));
        Assert.Equal([bata.Id], await Ids(Board(campus, until: new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc))));
        Assert.Equal([fisica.Id, laptop.Id, bata.Id], await Ids(Board(campus, exclude: [me, "no-es-un-id"])));
        // Busqueda: inicio de palabra, sin acentos ni mayusculas, en el producto o la descripcion (OR entre terminos).
        Assert.Equal([fisica.Id], await Ids(Board(campus, search: "fisica")));
        Assert.Equal([laptop.Id], await Ids(Board(campus, search: "ram")));
        Assert.Equal([mine.Id, bata.Id], await Ids(Board(campus, search: "bata calculadora")));
        Assert.Empty(await Ids(Board(campus, search: "boratorio")));
        // Pagina: el cursor es el id de la ultima.
        Assert.Equal([mine.Id, fisica.Id], await Ids(Board(campus, limit: 2)));
        Assert.Equal([laptop.Id, bata.Id], await Ids(Board(campus, before: fisica.Id, limit: 2)));
    }

    [Fact]
    public async Task IncrementOffers_AddsOne_WithoutTouchingTheRest()
    {
        var request = Request(AuthIds.New());
        await Requests.InsertAsync(request, Ct);

        await Requests.IncrementOffersAsync(request.Id, Ct);
        await Requests.IncrementOffersAsync(request.Id, Ct);
        var read = (await Requests.FindByIdAsync(request.Id, Ct))!;

        Assert.Equal((2, request.Title, request.BudgetCents), (read.OffersCount, read.Title, read.BudgetCents));
    }

    private static WantedDetails Changed(DateTime? neededBy = null) => new(
        "Calculadora graficadora",
        "otro",
        "Otro",
        null,
        ["Con funda"],
        90_000,
        2,
        neededBy,
        Now.AddDays(20));

    [Fact]
    public async Task UpdateDetails_ChangesTheFields_AndRemovesTheEmptyOptionals()
    {
        var me = AuthIds.New();
        var request = Request(me);
        await Requests.InsertAsync(request, Ct);

        var updated = (await Requests.UpdateDetailsAsync(request.Id, me, Changed(), requireNoOffers: false, Now, Ct))!;
        var read = (await Requests.FindByIdAsync(request.Id, Ct))!;

        Assert.Equal(("Calculadora graficadora", "otro", "Otro", 90_000L, 2), (read.Title, read.CategorySlug, read.CategoryName, read.BudgetCents, read.Quantity));
        Assert.Equal(["Con funda"], read.Features);
        Assert.Null(read.Description);
        Assert.Null(read.NeededBy);
        Assert.Equal(Now.AddDays(20), read.ExpiresAt);
        Assert.Equal((request.CreatedAt, request.RequesterName, WantedStatuses.Active), (read.CreatedAt, read.RequesterName, read.Status));
        Assert.Equal(read.Title, updated.Title);

        var withDate = (await Requests.UpdateDetailsAsync(request.Id, me, Changed(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc)), false, Now, Ct))!;
        Assert.Equal(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), withDate.NeededBy);
    }

    [Fact]
    public async Task UpdateDetails_OnlyTheOwners_ActiveAndCurrent_AndWithoutOffersIfAsked()
    {
        var me = AuthIds.New();
        var request = Request(me);
        var closed = Request(me, status: WantedStatuses.Closed);
        var expired = Request(me, expiresAt: Now.AddMinutes(-1));
        foreach (var r in new[] { request, closed, expired })
        {
            await Requests.InsertAsync(r, Ct);
        }

        Assert.Null(await Requests.UpdateDetailsAsync(request.Id, AuthIds.New(), Changed(), false, Now, Ct));
        Assert.Null(await Requests.UpdateDetailsAsync(closed.Id, me, Changed(), false, Now, Ct));
        Assert.Null(await Requests.UpdateDetailsAsync(expired.Id, me, Changed(), false, Now, Ct));
        Assert.Null(await Requests.UpdateDetailsAsync("no-es-un-id", me, Changed(), false, Now, Ct));

        await Requests.IncrementOffersAsync(request.Id, Ct);
        Assert.Null(await Requests.UpdateDetailsAsync(request.Id, me, Changed(), requireNoOffers: true, Now, Ct));
        Assert.Equal(request.Title, (await Requests.FindByIdAsync(request.Id, Ct))!.Title);
        var kept = (await Requests.UpdateDetailsAsync(request.Id, me, Changed(), requireNoOffers: false, Now, Ct))!;
        Assert.Equal(1, kept.OffersCount);
    }

    [Fact]
    public async Task SetStatus_ClosesOrCancels_OnlyTheOwnersActiveAndCurrent()
    {
        var me = AuthIds.New();
        var request = Request(me);
        var other = Request(me);
        var expired = Request(me, expiresAt: Now.AddMinutes(-1));
        foreach (var r in new[] { request, other, expired })
        {
            await Requests.InsertAsync(r, Ct);
        }

        Assert.Null(await Requests.SetStatusAsync(request.Id, AuthIds.New(), WantedStatuses.Closed, Now, Ct));
        Assert.Null(await Requests.SetStatusAsync(expired.Id, me, WantedStatuses.Closed, Now, Ct));
        Assert.Null(await Requests.SetStatusAsync("no-es-un-id", me, WantedStatuses.Closed, Now, Ct));

        var closed = (await Requests.SetStatusAsync(request.Id, me, WantedStatuses.Closed, Now, Ct))!;
        Assert.Equal((WantedStatuses.Closed, request.Title, request.ExpiresAt), (closed.Status, closed.Title, closed.ExpiresAt));
        // Ya no esta activa: no cambia otra vez.
        Assert.Null(await Requests.SetStatusAsync(request.Id, me, WantedStatuses.Cancelled, Now, Ct));
        Assert.Equal(WantedStatuses.Cancelled, (await Requests.SetStatusAsync(other.Id, me, WantedStatuses.Cancelled, Now, Ct))!.Status);
        Assert.Equal(0, await Requests.CountActiveAsync(me, Now, Ct));
    }
}
