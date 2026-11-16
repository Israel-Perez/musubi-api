using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;
using Musubi.TestShared;
using UnitTest.PurchaseRequests;

namespace UnitTest.Listings;

/// <summary>
/// HU-51: el detalle real (<see cref="ListingService"/>, con su visibilidad) sobre repositorios en memoria, para el
/// comparador y "Avísame".
/// </summary>
internal sealed class ListingsHarness
{
    public const string Campus = InMemoryCommunityRepository.UtttId;

    public static readonly DateTimeOffset Now = new(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);

    public ListingsHarness()
    {
        Listings = new ListingService(
            Products,
            new InMemoryProductStatsRepository(Products),
            new InMemoryCategoryRepository(),
            new MemberDirectory(new InMemoryCommunityRepository(), new InMemoryUserRepository()),
            new InMemoryMediaStorage(),
            new InMemoryContentModerator(),
            new BlockDirectory(Blocks),
            new RecordingOutputCacheStore(),
            Options.Create(new CatalogOptions()),
            new OpenOrders(new InMemoryOrderRepository()),
            new NoPublications(),
            Time);
    }

    public InMemoryProductRepository Products { get; } = new();

    public InMemoryBlockRepository Blocks { get; } = new();

    public InMemoryProfileRepository Profiles { get; } = new();

    public ManualTimeProvider Time { get; } = new(Now);

    public ListingService Listings { get; }

    public BlockDirectory BlockDirectory => new(Blocks);

    public ProfileDirectory ProfileDirectory => new(Profiles, new InMemoryCareerRepository());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Una publicacion de la UTTT.</summary>
    public async Task<ProductDocument> ProductAsync(
        string sellerId,
        string title = "Calculadora Casio fx-991ES Plus",
        long priceCents = 45_000,
        int stock = 1,
        string category = "calculadoras",
        string? status = null,
        string community = Campus,
        string sellerName = "Valeria C.")
    {
        var product = new ProductDocument
        {
            Id = AuthIds.New(),
            CommunityId = community,
            SellerId = sellerId,
            Seller = new ProductSeller { Name = sellerName },
            Title = title,
            Description = "La usé un semestre.",
            CategorySlug = category,
            PriceCents = priceCents,
            Condition = ProductConditions.Used,
            ConditionDetail = "good",
            Stock = stock,
            Status = status ?? (stock > 0 ? ProductStatuses.Active : ProductStatuses.SoldOut),
            Photos = [ListingService.NewPhotoKey(sellerId)],
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        await Products.InsertAsync(product, Ct).ConfigureAwait(false);
        return product;
    }

    /// <summary>Un bloqueo de <paramref name="blocker"/> a <paramref name="blocked"/>.</summary>
    public Task BlockAsync(string blocker, string blocked) =>
        Blocks.InsertIfAbsentAsync(
            new BlockDocument { Id = AuthIds.New(), CommunityId = Campus, BlockerId = blocker, BlockedId = blocked, BlockedName = "Alguien" },
            Ct);

    private sealed class NoPublications : IProductPublications
    {
        public bool Published(ProductPublished product) => true;
    }
}
