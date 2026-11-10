using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Moderation;
using Musubi.TestShared;

namespace UnitTest.PurchaseRequests;

/// <summary>
/// HU-13: RN-09 (obligatorios), limites de cada campo, la fecha en el dia del campus, RN-10 (vencimiento), RN-12
/// (maximo de activas), 02-07 (moderacion) y quien ve una solicitud (campus, activa, bloqueo). HU-15: el tablero
/// "Se busca" (filtros, busqueda, pagina, bloqueos, lo que ve un visitante y la cache). HU-14: editar con las
/// reglas de publicar (con ofertas el producto no cambia; solo se modera lo que cambio; vencimiento nuevo), cerrar y
/// cancelar (solo activas, idempotente) y solo quien la publico (404 para los demas).
/// </summary>
public sealed class WantedServiceTests
{
    private const string OtherCommunity = "000000000000000000000002";

    // Domingo 8 de noviembre de 2026, 23:30 en el campus (UTC-6): ya es lunes 9 en UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 5, 30, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryWantedRepository requests = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly InMemoryProfileRepository profiles = new();
    private readonly RecordingOutputCacheStore outputCache = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly WantedService service;

    public WantedServiceTests()
    {
        service = new WantedService(
            requests,
            new MemberDirectory(new InMemoryCommunityRepository(), users),
            new CategoryDirectory(new InMemoryCategoryRepository()),
            new BlockDirectory(blocks),
            new ProfileDirectory(profiles, new InMemoryCareerRepository()),
            new InMemoryContentModerator(),
            outputCache,
            Options.Create(new CatalogOptions()),
            time);
    }

    private static ShowcaseViewer Viewer(string userId) => new(userId, InMemoryCommunityRepository.UtttId);

    private static readonly ShowcaseViewer Visitor = new(null, null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateWantedRequest Valid(
        string title = "Calculadora Casio fx-991ES Plus",
        string category = "calculadoras",
        string? description = "La necesito para Cálculo Integral.",
        IReadOnlyList<string>? features = null,
        long? budget = 60_000,
        int? quantity = 1,
        DateOnly? neededBy = null) =>
        new(title, category, description, features ?? ["Con tapa"], budget, quantity, neededBy);

    private async Task<string> MemberAsync(string communityId = InMemoryCommunityRepository.UtttId, string name = "Ana Sofía Ramírez")
    {
        var id = AuthIds.New();
        Assert.True(await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = communityId,
                Email = $"ana.{id}@uttt.edu.mx",
                EmailNormalized = $"ana.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = name,
                Status = UserStatuses.Active,
                EmailVerifiedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 10, 1, 8, 55, 0, DateTimeKind.Utc),
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    [Fact]
    public async Task Create_SavesAnActiveRequest_WithTheCategoryName_AndTheShortName()
    {
        var me = await MemberAsync();

        var created = (await service.CreateAsync(me, Valid(features: ["  Con tapa ", "Modelo fx-991ES Plus"]), Ct)).Value!;

        Assert.Equal(("active", "Calculadoras", "Ana R.", 0), (created.Status, created.Category.Name, created.Requester.Name, created.OffersCount));
        Assert.Equal(["Con tapa", "Modelo fx-991ES Plus"], created.Features);
        // RN-10 sin fecha: 30 dias despues.
        Assert.Equal(Now.UtcDateTime.AddDays(30), created.ExpiresAt);
        Assert.Single(await requests.FindByRequesterAsync(me, 10, Ct));
    }

    [Fact]
    public async Task Create_WithADate_ExpiresAtTheEndOfThatDayOnCampus()
    {
        var me = await MemberAsync();

        var created = (await service.CreateAsync(me, Valid(neededBy: new DateOnly(2026, 11, 13)), Ct)).Value!;

        Assert.Equal(new DateOnly(2026, 11, 13), created.NeededBy);
        Assert.Equal(new DateTime(2026, 11, 14, 6, 0, 0, DateTimeKind.Utc), created.ExpiresAt);
    }

    [Fact]
    public async Task Create_TodayIsTheCampusDay_NotTheUtcDay()
    {
        var me = await MemberAsync();

        // Ya es 9 en UTC, pero en el campus sigue siendo domingo 8: hoy vale y ayer no.
        Assert.Null((await service.CreateAsync(me, Valid(neededBy: new DateOnly(2026, 11, 8)), Ct)).Error);
        Assert.Equal(WantedErrors.InvalidDate, (await service.CreateAsync(me, Valid(neededBy: new DateOnly(2026, 11, 7)), Ct)).Error);
        Assert.Null((await service.CreateAsync(me, Valid(neededBy: new DateOnly(2027, 1, 7)), Ct)).Error);
        Assert.Equal(WantedErrors.InvalidDate, (await service.CreateAsync(me, Valid(neededBy: new DateOnly(2027, 1, 8)), Ct)).Error);
    }

    [Fact]
    public async Task Create_Optionals_CanBeLeftOut_AndOtherIsAValidCategory()
    {
        var me = await MemberAsync();

        var created = (await service.CreateAsync(me, Valid(category: "otro", description: "   ", features: []), Ct)).Value!;

        Assert.Equal(("otro", "Otro"), (created.Category.Slug, created.Category.Name));
        Assert.Null(created.Description);
        Assert.Empty(created.Features);
        Assert.Null(created.NeededBy);
    }

    public static TheoryData<CreateWantedRequest, string> Invalid() => new()
    {
        { Valid(title: "  "), "wanted.invalid_title" },
        { Valid(title: "ab"), "wanted.invalid_title" },
        { Valid(title: new string('a', 81)), "wanted.invalid_title" },
        { Valid(category: ""), "wanted.invalid_category" },
        { Valid(category: "naves-espaciales"), "wanted.invalid_category" },
        { Valid(description: new string('a', 301)), "wanted.invalid_description" },
        { Valid(features: ["a", "b", "c", "d", "e", "f"]), "wanted.invalid_features" },
        { Valid(features: [" "]), "wanted.invalid_features" },
        { Valid(features: [new string('a', 41)]), "wanted.invalid_features" },
        { Valid(budget: null), "wanted.invalid_budget" },
        { Valid(budget: 99), "wanted.invalid_budget" },
        { Valid(budget: 100_000_000), "wanted.invalid_budget" },
        { Valid(quantity: null), "wanted.invalid_quantity" },
        { Valid(quantity: 0), "wanted.invalid_quantity" },
        { Valid(quantity: 11), "wanted.invalid_quantity" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Create_WithAMissingOrInvalidField_IsRejected_AndNothingIsSaved(CreateWantedRequest request, string code)
    {
        var me = await MemberAsync();

        var result = await service.CreateAsync(me, request, Ct);

        Assert.Equal(code, result.Error?.Code);
        Assert.Empty(await requests.FindByRequesterAsync(me, 10, Ct));
    }

    [Fact]
    public async Task Create_MoreThanFiveActive_IsRejected_ButExpiredOnesDoNotCount()
    {
        var me = await MemberAsync();
        await service.CreateAsync(me, Valid(neededBy: new DateOnly(2026, 11, 8)), Ct);
        for (var i = 0; i < 4; i++)
        {
            Assert.Null((await service.CreateAsync(me, Valid(), Ct)).Error);
        }

        Assert.Equal(WantedErrors.TooManyActive, (await service.CreateAsync(me, Valid(), Ct)).Error);

        // La de hoy vence al terminar el dia del campus: luego cabe otra.
        time.Advance(TimeSpan.FromHours(1));
        Assert.Null((await service.CreateAsync(me, Valid(), Ct)).Error);
    }

    [Theory]
    [InlineData("Calculadora [[inapropiado]]", null, null, "el producto que buscas")]
    [InlineData("Calculadora", "Que [[inapropiado]]", null, "la descripción")]
    [InlineData("Calculadora", null, "Con [[inapropiado]]", "las características")]
    public async Task Create_WithRejectedText_NamesTheField_AndSavesNothing(string title, string? description, string? feature, string field)
    {
        var me = await MemberAsync();

        var result = await service.CreateAsync(me, Valid(title: title, description: description, features: feature is null ? [] : [feature]), Ct);

        Assert.Equal("moderation.text_rejected", result.Error?.Code);
        Assert.Contains(field, result.Error!.Message, StringComparison.Ordinal);
        Assert.Empty(await requests.FindByRequesterAsync(me, 10, Ct));
    }

    [Fact]
    public async Task Mine_ShowsExpiredOnesAsExpired_NewestFirst()
    {
        var me = await MemberAsync();
        var today = (await service.CreateAsync(me, Valid(neededBy: new DateOnly(2026, 11, 8)), Ct)).Value!;
        var later = (await service.CreateAsync(me, Valid(), Ct)).Value!;
        time.Advance(TimeSpan.FromHours(1));

        var mine = (await service.MineAsync(me, Ct)).Items;

        Assert.Equal([(later.Id, "active"), (today.Id, "expired")], mine.Select(r => (r.Id, r.Status)));
    }

    [Fact]
    public async Task Get_TheOwnerAlwaysSeesIt_OthersOnlyIfActiveSameCampusAndNotBlocked()
    {
        var me = await MemberAsync();
        var neighbor = await MemberAsync();
        var blocked = await MemberAsync();
        var elsewhere = await MemberAsync(OtherCommunity);
        var created = (await service.CreateAsync(me, Valid(neededBy: new DateOnly(2026, 11, 8)), Ct)).Value!;
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), CommunityId = InMemoryCommunityRepository.UtttId, BlockerId = me, BlockedId = blocked, BlockedName = "Diego" }, Ct);

        Assert.Null((await service.GetAsync(neighbor, InMemoryCommunityRepository.UtttId, created.Id, Ct)).Error);
        Assert.Equal(WantedErrors.NotFound, (await service.GetAsync(blocked, InMemoryCommunityRepository.UtttId, created.Id, Ct)).Error);
        Assert.Equal(WantedErrors.NotFound, (await service.GetAsync(elsewhere, OtherCommunity, created.Id, Ct)).Error);
        Assert.Equal(WantedErrors.NotFound, (await service.GetAsync(neighbor, InMemoryCommunityRepository.UtttId, "no-es-un-id", Ct)).Error);

        time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(WantedErrors.NotFound, (await service.GetAsync(neighbor, InMemoryCommunityRepository.UtttId, created.Id, Ct)).Error);
        Assert.Equal("expired", (await service.GetAsync(me, InMemoryCommunityRepository.UtttId, created.Id, Ct)).Value!.Status);
    }

    [Fact]
    public async Task Create_EvictsTheVisitorBoardFromTheOutputCache()
    {
        var me = await MemberAsync();

        await service.CreateAsync(me, Valid(), Ct);

        Assert.Equal(["wanted-board"], outputCache.EvictedTags);
    }

    [Fact]
    public async Task Board_ShowsActiveOnesFromMyCampus_NewestFirst_WithoutMineExpiredOrBlocked()
    {
        var me = await MemberAsync();
        var ana = await MemberAsync(name: "Ana Sofía Ramírez");
        var diego = await MemberAsync(name: "Diego Hernández");
        var elsewhere = await MemberAsync(OtherCommunity);
        var mine = (await service.CreateAsync(me, Valid(), Ct)).Value!;
        var old = (await service.CreateAsync(ana, Valid(title: "Bata de laboratorio"), Ct)).Value!;
        var expiring = (await service.CreateAsync(ana, Valid(title: "Escuadras", neededBy: new DateOnly(2026, 11, 8)), Ct)).Value!;
        var fresh = (await service.CreateAsync(ana, Valid(title: "Kit de disección"), Ct)).Value!;
        var fromBlocked = (await service.CreateAsync(diego, Valid(title: "Laptop"), Ct)).Value!;
        var otherCampus = (await service.CreateAsync(elsewhere, Valid(category: "otro"), Ct)).Value!;
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), CommunityId = InMemoryCommunityRepository.UtttId, BlockerId = diego, BlockedId = me, BlockedName = "Yo" }, Ct);

        var before = (await service.BoardAsync(Viewer(me), new WantedBoardFilter(), Ct)).Value!;
        time.Advance(TimeSpan.FromHours(1));
        var after = (await service.BoardAsync(Viewer(me), new WantedBoardFilter(), Ct)).Value!;

        Assert.Equal([fresh.Id, expiring.Id, old.Id], before.Items.Select(r => r.Id));
        // RN-10: al terminar su dia en el campus ya no sale.
        Assert.Equal([fresh.Id, old.Id], after.Items.Select(r => r.Id));
        Assert.DoesNotContain(mine.Id, after.Items.Select(r => r.Id));
        Assert.DoesNotContain(fromBlocked.Id, after.Items.Select(r => r.Id));
        Assert.DoesNotContain(otherCampus.Id, after.Items.Select(r => r.Id));
        Assert.Null(after.NextCursor);
    }

    [Fact]
    public async Task Board_SignedIn_ShowsTheFullName_ThePhoto_AndTheCareerOnlyIfVisible()
    {
        var me = await MemberAsync();
        var fernanda = await MemberAsync(name: "Fernanda Ortiz Salinas");
        var mariana = await MemberAsync(name: "Mariana López");
        var luis = await MemberAsync(name: "Luis Martínez");
        await profiles.SetDetailsAsync(new ProfileOwner(fernanda, InMemoryCommunityRepository.UtttId, "Fernanda Ortiz Salinas"), "ing-quimica", 8, Now.UtcDateTime, Ct);
        await profiles.SetAvatarAsync(new ProfileOwner(fernanda, InMemoryCommunityRepository.UtttId, "Fernanda Ortiz Salinas"), "media/avatars/f.webp", Now.UtcDateTime, Ct);
        var hidden = new ProfileOwner(mariana, InMemoryCommunityRepository.UtttId, "Mariana López");
        await profiles.SetDetailsAsync(hidden, "ing-software", 9, Now.UtcDateTime, Ct);
        await profiles.SetVisibilityAsync(hidden, new ProfileVisibility { Career = false }, Now.UtcDateTime, Ct);
        await service.CreateAsync(fernanda, Valid(title: "Bata de laboratorio"), Ct);
        await service.CreateAsync(mariana, Valid(title: "Laptop"), Ct);
        await service.CreateAsync(luis, Valid(title: "Física Universitaria"), Ct);

        var people = (await service.BoardAsync(Viewer(me), new WantedBoardFilter(), Ct)).Value!.Items
            .Select(r => (r.Requester.Id, r.Requester.Name, r.Requester.Photo, r.Requester.Career))
            .ToList();

        Assert.Equal(
            [
                (luis, "Luis Martínez", null, null),
                (mariana, "Mariana López", null, null),
                (fernanda, "Fernanda Ortiz Salinas", "/media/avatars/f.webp", "Ing. Química"),
            ],
            people);
    }

    [Fact]
    public async Task Board_Visitor_SeesTheDefaultCampus_WithTheShortNameOnly()
    {
        var fernanda = await MemberAsync(name: "Fernanda Ortiz");
        await profiles.SetDetailsAsync(new ProfileOwner(fernanda, InMemoryCommunityRepository.UtttId, "Fernanda Ortiz"), "ing-quimica", 8, Now.UtcDateTime, Ct);
        await service.CreateAsync(fernanda, Valid(), Ct);

        var item = Assert.Single((await service.BoardAsync(Visitor, new WantedBoardFilter(), Ct)).Value!.Items);
        var byCampus = Assert.Single((await service.BoardAsync(Visitor, new WantedBoardFilter(Campus: " UTTT "), Ct)).Value!.Items);

        // RNF-05: sin id, foto ni carrera.
        Assert.Equal(new WantedRequesterResponse(null, "Fernanda O."), item.Requester);
        Assert.Equal(item.Id, byCampus.Id);
        Assert.Equal("listings.unknown_campus", (await service.BoardAsync(Visitor, new WantedBoardFilter(Campus: "unam"), Ct)).Error?.Code);
    }

    [Fact]
    public async Task Board_FiltersByCategory_Budget_Deadline_AndWords()
    {
        var me = await MemberAsync();
        var ana = await MemberAsync();
        // Hoy en el campus es domingo 8 de noviembre.
        var bata = (await service.CreateAsync(ana, Valid(title: "Bata de laboratorio talla M", category: "laboratorio", budget: 30_000, neededBy: new DateOnly(2026, 11, 9)), Ct)).Value!;
        var laptop = (await service.CreateAsync(ana, Valid(title: "Laptop para programación", category: "laptops", description: "Con 16 GB de RAM", budget: 1_200_000, neededBy: new DateOnly(2026, 11, 15)), Ct)).Value!;
        var fisica = (await service.CreateAsync(ana, Valid(title: "Física Universitaria", category: "libros", description: null, budget: 40_000), Ct)).Value!;
        var graf = (await service.CreateAsync(ana, Valid(title: "Calculadora graficadora", category: "calculadoras", budget: 150_000, neededBy: new DateOnly(2026, 11, 11)), Ct)).Value!;

        async Task<IEnumerable<string>> Ids(WantedBoardFilter filter) =>
            (await service.BoardAsync(Viewer(me), filter, Ct).ConfigureAwait(false)).Value!.Items.Select(r => r.Id);

        Assert.Equal([fisica.Id, bata.Id], await Ids(new WantedBoardFilter(Category: ["libros", "laboratorio", " "])));
        // "Hasta $300", "$300 – $1,000" y "Mas de $1,000" de la maqueta (inclusivos).
        Assert.Equal([bata.Id], await Ids(new WantedBoardFilter(MaxBudgetCents: 30_000)));
        Assert.Equal([fisica.Id, bata.Id], await Ids(new WantedBoardFilter(MinBudgetCents: 30_000, MaxBudgetCents: 100_000)));
        Assert.Equal([graf.Id, laptop.Id], await Ids(new WantedBoardFilter(MinBudgetCents: 100_001)));
        // "En 3 dias o menos" y "Esta semana": sin fecha no sale.
        Assert.Equal([graf.Id, bata.Id], await Ids(new WantedBoardFilter(NeededWithinDays: 3)));
        Assert.Equal([graf.Id, laptop.Id, bata.Id], await Ids(new WantedBoardFilter(NeededWithinDays: 7)));
        // Inicio de palabra, sin acentos ni mayusculas, en el producto o la descripcion; un termino basta (OR).
        Assert.Equal([fisica.Id], await Ids(new WantedBoardFilter(Q: "FISICA")));
        Assert.Equal([laptop.Id], await Ids(new WantedBoardFilter(Q: "ram")));
        Assert.Equal([graf.Id, bata.Id], await Ids(new WantedBoardFilter(Q: "batas calculadoras")));
        Assert.Empty(await Ids(new WantedBoardFilter(Q: "atoria")));
        Assert.Equal([bata.Id], await Ids(new WantedBoardFilter(Category: ["laboratorio"], Q: "bata", NeededWithinDays: 1)));
    }

    [Fact]
    public async Task Board_Paginates_WithTheIdOfTheLastOne()
    {
        var me = await MemberAsync();
        var ids = new List<string>();
        foreach (var name in new[] { "Ana", "Diego", "Luis" })
        {
            var who = await MemberAsync(name: name + " Pérez");
            ids.Add((await service.CreateAsync(who, Valid(), Ct)).Value!.Id);
        }

        var first = (await service.BoardAsync(Viewer(me), new WantedBoardFilter(Limit: 2), Ct)).Value!;
        var second = (await service.BoardAsync(Viewer(me), new WantedBoardFilter(Limit: 2, Cursor: first.NextCursor), Ct)).Value!;

        Assert.Equal([ids[2], ids[1]], first.Items.Select(r => r.Id));
        Assert.Equal(ids[1], first.NextCursor);
        Assert.Equal([ids[0]], second.Items.Select(r => r.Id));
        Assert.Null(second.NextCursor);
    }

    public static TheoryData<WantedBoardFilter, string> InvalidBoard() => new()
    {
        { new WantedBoardFilter(MinBudgetCents: -1), "wanted.invalid_budget_range" },
        { new WantedBoardFilter(MaxBudgetCents: 100_000_000), "wanted.invalid_budget_range" },
        { new WantedBoardFilter(MinBudgetCents: 50_000, MaxBudgetCents: 40_000), "wanted.invalid_budget_range" },
        { new WantedBoardFilter(NeededWithinDays: 0), "wanted.invalid_deadline" },
        { new WantedBoardFilter(NeededWithinDays: 61), "wanted.invalid_deadline" },
        { new WantedBoardFilter(Q: "¿¡--!?"), "wanted.invalid_query" },
        { new WantedBoardFilter(Q: new string('a', 81)), "wanted.invalid_query" },
        { new WantedBoardFilter(Cursor: "no-es-un-cursor"), "wanted.invalid_cursor" },
    };

    [Theory]
    [MemberData(nameof(InvalidBoard))]
    public async Task Board_RejectsInvalidFilters(WantedBoardFilter filter, string code)
    {
        var me = await MemberAsync();

        Assert.Equal(code, (await service.BoardAsync(Viewer(me), filter, Ct)).Error?.Code);
    }

    [Fact]
    public async Task Board_RegexCharactersInTheQuery_AreJustIgnored()
    {
        var me = await MemberAsync();
        var ana = await MemberAsync();
        var created = (await service.CreateAsync(ana, Valid(title: "Calculadora (científica)"), Ct)).Value!;

        var found = (await service.BoardAsync(Viewer(me), new WantedBoardFilter(Q: "(cient.*"), Ct)).Value!;

        Assert.Equal([created.Id], found.Items.Select(r => r.Id));
    }

    private static UpdateWantedRequest Edit(
        string title = "Calculadora Casio fx-991ES Plus",
        string category = "calculadoras",
        string? description = "La necesito para Cálculo Integral.",
        IReadOnlyList<string>? features = null,
        long? budget = 60_000,
        int? quantity = 1,
        DateOnly? neededBy = null) =>
        new(title, category, description, features ?? ["Con tapa"], budget, quantity, neededBy);

    [Fact]
    public async Task Update_ChangesEveryField_AndTheExpiryFollowsTheNewDate()
    {
        var me = await MemberAsync();
        var created = (await service.CreateAsync(me, Valid(neededBy: new DateOnly(2026, 11, 13)), Ct)).Value!;
        outputCache.EvictedTags.Clear();
        time.Advance(TimeSpan.FromDays(2));

        var updated = (await service.UpdateAsync(
            me,
            created.Id,
            Edit(title: "  Calculadora graficadora ", category: "otro", description: "  ", features: [], budget: 120_000, quantity: 2),
            Ct)).Value!;

        Assert.Equal(("Calculadora graficadora", "otro", "Otro", 120_000L, 2), (updated.Title, updated.Category.Slug, updated.Category.Name, updated.BudgetCents, updated.Quantity));
        Assert.Null(updated.Description);
        Assert.Empty(updated.Features);
        Assert.Null(updated.NeededBy);
        // RN-10: sin fecha vence a los 30 dias de publicarse, no de editarse.
        Assert.Equal(created.CreatedAt.AddDays(30), updated.ExpiresAt);
        Assert.Equal(("active", created.CreatedAt), (updated.Status, updated.CreatedAt));
        Assert.Equal(["wanted-board"], outputCache.EvictedTags);

        var withDate = (await service.UpdateAsync(me, created.Id, Edit(neededBy: new DateOnly(2026, 11, 20)), Ct)).Value!;
        Assert.Equal(new DateTime(2026, 11, 21, 6, 0, 0, DateTimeKind.Utc), withDate.ExpiresAt);
        Assert.Equal("La necesito para Cálculo Integral.", withDate.Description);
    }

    [Fact]
    public async Task Update_UsesThePublishRules()
    {
        var me = await MemberAsync();
        var created = (await service.CreateAsync(me, Valid(), Ct)).Value!;

        Assert.Equal(WantedErrors.InvalidTitle, (await service.UpdateAsync(me, created.Id, Edit(title: "ab"), Ct)).Error);
        Assert.Equal(WantedErrors.InvalidCategory, (await service.UpdateAsync(me, created.Id, Edit(category: "no-existe"), Ct)).Error);
        Assert.Equal(WantedErrors.InvalidBudget, (await service.UpdateAsync(me, created.Id, Edit(budget: 50), Ct)).Error);
        Assert.Equal(WantedErrors.InvalidQuantity, (await service.UpdateAsync(me, created.Id, Edit(quantity: 11), Ct)).Error);
        Assert.Equal(WantedErrors.InvalidFeatures, (await service.UpdateAsync(me, created.Id, Edit(features: ["a", "b", "c", "d", "e", "f"]), Ct)).Error);
        Assert.Equal(WantedErrors.InvalidDate, (await service.UpdateAsync(me, created.Id, Edit(neededBy: new DateOnly(2026, 11, 7)), Ct)).Error);
        Assert.Equal(created.Title, (await service.GetAsync(me, InMemoryCommunityRepository.UtttId, created.Id, Ct)).Value!.Title);
    }

    [Fact]
    public async Task Update_WithOffers_TheProductAndTheCategoryStay_TheRestChanges()
    {
        var me = await MemberAsync();
        var created = (await service.CreateAsync(me, Valid(), Ct)).Value!;
        await requests.IncrementOffersAsync(created.Id, Ct);

        Assert.Equal(WantedErrors.HasOffers, (await service.UpdateAsync(me, created.Id, Edit(title: "Laptop Dell"), Ct)).Error);
        Assert.Equal(WantedErrors.HasOffers, (await service.UpdateAsync(me, created.Id, Edit(category: "otro"), Ct)).Error);

        var updated = (await service.UpdateAsync(me, created.Id, Edit(budget: 70_000, description: "Con pilas"), Ct)).Value!;
        Assert.Equal((70_000L, "Con pilas", 1), (updated.BudgetCents, updated.Description, updated.OffersCount));
    }

    [Fact]
    public async Task Update_OnlyModeratesTheTextThatChanged()
    {
        var me = await MemberAsync();
        var created = (await service.CreateAsync(me, Valid(), Ct)).Value!;
        // Un texto que ya estaba guardado no se vuelve a moderar (se modero al publicar).
        var stored = (await requests.FindByIdAsync(created.Id, Ct))!;
        await requests.DeleteByRequesterAsync(me, Ct);
        await requests.InsertAsync(new WantedRequestDocument
        {
            Id = stored.Id,
            CommunityId = stored.CommunityId,
            RequesterId = stored.RequesterId,
            RequesterName = stored.RequesterName,
            Title = $"Calculadora {InMemoryContentModerator.RejectMarker}",
            CategorySlug = stored.CategorySlug,
            CategoryName = stored.CategoryName,
            Description = stored.Description,
            Features = stored.Features,
            BudgetCents = stored.BudgetCents,
            Quantity = stored.Quantity,
            ExpiresAt = stored.ExpiresAt,
            CreatedAt = stored.CreatedAt,
        }, Ct);

        var sameTitle = Edit(title: $"Calculadora {InMemoryContentModerator.RejectMarker}", budget: 80_000);
        Assert.Null((await service.UpdateAsync(me, created.Id, sameTitle, Ct)).Error);

        var badDescription = sameTitle with { Description = $"Urge {InMemoryContentModerator.RejectMarker}" };
        Assert.Equal("moderation.text_rejected", (await service.UpdateAsync(me, created.Id, badDescription, Ct)).Error?.Code);
        var badFeature = sameTitle with { Features = [InMemoryContentModerator.RejectMarker] };
        Assert.Equal("moderation.text_rejected", (await service.UpdateAsync(me, created.Id, badFeature, Ct)).Error?.Code);
    }

    [Fact]
    public async Task OnlyTheOwner_EditsClosesOrCancels_TheRestGet404()
    {
        var me = await MemberAsync();
        var neighbor = await MemberAsync(name: "Diego Hernández");
        var created = (await service.CreateAsync(me, Valid(), Ct)).Value!;

        Assert.Equal(WantedErrors.NotFound, (await service.UpdateAsync(neighbor, created.Id, Edit(budget: 1_000), Ct)).Error);
        Assert.Equal(WantedErrors.NotFound, (await service.CloseAsync(neighbor, created.Id, Ct)).Error);
        Assert.Equal(WantedErrors.NotFound, (await service.CancelAsync(neighbor, created.Id, Ct)).Error);
        Assert.Equal(WantedErrors.NotFound, (await service.CloseAsync(me, "no-es-un-id", Ct)).Error);
        Assert.Equal(WantedErrors.NotFound, (await service.UpdateAsync(me, "000000000000000000000099", Edit(), Ct)).Error);

        var mine = (await service.GetAsync(me, InMemoryCommunityRepository.UtttId, created.Id, Ct)).Value!;
        Assert.Equal(("active", 60_000L), (mine.Status, mine.BudgetCents));
    }

    [Fact]
    public async Task Close_LeavesTheBoard_FreesASlot_AndIsIdempotent()
    {
        var me = await MemberAsync();
        var neighbor = await MemberAsync(name: "Diego Hernández");
        var created = (await service.CreateAsync(me, Valid(), Ct)).Value!;
        outputCache.EvictedTags.Clear();

        var closed = (await service.CloseAsync(me, created.Id, Ct)).Value!;

        Assert.Equal("closed", closed.Status);
        Assert.Equal(["wanted-board"], outputCache.EvictedTags);
        Assert.Equal(0, await requests.CountActiveAsync(me, Now.UtcDateTime, Ct));
        Assert.Empty((await service.BoardAsync(Viewer(neighbor), new WantedBoardFilter(), Ct)).Value!.Items);
        Assert.Equal(WantedErrors.NotFound, (await service.GetAsync(neighbor, InMemoryCommunityRepository.UtttId, created.Id, Ct)).Error);

        // Otra vez: la misma respuesta. Cancelar o editar una cerrada ya no se puede (RN-11).
        Assert.Equal("closed", (await service.CloseAsync(me, created.Id, Ct)).Value!.Status);
        Assert.Equal(WantedErrors.NotActive, (await service.CancelAsync(me, created.Id, Ct)).Error);
        Assert.Equal(WantedErrors.NotActive, (await service.UpdateAsync(me, created.Id, Edit(), Ct)).Error);
    }

    [Fact]
    public async Task Cancel_IsCancelled_AndAnExpiredOneCanNoLongerChange()
    {
        var me = await MemberAsync();
        var created = (await service.CreateAsync(me, Valid(), Ct)).Value!;
        var dated = (await service.CreateAsync(me, Valid(title: "Bata de laboratorio", neededBy: new DateOnly(2026, 11, 8)), Ct)).Value!;

        Assert.Equal("cancelled", (await service.CancelAsync(me, created.Id, Ct)).Value!.Status);
        Assert.Equal("cancelled", (await service.CancelAsync(me, created.Id, Ct)).Value!.Status);
        Assert.Equal(WantedErrors.NotActive, (await service.CloseAsync(me, created.Id, Ct)).Error);

        // RF-18: vence sola al terminar su dia; ya no se edita, cierra ni cancela.
        time.Advance(TimeSpan.FromHours(1));
        Assert.Equal("expired", (await service.GetAsync(me, InMemoryCommunityRepository.UtttId, dated.Id, Ct)).Value!.Status);
        Assert.Equal(WantedErrors.NotActive, (await service.CloseAsync(me, dated.Id, Ct)).Error);
        Assert.Equal(WantedErrors.NotActive, (await service.CancelAsync(me, dated.Id, Ct)).Error);
        Assert.Equal(WantedErrors.NotActive, (await service.UpdateAsync(me, dated.Id, Edit(), Ct)).Error);
    }
}
