using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.TestShared;

namespace UnitTest.Listings;

/// <summary>
/// HU-12 (decisiones de la usuaria, 14-Nov-2026): "Avísame cuando aparezca" guarda la busqueda por 30 dias (RN-34),
/// hasta 10 por persona; la misma busqueda solo se renueva. Coincide si la publicacion tiene todas las palabras y cumple
/// los filtros. Privadas (RNF-07).
/// </summary>
public sealed class SavedSearchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemorySavedSearchRepository searches = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly SavedSearchService service;

    public SavedSearchServiceTests() =>
        service = new SavedSearchService(searches, new MemberDirectory(new InMemoryCommunityRepository(), users), time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync(string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        Assert.True(await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"ana.{id}@uttt.edu.mx",
                EmailNormalized = $"ana.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Ana Sofía Ramírez",
                Status = status,
                EmailVerifiedAt = Now.UtcDateTime,
                CreatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private static ProductPublished Product(string title, string category = "calculadoras", long price = 165_000, string condition = ProductConditions.Used) =>
        new(AuthIds.New(), InMemoryCommunityRepository.UtttId, AuthIds.New(), title, category, price, SearchWord.Words(title), condition, "Luis M.");

    [Fact]
    public async Task Save_KeepsItFor30Days_WithItsFilters()
    {
        var ana = await MemberAsync();

        var result = await service.SaveAsync(ana, new SaveSearchRequest(" Calculadora TI-84 ", ["calculadoras"], 100_000, 200_000, "used"), Ct);

        var (saved, created) = result.Value;
        Assert.Null(result.Error);
        Assert.True(created);
        Assert.Equal(("Calculadora TI-84", (long?)100_000, (long?)200_000, "used"), (saved.Query, saved.MinPriceCents, saved.MaxPriceCents, saved.Condition));
        Assert.Equal(["calculadoras"], saved.Categories);
        Assert.Equal(Now.UtcDateTime.AddDays(30), saved.ExpiresAt);
        Assert.Single((await service.MineAsync(ana, Ct)).Items);
    }

    [Fact]
    public async Task TheSameSearchAgain_OnlyRenewsIt()
    {
        var ana = await MemberAsync();
        await service.SaveAsync(ana, new SaveSearchRequest("calculadora ti-84"), Ct);
        time.Advance(TimeSpan.FromDays(10));

        // Otra forma de escribir lo mismo: mismas palabras normalizadas.
        var again = await service.SaveAsync(ana, new SaveSearchRequest("Calculadoras TI 84"), Ct);

        Assert.False(again.Value.Created);
        Assert.Equal(Now.UtcDateTime.AddDays(40), again.Value.Search.ExpiresAt);
        Assert.Single((await service.MineAsync(ana, Ct)).Items);
    }

    [Fact]
    public async Task UpToTen_TheEleventhIsRejected_ButRenewingOneStillWorks()
    {
        var ana = await MemberAsync();
        for (var i = 0; i < SavedSearchService.MaxPerUser; i++)
        {
            Assert.Null((await service.SaveAsync(ana, new SaveSearchRequest($"calculadora modelo{i}"), Ct)).Error);
        }

        var eleventh = await service.SaveAsync(ana, new SaveSearchRequest("bata de laboratorio"), Ct);
        var renewed = await service.SaveAsync(ana, new SaveSearchRequest("calculadora modelo3"), Ct);

        Assert.Equal(ListingsErrors.TooManySavedSearches, eleventh.Error);
        Assert.Null(renewed.Error);
    }

    [Theory]
    [InlineData("", null, null, null)]
    [InlineData("!!!", null, null, null)]
    [InlineData("calculadora", 200_000L, 100_000L, null)]
    [InlineData("calculadora", -1L, null, null)]
    [InlineData("calculadora", null, null, "nueva")]
    public async Task InvalidSearches_AreRejected(string q, long? min, long? max, string? condition)
    {
        var ana = await MemberAsync();

        var result = await service.SaveAsync(ana, new SaveSearchRequest(q, null, min, max, condition), Ct);

        Assert.NotNull(result.Error);
        Assert.Empty((await service.MineAsync(ana, Ct)).Items);
    }

    [Fact]
    public async Task ASuspendedAccount_CannotSave()
    {
        var ana = await MemberAsync(UserStatuses.Suspended);

        Assert.Equal("auth.forbidden", (await service.SaveAsync(ana, new SaveSearchRequest("calculadora"), Ct)).Error?.Code);
    }

    [Fact]
    public async Task TheyArePrivate_AndExpire()
    {
        var ana = await MemberAsync();
        var luis = await MemberAsync();
        var saved = (await service.SaveAsync(ana, new SaveSearchRequest("calculadora"), Ct)).Value.Search;

        // Luis no la ve ni la puede quitar.
        await service.DeleteAsync(luis, saved.Id, Ct);
        var luisSees = (await service.MineAsync(luis, Ct)).Items;
        time.Advance(TimeSpan.FromDays(31));
        var afterExpiry = (await service.MineAsync(ana, Ct)).Items;

        Assert.Empty(luisSees);
        Assert.Empty(afterExpiry);
        Assert.Empty(await service.ActiveAsync(InMemoryCommunityRepository.UtttId, [], 10, Ct));
    }

    [Fact]
    public async Task Delete_RemovesOnlyMine()
    {
        var ana = await MemberAsync();
        var saved = (await service.SaveAsync(ana, new SaveSearchRequest("calculadora"), Ct)).Value.Search;

        await service.DeleteAsync(ana, saved.Id, Ct);

        Assert.Empty((await service.MineAsync(ana, Ct)).Items);
    }

    [Theory]
    [InlineData("Calculadora graficadora TI-84 Plus", "calculadoras", 165_000, "used", true)]
    [InlineData("Calculadoras TI-84 graficadoras", "calculadoras", 165_000, "used", true)] // otro orden y plural
    [InlineData("Calculadora graficadora Casio", "calculadoras", 165_000, "used", false)] // falta "ti" y "84"
    [InlineData("Calculadora graficadora TI-84 Plus", "libros", 165_000, "used", false)] // otra categoria
    [InlineData("Calculadora graficadora TI-84 Plus", "calculadoras", 250_000, "used", false)] // arriba del maximo
    [InlineData("Calculadora graficadora TI-84 Plus", "calculadoras", 165_000, "new", false)] // otro estado
    public async Task AListingMatches_WithAllTheWords_AndTheFilters(string title, string category, long price, string condition, bool expected)
    {
        var ana = await MemberAsync();
        await service.SaveAsync(ana, new SaveSearchRequest("calculadora TI-84", ["calculadoras"], null, 200_000, "used"), Ct);

        var search = Assert.Single(await service.ActiveAsync(InMemoryCommunityRepository.UtttId, [], 10, Ct));

        Assert.Equal(expected, search.Matches(Product(title, category, price, condition)));
    }

    [Fact]
    public async Task WithoutFilters_AnyCategoryPriceOrCondition_Matches()
    {
        var ana = await MemberAsync();
        await service.SaveAsync(ana, new SaveSearchRequest("estetoscopio"), Ct);

        var search = Assert.Single(await service.ActiveAsync(InMemoryCommunityRepository.UtttId, [], 10, Ct));

        Assert.True(search.Matches(Product("Estetoscopio Littmann", "otros", 5_000_00, ProductConditions.New)));
    }

    [Fact]
    public async Task Erase_RemovesThemWithTheAccount()
    {
        var ana = await MemberAsync();
        await service.SaveAsync(ana, new SaveSearchRequest("calculadora"), Ct);

        await service.EraseAsync(ana, Ct);

        Assert.Empty((await service.MineAsync(ana, Ct)).Items);
    }
}
