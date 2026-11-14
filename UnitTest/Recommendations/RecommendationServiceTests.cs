using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Recommendations;
using Musubi.Api.Modules.Recommendations.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared;

namespace UnitTest.Recommendations;

/// <summary>
/// HU-35: "Recomendado para ti" (personal con historial, general sin el, y completa con lo general) y "Ver parecidos"
/// (con las reglas de visibilidad del detalle). Nunca lo propio, lo de personas bloqueadas ni lo que ya no esta
/// disponible (RN-45, HU-57).
/// </summary>
public sealed class RecommendationServiceTests
{
    private const string Campus = "6716a0000000000000000c01";

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 18, 0, 0, TimeSpan.Zero);

    private readonly FakeCatalog catalog = new();
    private readonly InMemoryInteractionRepository interactions = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly RecommendationService service;
    private readonly string ana = AuthIds.New();
    private readonly string diego = AuthIds.New();

    public RecommendationServiceTests()
    {
        var store = new RecommendationModelStore(catalog, interactions, time, NullLogger<RecommendationModelStore>.Instance);
        service = new RecommendationService(catalog, interactions, new BlockDirectory(blocks), store, time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CatalogItem Add(string title, string category, string? seller = null, int hoursAgo = 1)
    {
        var item = new CatalogItem(AuthIds.New(), Campus, seller ?? diego, title, string.Empty, category, 30_000, Now.UtcDateTime.AddHours(-hoursAgo));
        catalog.Items.Add(item);
        return item;
    }

    private Task SawAsync(string user, CatalogItem item, string kind = InteractionKinds.View) =>
        interactions.RecordAsync(user, Campus, item.Id, kind, Now.UtcDateTime.AddHours(-2), Now.UtcDateTime.AddDays(90), Ct);

    [Fact]
    public async Task ForYou_WithHistory_IsPersonal_AndCompletesWithTheGeneralOnes()
    {
        var bata = Add("Bata de laboratorio talla M", "laboratorio");
        var bataS = Add("Bata de laboratorio talla S", "laboratorio");
        var casio = Add("Calculadora Casio fx-991", "calculadoras", hoursAgo: 5);
        await SawAsync(ana, bata);

        var page = await service.ForYouAsync(ana, Campus, 2, Ct);

        Assert.Equal("personal", page.Basis);
        Assert.Equal([bataS.Id, casio.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task ForYou_WithoutHistory_IsGeneral_TheMostActiveFirst()
    {
        var old = Add("Libro de Cálculo", "libros", hoursAgo: 48);
        var recent = Add("Calculadora Casio", "calculadoras");
        await SawAsync(diego, old, InteractionKinds.Purchase);

        var page = await service.ForYouAsync(ana, Campus, null, Ct);

        Assert.Equal("general", page.Basis);
        Assert.Equal([old.Id, recent.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task ForYou_NeverOwnItems_BlockedSellers_OrWhatIsNoLongerAvailable()
    {
        var luis = AuthIds.New();
        Add("Bata de Ana", "laboratorio", seller: ana);
        Add("Bata de Luis", "laboratorio", seller: luis);
        var sold = Add("Bata vendida", "laboratorio");
        var ok = Add("Bata de Diego", "laboratorio");
        catalog.Unavailable.Add(sold.Id);
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = luis, BlockedId = ana, CreatedAt = Now.UtcDateTime }, Ct);

        var page = await service.ForYouAsync(ana, Campus, 10, Ct);

        Assert.Equal([ok.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task ForYou_TheLimitGoesFrom1To20()
    {
        for (var i = 0; i < 25; i++)
        {
            Add($"Producto {i}", "otro", hoursAgo: i);
        }

        Assert.Single((await service.ForYouAsync(ana, Campus, 0, Ct)).Items);
        Assert.Equal(20, (await service.ForYouAsync(ana, Campus, 100, Ct)).Items.Count);
        Assert.Equal(10, (await service.ForYouAsync(ana, Campus, null, Ct)).Items.Count);
    }

    [Fact]
    public async Task Similar_OfAVisibleProduct_AndNotFoundOtherwise()
    {
        var casio = Add("Calculadora Casio fx-991ES Plus", "calculadoras");
        var fx82 = Add("Calculadora Casio fx-82", "calculadoras");
        Add("Bata de laboratorio", "laboratorio");

        var similar = await service.SimilarAsync(ana, Campus, casio.Id, null, Ct);
        var visitor = await service.SimilarAsync(null, null, casio.Id, null, Ct);
        var hidden = await service.SimilarAsync(ana, Campus, AuthIds.New(), null, Ct);

        Assert.Equal(("similar", fx82.Id), (similar.Value!.Basis, Assert.Single(similar.Value.Items).Id));
        // Sin sesion no se muestra quien vende (RNF-05).
        Assert.Null(Assert.Single(visitor.Value!.Items).Seller);
        Assert.NotNull(similar.Value.Items[0].Seller);
        Assert.Equal(ListingsErrors.ProductNotFound, hidden.Error);
    }

    [Fact]
    public async Task TheModel_IsRetrained_AfterThirtyMinutes()
    {
        Add("Calculadora Casio", "calculadoras");
        Assert.Single((await service.ForYouAsync(ana, Campus, null, Ct)).Items);

        Add("Calculadora TI-84", "calculadoras");
        Assert.Single((await service.ForYouAsync(ana, Campus, null, Ct)).Items);

        time.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(2, (await service.ForYouAsync(ana, Campus, null, Ct)).Items.Count);
    }

    /// <summary>Catalogo en memoria: cada publicacion de <see cref="Items"/> se ve en su campus.</summary>
    private sealed class FakeCatalog : IRecommendationCatalog
    {
        public List<CatalogItem> Items { get; } = [];

        public HashSet<string> Unavailable { get; } = [];

        public Task<IReadOnlyList<CatalogItem>> AvailableAsync(string communityId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CatalogItem>>([.. Items.Where(i => i.CommunityId == communityId).Take(limit)]);

        public Task<CatalogItem?> FindVisibleAsync(string? viewerId, string? viewerCommunityId, string productId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(i => i.Id == productId && (viewerCommunityId is null || i.CommunityId == viewerCommunityId)));

        public Task<IReadOnlyList<ProductResponse>> CardsAsync(IReadOnlyList<string> ids, bool includeSeller, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProductResponse>>([.. ids
                .Where(id => !Unavailable.Contains(id))
                .Select(id => Items.Single(i => i.Id == id))
                .Select(i => new ProductResponse(
                    i.Id, i.Title, i.Description, i.CategorySlug, i.PriceCents, "used", "good", 1, "active", [],
                    includeSeller ? new SellerResponse("Diego H.") : null, i.PublishedAt, null))]);
    }
}
