using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Moderation;
using Musubi.TestShared;

namespace UnitTest.PurchaseRequests;

/// <summary>
/// HU-13: RN-09 (obligatorios), limites de cada campo, la fecha en el dia del campus, RN-10 (vencimiento), RN-12
/// (maximo de activas), 02-07 (moderacion) y quien ve una solicitud (campus, activa, bloqueo).
/// </summary>
public sealed class WantedServiceTests
{
    private const string OtherCommunity = "000000000000000000000002";

    // Domingo 8 de noviembre de 2026, 23:30 en el campus (UTC-6): ya es lunes 9 en UTC.
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 5, 30, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryWantedRepository requests = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly WantedService service;

    public WantedServiceTests()
    {
        service = new WantedService(
            requests,
            new MemberDirectory(new InMemoryCommunityRepository(), users),
            new CategoryDirectory(new InMemoryCategoryRepository()),
            new BlockDirectory(blocks),
            new InMemoryContentModerator(),
            time);
    }

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
}
