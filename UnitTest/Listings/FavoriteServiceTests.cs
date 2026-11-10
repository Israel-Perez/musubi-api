using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared;

namespace UnitTest.Listings;

/// <summary>
/// HU-53: guardar y quitar favoritos, los tres estados de la lista, la privacidad, los bloqueos de HU-57 y el
/// tope de 200. La app arma el corazon con GET /favorites; los productos no cambian.
/// </summary>
public sealed class FavoriteServiceTests
{
    private const string OtherCommunity = "000000000000000000000002";
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryFavoriteRepository favorites = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly ManualTimeProvider time = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));
    private readonly FavoriteService service;

    public FavoriteServiceTests()
    {
        service = new FavoriteService(
            favorites,
            products,
            new MemberDirectory(new InMemoryCommunityRepository(), users),
            new BlockDirectory(blocks),
            time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync(string communityId = InMemoryCommunityRepository.UtttId, string status = UserStatuses.Active)
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
                FullName = "Ana Sofía Ramírez",
                Status = status,
                EmailVerifiedAt = Now,
                CreatedAt = Now,
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private static ProductDocument Product(string communityId, string sellerId, string status = ProductStatuses.Active) => new()
    {
        Id = AuthIds.New(),
        CommunityId = communityId,
        SellerId = sellerId,
        Seller = new ProductSeller { Name = "Ana R." },
        Title = "Calculadora Casio fx-991ES Plus",
        Description = "La usé un semestre.",
        CategorySlug = "calculadoras",
        PriceCents = 45_000,
        Condition = ProductConditions.Used,
        ConditionDetail = "likeNew",
        Stock = status == ProductStatuses.SoldOut ? 0 : 1,
        Status = status,
        Photos = [$"media/products/{sellerId}/{Guid.NewGuid():N}.webp"],
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private Task<bool> SaveAsync(string userId, string productId, int minute = 0) =>
        favorites.InsertIfAbsentAsync(
            new FavoriteDocument
            {
                Id = AuthIds.New(),
                UserId = userId,
                ProductId = productId,
                CommunityId = InMemoryCommunityRepository.UtttId,
                CreatedAt = Now.AddMinutes(minute),
            },
            Ct);

    private Task<bool> BlockAsync(string blockerId, string blockedId) =>
        blocks.InsertIfAbsentAsync(
            new BlockDocument
            {
                Id = AuthIds.New(),
                CommunityId = InMemoryCommunityRepository.UtttId,
                BlockerId = blockerId,
                BlockedId = blockedId,
                BlockedName = "Ana Sofía Ramírez",
                CreatedAt = Now,
            },
            Ct);

    [Fact]
    public async Task Add_Twice_KeepsOneFavorite_ForTheTokenUser()
    {
        var me = await MemberAsync();
        var seller = await MemberAsync();
        var product = Product(InMemoryCommunityRepository.UtttId, seller);
        await products.InsertAsync(product, Ct);

        Assert.Null((await service.AddAsync(me, product.Id, Ct)).Error);
        Assert.Null((await service.AddAsync(me, product.Id, Ct)).Error);

        var saved = Assert.Single(await favorites.FindByUserAsync(me, 10, Ct));
        Assert.Equal((product.Id, InMemoryCommunityRepository.UtttId), (saved.ProductId, saved.CommunityId));
    }

    [Fact]
    public async Task Add_AcceptsSoldOut_AndRejectsHiddenRemovedMissingOrAnotherCampus()
    {
        var me = await MemberAsync();
        var seller = await MemberAsync();
        var soldOut = Product(InMemoryCommunityRepository.UtttId, seller, ProductStatuses.SoldOut);
        var hidden = Product(InMemoryCommunityRepository.UtttId, seller, ProductStatuses.Hidden);
        var removed = Product(InMemoryCommunityRepository.UtttId, seller, ProductStatuses.Removed);
        var elsewhere = Product(OtherCommunity, seller);
        foreach (var product in new[] { soldOut, hidden, removed, elsewhere })
        {
            await products.InsertAsync(product, Ct);
        }

        Assert.Null((await service.AddAsync(me, soldOut.Id, Ct)).Error);
        foreach (var productId in new[] { hidden.Id, removed.Id, elsewhere.Id, AuthIds.New(), "no-es-un-id" })
        {
            Assert.Equal(ListingsErrors.ProductNotFound, (await service.AddAsync(me, productId, Ct)).Error);
        }
    }

    [Fact]
    public async Task Add_WithABlockInEitherDirection_IsNotFound()
    {
        var me = await MemberAsync();
        var seller = await MemberAsync();
        var iBlocked = Product(InMemoryCommunityRepository.UtttId, seller);
        var blockedMe = Product(InMemoryCommunityRepository.UtttId, seller);
        await products.InsertAsync(iBlocked, Ct);
        await products.InsertAsync(blockedMe, Ct);

        await BlockAsync(me, seller);
        Assert.Equal(ListingsErrors.ProductNotFound, (await service.AddAsync(me, iBlocked.Id, Ct)).Error);

        await BlockAsync(seller, me);
        Assert.Equal(ListingsErrors.ProductNotFound, (await service.AddAsync(me, blockedMe.Id, Ct)).Error);
    }

    [Fact]
    public async Task Add_ByASuspendedAccount_IsForbidden()
    {
        var suspended = await MemberAsync(status: UserStatuses.Suspended);
        var seller = await MemberAsync();
        var product = Product(InMemoryCommunityRepository.UtttId, seller);
        await products.InsertAsync(product, Ct);

        var result = await service.AddAsync(suspended, product.Id, Ct);

        Assert.Equal("auth.forbidden", result.Error?.Code);
    }

    [Fact]
    public async Task List_IsNewestFirst_AndMapsAvailableSoldOutAndUnavailableWithoutProduct()
    {
        var me = await MemberAsync();
        var seller = await MemberAsync();
        var active = Product(InMemoryCommunityRepository.UtttId, seller);
        var soldOut = Product(InMemoryCommunityRepository.UtttId, seller, ProductStatuses.SoldOut);
        var hidden = Product(InMemoryCommunityRepository.UtttId, seller, ProductStatuses.Hidden);
        var removed = Product(InMemoryCommunityRepository.UtttId, seller, ProductStatuses.Removed);
        foreach (var product in new[] { active, soldOut, hidden, removed })
        {
            await products.InsertAsync(product, Ct);
        }

        var missing = AuthIds.New();
        await SaveAsync(me, active.Id, minute: 1);
        await SaveAsync(me, soldOut.Id, minute: 2);
        await SaveAsync(me, hidden.Id, minute: 3);
        await SaveAsync(me, removed.Id, minute: 4);
        await SaveAsync(me, missing, minute: 5);

        var list = await service.ListAsync(me, InMemoryCommunityRepository.UtttId, Ct);

        Assert.Equal([missing, removed.Id, hidden.Id, soldOut.Id, active.Id], list.Items.Select(i => i.ProductId));
        var byProduct = list.Items.ToDictionary(i => i.ProductId);
        Assert.Equal(("available", active.Id), (byProduct[active.Id].Status, byProduct[active.Id].Product?.Id));
        Assert.Equal(("soldOut", soldOut.Id), (byProduct[soldOut.Id].Status, byProduct[soldOut.Id].Product?.Id));
        foreach (var unavailable in new[] { hidden.Id, removed.Id, missing })
        {
            Assert.Equal(FavoriteStatuses.Unavailable, byProduct[unavailable].Status);
            Assert.Null(byProduct[unavailable].Product);
        }
    }

    [Fact]
    public async Task List_WithAProductOfAnotherCampus_IsUnavailable()
    {
        var me = await MemberAsync();
        var seller = await MemberAsync();
        var elsewhere = Product(OtherCommunity, seller);
        await products.InsertAsync(elsewhere, Ct);
        await SaveAsync(me, elsewhere.Id);

        var item = Assert.Single((await service.ListAsync(me, InMemoryCommunityRepository.UtttId, Ct)).Items);

        Assert.Equal(FavoriteStatuses.Unavailable, item.Status);
        Assert.Null(item.Product);
    }

    [Fact]
    public async Task List_WithABlockedSeller_IsUnavailable()
    {
        var me = await MemberAsync();
        var seller = await MemberAsync();
        var product = Product(InMemoryCommunityRepository.UtttId, seller);
        await products.InsertAsync(product, Ct);
        await SaveAsync(me, product.Id);
        await BlockAsync(seller, me);

        var item = Assert.Single((await service.ListAsync(me, InMemoryCommunityRepository.UtttId, Ct)).Items);

        Assert.Equal(FavoriteStatuses.Unavailable, item.Status);
        Assert.Null(item.Product);
    }

    [Fact]
    public async Task List_OnlyMine_AndLimitedTo200()
    {
        var me = await MemberAsync();
        var other = await MemberAsync();
        var seller = await MemberAsync();
        var product = Product(InMemoryCommunityRepository.UtttId, seller);
        await products.InsertAsync(product, Ct);
        for (var i = 0; i < FavoriteService.MaxListed + 1; i++)
        {
            await SaveAsync(me, AuthIds.New(), minute: i);
        }

        await SaveAsync(other, product.Id);

        var mine = await service.ListAsync(me, InMemoryCommunityRepository.UtttId, Ct);
        var theirs = await service.ListAsync(other, InMemoryCommunityRepository.UtttId, Ct);

        Assert.Equal(FavoriteService.MaxListed, mine.Items.Count);
        Assert.DoesNotContain(mine.Items, i => i.ProductId == product.Id);
        Assert.Equal(product.Id, Assert.Single(theirs.Items).ProductId);
    }

    [Fact]
    public async Task Remove_IsIdempotent_AndOnlyRemovesTheTokenUsersFavorite()
    {
        var me = await MemberAsync();
        var other = await MemberAsync();
        var seller = await MemberAsync();
        var product = Product(InMemoryCommunityRepository.UtttId, seller);
        await products.InsertAsync(product, Ct);
        await SaveAsync(me, product.Id);
        await SaveAsync(other, product.Id);

        await service.RemoveAsync(me, product.Id, Ct);
        await service.RemoveAsync(me, product.Id, Ct);

        Assert.Empty((await service.ListAsync(me, InMemoryCommunityRepository.UtttId, Ct)).Items);
        Assert.Single((await service.ListAsync(other, InMemoryCommunityRepository.UtttId, Ct)).Items);
    }
}
