using Microsoft.AspNetCore.OutputCaching;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Platform.Caching;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;
using Musubi.TestShared;

namespace UnitTest.Listings;

/// <summary>
/// HU-07: reglas de editar, pausar, reactivar y eliminar que el flujo HTTP no deja ver (que textos se moderan,
/// cuantas veces se borra la vitrina de la cache, que fotos se borran y la cuenta no activa).
/// HU-08: marcar como vendida (R4), y una vendida no se edita, no se pausa ni se reactiva (R5).
/// </summary>
public sealed class MyListingsServiceTests
{
    private static readonly DateTime Published = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryMediaStorage media = new();
    private readonly RecordingModerator moderator = new();
    private readonly CountingOutputCache outputCache = new();
    private readonly FakePhotoReferences photoReferences = new();
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryOrderRepository orders = new();
    private readonly MyListingsService service;

    public MyListingsServiceTests()
    {
        service = new MyListingsService(
            products,
            new InMemoryCategoryRepository(),
            new MemberDirectory(new InMemoryCommunityRepository(), users),
            media,
            moderator,
            outputCache,
            [photoReferences],
            new OpenOrders(orders),
            clock);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UpdateProductRequest Request(
        string title = "Calculadora Casio fx-991ES Plus",
        string? description = "La usé un semestre.",
        string category = "calculadoras",
        long? priceCents = 45_000,
        string condition = "used",
        string? detail = "likeNew") => new(title, description, category, priceCents, condition, detail);

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
                EmailVerifiedAt = Published,
                CreatedAt = Published,
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private async Task<ProductDocument> ProductAsync(string sellerId, int stock = 1, int photos = 1)
    {
        var keys = Enumerable.Range(0, photos).Select(_ => ListingService.NewPhotoKey(sellerId)).ToList();
        foreach (var key in keys)
        {
            await media.PutAsync(key, [1, 2, 3], "image/webp", Ct).ConfigureAwait(false);
        }

        var product = new ProductDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            SellerId = sellerId,
            Seller = new ProductSeller { Name = "Ana R." },
            Title = "Calculadora Casio fx-991ES Plus",
            Description = "La usé un semestre.",
            CategorySlug = "calculadoras",
            PriceCents = 45_000,
            Condition = ProductConditions.Used,
            ConditionDetail = "likeNew",
            Stock = stock,
            Status = stock > 0 ? ProductStatuses.Active : ProductStatuses.SoldOut,
            Photos = keys,
            CreatedAt = Published,
            UpdatedAt = Published,
        };
        await products.InsertAsync(product, Ct).ConfigureAwait(false);
        return product;
    }

    [Fact]
    public async Task Update_ByTheOwner_SavesTheCleanDetails_AndEvictsTheShowcase()
    {
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);

        var result = await service.UpdateAsync(
            seller, product.Id, Request(title: "  Calculadora Casio fx-570  ", category: "libros", priceCents: 39_900, condition: "new", detail: null), Ct);

        Assert.Null(result.Error);
        Assert.Equal("Calculadora Casio fx-570", result.Value!.Title);
        Assert.Equal("libros", result.Value.Category);
        Assert.Equal(39_900, result.Value.PriceCents);
        Assert.Null(result.Value.ConditionDetail);
        Assert.Equal(clock.Now.UtcDateTime, (await products.FindByIdAsync(product.Id, Ct))!.UpdatedAt);
        Assert.Equal([CachingServiceCollectionExtensions.ShowcaseTag], outputCache.Evicted);
    }

    [Fact]
    public async Task Update_OfSomeoneElsesProduct_IsNotFound_AndNothingIsModeratedOrChanged()
    {
        var product = await ProductAsync(await MemberAsync());
        var outsider = await MemberAsync();

        var result = await service.UpdateAsync(outsider, product.Id, Request(title: "Me la quedo yo"), Ct);

        Assert.Equal(ListingsErrors.ProductNotFound, result.Error);
        Assert.Empty(moderator.Checked);
        Assert.Empty(outputCache.Evicted);
        Assert.Equal(product.Title, (await products.FindByIdAsync(product.Id, Ct))!.Title);
    }

    [Fact]
    public async Task Update_ModeratesOnlyTheTextThatChanged()
    {
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);

        await service.UpdateAsync(seller, product.Id, Request(priceCents: 40_000), Ct);
        Assert.Empty(moderator.Checked);

        await service.UpdateAsync(seller, product.Id, Request(priceCents: 40_000, description: "Con su tapa."), Ct);
        Assert.Equal(["Con su tapa."], moderator.Checked);
    }

    [Theory]
    [InlineData(true, "el nombre del producto")]
    [InlineData(false, "la descripción")]
    public async Task Update_WithInappropriateText_IsRejected_AndNothingChanges(bool inTitle, string field)
    {
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);
        var bad = "Calculadora " + InMemoryContentModerator.RejectMarker;

        var result = await service.UpdateAsync(
            seller, product.Id, inTitle ? Request(title: bad) : Request(description: bad), Ct);

        Assert.Equal("moderation.text_rejected", result.Error?.Code);
        Assert.Contains(field, result.Error!.Message, StringComparison.Ordinal);
        var stored = await products.FindByIdAsync(product.Id, Ct);
        Assert.Equal(product.Title, stored!.Title);
        Assert.Equal(product.Description, stored.Description);
        Assert.Empty(outputCache.Evicted);
    }

    [Fact]
    public async Task Update_AppliesThePublishingRules()
    {
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);

        Assert.Equal(ListingsErrors.InvalidTitle, (await service.UpdateAsync(seller, product.Id, Request(title: "ab"), Ct)).Error);
        Assert.Equal(ListingsErrors.InvalidDescription, (await service.UpdateAsync(seller, product.Id, Request(description: new string('a', 501)), Ct)).Error);
        Assert.Equal(ListingsErrors.InvalidPrice, (await service.UpdateAsync(seller, product.Id, Request(priceCents: 0), Ct)).Error);
        Assert.Equal(ListingsErrors.InvalidPrice, (await service.UpdateAsync(seller, product.Id, Request(priceCents: ListingService.MaxPriceCents + 1), Ct)).Error);
        Assert.Equal(ListingsErrors.InvalidCondition, (await service.UpdateAsync(seller, product.Id, Request(detail: null), Ct)).Error);
        Assert.Equal(ListingsErrors.InvalidCondition, (await service.UpdateAsync(seller, product.Id, Request(condition: "new", detail: "good"), Ct)).Error);
        Assert.Equal(ListingsErrors.InvalidCategory, (await service.UpdateAsync(seller, product.Id, Request(category: "autos"), Ct)).Error);
    }

    [Fact]
    public async Task PauseAndResume_AreIdempotent_AndResumeWithoutStockIsSoldOut()
    {
        var seller = await MemberAsync();
        var soldOut = await ProductAsync(seller, stock: 0);

        Assert.Equal(ProductStatuses.Hidden, (await service.SetPausedAsync(seller, soldOut.Id, true, Ct)).Value!.Status);
        Assert.Equal(ProductStatuses.Hidden, (await service.SetPausedAsync(seller, soldOut.Id, true, Ct)).Value!.Status);
        Assert.Equal(ProductStatuses.SoldOut, (await service.SetPausedAsync(seller, soldOut.Id, false, Ct)).Value!.Status);
        Assert.Equal(3, outputCache.Evicted.Count);
    }

    [Fact]
    public async Task Delete_RemovesItsPhotos_ButNotTheOtherProductsOfTheSeller()
    {
        var seller = await MemberAsync();
        var product = await ProductAsync(seller, photos: 3);
        var other = await ProductAsync(seller);

        var result = await service.DeleteAsync(seller, product.Id, Ct);

        Assert.Null(result.Error);
        Assert.Null(await products.FindByIdAsync(product.Id, Ct));
        foreach (var key in product.Photos)
        {
            Assert.False(await media.ExistsAsync(key, Ct));
        }

        Assert.True(await media.ExistsAsync(other.Photos[0], Ct));
        Assert.Equal([CachingServiceCollectionExtensions.ShowcaseTag], outputCache.Evicted);
        Assert.Equal(ListingsErrors.ProductNotFound, (await service.DeleteAsync(seller, product.Id, Ct)).Error);
    }

    [Fact]
    public async Task Delete_WithAPendingOrder_IsRejected_ButSomeoneElseStillGets404()
    {
        var seller = await MemberAsync();
        var outsider = await MemberAsync();
        var product = await ProductAsync(seller);
        var conversation = AuthIds.New();
        // RN-07 (HU-23): un pedido pendiente de esa publicacion.
        await orders.TryInsertAsync(Order(product.Id, conversation, OrderStatuses.Pending), Ct);

        var byOwner = await service.DeleteAsync(seller, product.Id, Ct);
        var byOutsider = await service.DeleteAsync(outsider, product.Id, Ct);

        Assert.Equal(ListingsErrors.ProductHasOrders, byOwner.Error);
        Assert.Equal(ListingsErrors.ProductNotFound, byOutsider.Error);
        Assert.NotNull(await products.FindByIdAsync(product.Id, Ct));
    }

    [Fact]
    public async Task Delete_WithOnlyCancelledOrders_IsAllowed()
    {
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);
        await orders.TryInsertAsync(Order(product.Id, AuthIds.New(), OrderStatuses.Cancelled), Ct);

        Assert.Null((await service.DeleteAsync(seller, product.Id, Ct)).Error);
    }

    private static OrderDocument Order(string productId, string conversationId, string status) => new()
    {
        Id = AuthIds.New(),
        CommunityId = InMemoryCommunityRepository.UtttId,
        Number = 1001,
        BuyerId = AuthIds.New(),
        SellerId = AuthIds.New(),
        BuyerName = "Ana",
        SellerName = "Diego",
        ConversationId = conversationId,
        Item = new OrderItem { SourceId = productId, ProductId = productId, Title = "Calculadora", PriceCents = 1, Quantity = 1, TotalCents = 1 },
        Status = status,
    };

    [Fact]
    public async Task Delete_KeepsThePhotosThatAnOfferStillShows()
    {
        var seller = await MemberAsync();
        var product = await ProductAsync(seller, photos: 2);
        // HU-16: una oferta vinculada copio la portada.
        photoReferences.InUse.Add(product.Photos[0]);

        Assert.Null((await service.DeleteAsync(seller, product.Id, Ct)).Error);

        Assert.True(await media.ExistsAsync(product.Photos[0], Ct));
        Assert.False(await media.ExistsAsync(product.Photos[1], Ct));
        Assert.Equal((seller, 2), (photoReferences.AskedFor, photoReferences.AskedKeys));
    }

    [Fact]
    public async Task MarkSold_EvictsTheShowcase_AndIsIdempotent()
    {
        // HU-08 R4.
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);

        var first = await service.MarkSoldAsync(seller, product.Id, Ct);
        clock.Advance(TimeSpan.FromDays(1));
        var again = await service.MarkSoldAsync(seller, product.Id, Ct);

        Assert.Equal(ProductStatuses.Sold, first.Value!.Status);
        Assert.Equal(first.Value.SoldAt, again.Value!.SoldAt);
        Assert.Equal([CachingServiceCollectionExtensions.ShowcaseTag], outputCache.Evicted);
    }

    [Fact]
    public async Task ASoldProduct_IsNotEditedPausedOrResumed_ButCanBeDeleted()
    {
        // HU-08 R5.
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);
        await service.MarkSoldAsync(seller, product.Id, Ct);

        Assert.Equal(ListingsErrors.ProductSold, (await service.UpdateAsync(seller, product.Id, Request(priceCents: 1_000), Ct)).Error);
        Assert.Equal(ListingsErrors.ProductSold, (await service.SetPausedAsync(seller, product.Id, true, Ct)).Error);
        Assert.Equal(ListingsErrors.ProductSold, (await service.SetPausedAsync(seller, product.Id, false, Ct)).Error);
        Assert.Equal(ProductStatuses.Sold, (await products.FindByIdAsync(product.Id, Ct))!.Status);
        Assert.Null((await service.DeleteAsync(seller, product.Id, Ct)).Error);
    }

    [Fact]
    public async Task SomeoneElse_CannotMarkItSold_EvenIfItIsAlreadySold()
    {
        // HU-08 R4, RNF-03.
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);
        var outsider = await MemberAsync();

        Assert.Equal(ListingsErrors.ProductNotFound, (await service.MarkSoldAsync(outsider, product.Id, Ct)).Error);
        await service.MarkSoldAsync(seller, product.Id, Ct);
        // Ya vendida: a otra persona tampoco se le revela (404, no 409).
        Assert.Equal(ListingsErrors.ProductNotFound, (await service.MarkSoldAsync(outsider, product.Id, Ct)).Error);
        Assert.Equal(ListingsErrors.ProductNotFound, (await service.SetPausedAsync(outsider, product.Id, false, Ct)).Error);
    }

    [Fact]
    public async Task SetStock_ChangesThePieces_AndEvictsTheShowcase()
    {
        // HU-50: "Cambiar piezas"; con 0 queda agotada y con mas vuelve a estar disponible.
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);

        var empty = await service.SetStockAsync(seller, product.Id, new SetStockRequest(0), Ct);
        var restocked = await service.SetStockAsync(seller, product.Id, new SetStockRequest(10), Ct);

        Assert.Equal((0, ProductStatuses.SoldOut), (empty.Value!.Stock, empty.Value.Status));
        Assert.Equal((10, ProductStatuses.Active), (restocked.Value!.Stock, restocked.Value.Status));
        Assert.Equal(2, outputCache.Evicted.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(null)]
    public async Task SetStock_OutOfRange_IsRejected(int? stock)
    {
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);

        var result = await service.SetStockAsync(seller, product.Id, new SetStockRequest(stock), Ct);

        Assert.Equal("listings.invalid_stock", result.Error?.Code);
        Assert.Contains("0 a 10", result.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(1, (await products.FindByIdAsync(product.Id, Ct))!.Stock);
    }

    [Fact]
    public async Task SetStock_OfASoldProduct_Is409_AndOfSomeoneElses404()
    {
        var seller = await MemberAsync();
        var product = await ProductAsync(seller);
        var outsider = await MemberAsync();
        await service.MarkSoldAsync(seller, product.Id, Ct);

        Assert.Equal(ListingsErrors.ProductSold, (await service.SetStockAsync(seller, product.Id, new SetStockRequest(3), Ct)).Error);
        Assert.Equal(ListingsErrors.ProductNotFound, (await service.SetStockAsync(outsider, product.Id, new SetStockRequest(3), Ct)).Error);
    }

    [Fact]
    public async Task SomeoneElse_CannotPauseOrDelete()
    {
        var product = await ProductAsync(await MemberAsync());
        var outsider = await MemberAsync();

        Assert.Equal(ListingsErrors.ProductNotFound, (await service.SetPausedAsync(outsider, product.Id, true, Ct)).Error);
        Assert.Equal(ListingsErrors.ProductNotFound, (await service.DeleteAsync(outsider, product.Id, Ct)).Error);
        Assert.Equal(ProductStatuses.Active, (await products.FindByIdAsync(product.Id, Ct))!.Status);
        Assert.True(await media.ExistsAsync(product.Photos[0], Ct));
    }

    [Fact]
    public async Task AnAccountThatIsNotActive_IsForbidden()
    {
        var seller = await MemberAsync(UserStatuses.Suspended);
        var product = await ProductAsync(seller);

        Assert.Equal(ApiErrors.Forbidden, (await service.UpdateAsync(seller, product.Id, Request(), Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await service.SetPausedAsync(seller, product.Id, true, Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await service.DeleteAsync(seller, product.Id, Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await service.MarkSoldAsync(seller, product.Id, Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await service.SetStockAsync(seller, product.Id, new SetStockRequest(3), Ct)).Error);
        Assert.NotNull(await products.FindByIdAsync(product.Id, Ct));
    }

    /// <summary>El moderador en memoria, guardando cada texto que se le pidio revisar.</summary>
    private sealed class RecordingModerator : IContentModerator
    {
        private readonly InMemoryContentModerator inner = new();

        public List<string> Checked { get; } = [];

        public Task<ModerationVerdict> CheckImageAsync(byte[] image, CancellationToken cancellationToken) =>
            inner.CheckImageAsync(image, cancellationToken);

        public Task<IReadOnlyList<ModerationVerdict>> CheckTextsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Checked.AddRange(texts);
            return inner.CheckTextsAsync(texts, cancellationToken);
        }
    }

    /// <summary>Fotos que "otro modulo" sigue mostrando.</summary>
    private sealed class FakePhotoReferences : IListingPhotoReferences
    {
        public HashSet<string> InUse { get; } = new(StringComparer.Ordinal);

        public string? AskedFor { get; private set; }

        public int AskedKeys { get; private set; }

        public Task<IReadOnlyCollection<string>> InUseAsync(string sellerId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken)
        {
            AskedFor = sellerId;
            AskedKeys = keys.Count;
            return Task.FromResult<IReadOnlyCollection<string>>(keys.Where(InUse.Contains).ToList());
        }
    }

    /// <summary>Cache de salida que solo cuenta las etiquetas borradas.</summary>
    private sealed class CountingOutputCache : IOutputCacheStore
    {
        public List<string> Evicted { get; } = [];

        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
        {
            Evicted.Add(tag);
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
