using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.TestShared.Listings;

/// <summary>
/// HU-06: comportamiento esperado de los repositorios de publicaciones. Corre contra los fakes en memoria
/// (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa una comunidad o un
/// vendedor propio: la base de Mongo se comparte en la clase.
/// </summary>
public abstract class ListingRepositoryContract
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    internal abstract ICategoryRepository Categories { get; }

    internal abstract IProductRepository Products { get; }

    /// <summary>Id de la UTTT en esa base (lo genera el script en Mongo).</summary>
    internal abstract Task<string> UtttIdAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProductDocument NewProduct(string communityId, string sellerId, string category = "calculadoras", string status = ProductStatuses.Active, long price = 45_000) => new()
    {
        Id = AuthIds.New(),
        CommunityId = communityId,
        SellerId = sellerId,
        Seller = new ProductSeller { Name = "Ana R." },
        Title = "Calculadora Casio fx-991ES Plus",
        Description = "La usé un semestre.",
        CategorySlug = category,
        PriceCents = price,
        Condition = ProductConditions.Used,
        ConditionDetail = "likeNew",
        Stock = status == ProductStatuses.SoldOut ? 0 : 1,
        Status = status,
        Photos = [$"media/products/{sellerId}/{Guid.NewGuid():N}.webp"],
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public async Task Categories_OfUttt_AreTheSixOfTheMockupInOrder()
    {
        var categories = await Categories.ListActiveAsync(await UtttIdAsync(), Ct);

        Assert.Equal(
            ["calculadoras", "libros", "laptops", "laboratorio", "dibujo", "electronica"],
            categories.Select(c => c.Slug));
        Assert.Equal("Electrónica", categories[^1].Name);
    }

    [Fact]
    public async Task Categories_OfAnotherCommunity_AreEmpty()
    {
        Assert.Empty(await Categories.ListActiveAsync(AuthIds.New(), Ct));
    }

    [Fact]
    public async Task Page_IsNewestFirst_AndTheCursorContinues()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var inserted = new List<ProductDocument>();
        for (var i = 0; i < 3; i++)
        {
            var product = NewProduct(community, seller);
            inserted.Add(product);
            await Products.InsertAsync(product, Ct);
        }

        var first = await Products.FindPageAsync(new ProductQuery(community, null, true, null, 2), Ct);
        var second = await Products.FindPageAsync(new ProductQuery(community, null, true, first[^1].Id, 2), Ct);

        Assert.Equal([inserted[2].Id, inserted[1].Id], first.Select(p => p.Id));
        Assert.Equal([inserted[0].Id], second.Select(p => p.Id));
        Assert.Equal(3, await Products.CountAsync(new ProductQuery(community, null, true, null, 2), Ct));
    }

    [Fact]
    public async Task Page_RoundTripsAllFields()
    {
        var community = AuthIds.New();
        var product = NewProduct(community, AuthIds.New());
        await Products.InsertAsync(product, Ct);

        var read = Assert.Single(await Products.FindPageAsync(new ProductQuery(community, null, true, null, 10), Ct));

        Assert.Equal(product.SellerId, read.SellerId);
        Assert.Equal("Ana R.", read.Seller.Name);
        Assert.Equal(product.Title, read.Title);
        Assert.Equal(45_000, read.PriceCents);
        Assert.Equal("likeNew", read.ConditionDetail);
        Assert.Equal(product.Photos, read.Photos);
        Assert.Equal(Now, read.CreatedAt);
    }

    [Fact]
    public async Task Filters_ByCategory_AndHideSoldOut()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, seller, "libros"), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "calculadoras"), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "calculadoras", ProductStatuses.SoldOut), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "calculadoras", ProductStatuses.Hidden), Ct);

        var calculators = await Products.FindPageAsync(new ProductQuery(community, "calculadoras", true, null, 10), Ct);
        var available = await Products.FindPageAsync(new ProductQuery(community, "calculadoras", false, null, 10), Ct);

        Assert.Equal(2, calculators.Count);
        Assert.Single(available);
        // Sin agotados ni pausados: el libro y una calculadora.
        Assert.Equal(2, await Products.CountAsync(new ProductQuery(community, null, false, null, 10), Ct));
    }

    [Fact]
    public async Task BySeller_ExcludesRemoved()
    {
        var seller = AuthIds.New();
        var community = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, seller), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Hidden), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Removed), Ct);

        var mine = await Products.FindBySellerAsync(seller, 10, Ct);

        Assert.Equal(2, mine.Count);
        Assert.DoesNotContain(mine, p => p.Status == ProductStatuses.Removed);
    }

    [Fact]
    public async Task ActivePrices_OnlyThatCategoryAndAvailable()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, seller, "libros", price: 30_000), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "libros", price: 42_000), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "libros", ProductStatuses.SoldOut, 99_000), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "dibujo", price: 18_000), Ct);

        var prices = await Products.FindActivePricesAsync(community, "libros", 10, Ct);

        Assert.Equal([30_000L, 42_000L], prices.Order());
    }

    [Fact]
    public async Task DeleteBySeller_RemovesEveryStatusOnlyForThatSeller()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var other = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, seller), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.SoldOut), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Removed), Ct);
        await Products.InsertAsync(NewProduct(community, other), Ct);

        Assert.Equal(3, await Products.DeleteBySellerAsync(seller, Ct));
        Assert.Empty(await Products.FindBySellerAsync(seller, 10, Ct));
        Assert.Single(await Products.FindBySellerAsync(other, 10, Ct));
        Assert.Equal(0, await Products.DeleteBySellerAsync(seller, Ct));
    }
}
