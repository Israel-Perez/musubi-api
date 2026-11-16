using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Profiles.Data;

namespace UnitTest.Listings;

/// <summary>
/// HU-51 (decision de la usuaria, 16-Nov-2026: la regla de la maqueta): "Compara antes de comprar" da hasta 5
/// disponibles de la misma categoria y campus, sin la misma, sin las mias y sin bloqueos; el mismo modelo primero, luego
/// el precio mas cercano. Cada una trae la reputacion y las ventas de quien vende (sin las ventas si las oculta).
/// </summary>
public sealed class ProductComparisonTests
{
    private readonly ListingsHarness harness = new();
    private readonly ProductComparisonService comparison;
    private readonly string ana = AuthIds.New();
    private readonly string valeria = AuthIds.New();

    public ProductComparisonTests() =>
        comparison = new ProductComparisonService(harness.Listings, harness.Products, harness.BlockDirectory, harness.ProfileDirectory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Calculadora Casio fx-991ES Plus", "fx991es")]
    [InlineData("Calculadora graficadora TI-84 Plus", "ti84")]
    [InlineData("Laptop Acer Aspire A515-54", "a51554")]
    public void ModelCodes_AreWordsWithLettersAndDigits(string title, string code) =>
        Assert.Contains(code, ModelCodes.Of(title));

    [Theory]
    [InlineData("Laptop Lenovo IdeaPad 5 de 16GB")]
    [InlineData("Química orgánica, 2da edición")]
    [InlineData("Bata de laboratorio talla M")]
    public void ModelCodes_IgnoreNumbersMeasuresAndShortWords(string title) =>
        Assert.Empty(ModelCodes.Of(title));

    [Fact]
    public void SameModel_NeedsTheSameCode()
    {
        var codes = ModelCodes.Of("Calculadora Casio fx-991ES Plus");

        Assert.True(ModelCodes.Shares(codes, "Casio FX-991ES de mi hermana"));
        Assert.False(ModelCodes.Shares(codes, "Calculadora Casio fx-991LA X"));
        Assert.False(ModelCodes.Shares(ModelCodes.Of("Bata de laboratorio"), "Bata de laboratorio"));
    }

    [Fact]
    public async Task Compare_PutsTheSameModelFirst_ThenTheClosestPrice_AndKeepsFive()
    {
        var mine = await harness.ProductAsync(valeria, priceCents: 45_000);
        var luis = AuthIds.New();
        var cheaper = await harness.ProductAsync(luis, "Calculadora Casio fx-991LA X", 40_000);
        var closest = await harness.ProductAsync(luis, "Calculadora Sharp EL-W506", 46_000);
        var sameModel = await harness.ProductAsync(luis, "Casio fx-991ES Plus con tapa", 52_000);
        await harness.ProductAsync(luis, "Calculadora HP 10s+", 20_000);
        await harness.ProductAsync(luis, "Calculadora Texas TI-30XS", 30_000);
        var farthest = await harness.ProductAsync(luis, "Calculadora graficadora TI-84 Plus", 165_000);

        var result = await comparison.CompareAsync(ana, ListingsHarness.Campus, mine.Id, Ct);

        var items = result.Value!.Items;
        Assert.Equal(ProductComparisonService.MaxOptions, items.Count);
        Assert.Equal((sameModel.Id, true), (items[0].Product.Id, items[0].SameModel));
        Assert.Equal([closest.Id, cheaper.Id], items.Skip(1).Take(2).Select(i => i.Product.Id));
        Assert.DoesNotContain(items, i => i.Product.Id == farthest.Id || i.Product.Id == mine.Id);
        Assert.All(items.Skip(1), i => Assert.False(i.SameModel));
    }

    [Fact]
    public async Task Compare_LeavesOutMine_SoldOut_OtherCategories_OtherCampus_AndBlocks()
    {
        var product = await harness.ProductAsync(valeria);
        var (luis, diego) = (AuthIds.New(), AuthIds.New());
        var ok = await harness.ProductAsync(luis, "Calculadora Casio fx-82", 25_000);
        await harness.ProductAsync(ana, "Mi calculadora Casio", 40_000);
        await harness.ProductAsync(luis, "Calculadora agotada", 40_000, stock: 0);
        await harness.ProductAsync(luis, "Calculadora pausada", 40_000, status: ProductStatuses.Hidden);
        await harness.ProductAsync(luis, "Bata", 40_000, category: "laboratorio");
        await harness.ProductAsync(luis, "Calculadora de otro campus", 40_000, community: AuthIds.New());
        await harness.ProductAsync(diego, "Calculadora de Diego", 45_000);
        await harness.BlockAsync(diego, ana);

        var items = (await comparison.CompareAsync(ana, ListingsHarness.Campus, product.Id, Ct)).Value!.Items;

        Assert.Equal([ok.Id], items.Select(i => i.Product.Id));
    }

    [Fact]
    public async Task Compare_BringsTheSellersReputation_AndHidesTheSalesWhenTheyHideThem()
    {
        var product = await harness.ProductAsync(valeria);
        var (luis, diego) = (AuthIds.New(), AuthIds.New());
        var luisOwner = new ProfileOwner(luis, ListingsHarness.Campus, "Luis Martínez");
        await harness.Profiles.AddRatingAsync(luisOwner, 5, onTime: true, ListingsHarness.Now.UtcDateTime, Ct);
        await harness.Profiles.AddRatingAsync(luisOwner, 4, onTime: false, ListingsHarness.Now.UtcDateTime, Ct);
        await harness.Profiles.AddTradeAsync(luisOwner, asSeller: true, ListingsHarness.Now.UtcDateTime, Ct);
        var diegoOwner = new ProfileOwner(diego, ListingsHarness.Campus, "Diego Hernández");
        await harness.Profiles.AddTradeAsync(diegoOwner, asSeller: true, ListingsHarness.Now.UtcDateTime, Ct);
        await harness.Profiles.SetVisibilityAsync(diegoOwner, new ProfileVisibility { Career = true, Sales = false }, ListingsHarness.Now.UtcDateTime, Ct);
        await harness.ProductAsync(luis, "Calculadora de Luis", 44_000, sellerName: "Luis M.");
        await harness.ProductAsync(diego, "Calculadora de Diego", 47_000, sellerName: "Diego H.");

        var items = (await comparison.CompareAsync(ana, ListingsHarness.Campus, product.Id, Ct)).Value!.Items;

        var fromLuis = items.Single(i => i.Seller.Id == luis).Seller;
        var fromDiego = items.Single(i => i.Seller.Id == diego).Seller;
        Assert.Equal(("Luis M.", 4.5, 2, (int?)1), (fromLuis.Name, fromLuis.RatingAvg, fromLuis.RatingCount, fromLuis.Sales));
        Assert.Equal(("Diego H.", (double?)null, 0, (int?)null), (fromDiego.Name, fromDiego.RatingAvg, fromDiego.RatingCount, fromDiego.Sales));
        Assert.All(items, i => Assert.NotNull(i.Product.Seller));
    }

    [Fact]
    public async Task Compare_WithNothingSimilar_IsEmpty_AndAnInvisibleProduct_IsNotFound()
    {
        var alone = await harness.ProductAsync(valeria, "Bata de laboratorio talla M", category: "laboratorio");
        var paused = await harness.ProductAsync(valeria, status: ProductStatuses.Hidden);
        var elsewhere = await harness.ProductAsync(valeria, community: AuthIds.New());

        Assert.Empty((await comparison.CompareAsync(ana, ListingsHarness.Campus, alone.Id, Ct)).Value!.Items);
        Assert.Equal(ListingsErrors.ProductNotFound, (await comparison.CompareAsync(ana, ListingsHarness.Campus, paused.Id, Ct)).Error);
        Assert.Equal(ListingsErrors.ProductNotFound, (await comparison.CompareAsync(ana, ListingsHarness.Campus, elsewhere.Id, Ct)).Error);
        Assert.Equal(ListingsErrors.ProductNotFound, (await comparison.CompareAsync(ana, ListingsHarness.Campus, "no-existe", Ct)).Error);
    }
}
