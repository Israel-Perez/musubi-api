using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.SellerSupport;

namespace UnitTest.SellerSupport;

/// <summary>
/// HU-37 (RF-52, RN-38): "Ideas para venderla" son siempre las tres sugerencias predefinidas (precio, descripcion y
/// fotos); primero las que piden los datos. Si el precio pasa los presupuestos de quienes la buscan, se dice cuanto.
/// </summary>
public sealed class SalesTipsTests
{
    private const string LongDescription = "Bata de manga larga, talla M. La usé dos semestres en el laboratorio de química y está limpia.";

    private static ProductResponse Product(long price = 25_000, string description = LongDescription, int photos = 3) => new(
        "p1",
        "Bata de laboratorio blanca, talla M",
        description,
        "laboratorio",
        price,
        ProductConditions.Used,
        null,
        1,
        ProductStatuses.Active,
        [.. Enumerable.Range(0, photos).Select(i => $"/media/{i}.webp")],
        new SellerResponse("Diego H."),
        new DateTime(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc),
        null);

    private static WantedForMatch Request(long budget) => new("w1", "c1", "u1", "Bata de laboratorio", "laboratorio", budget, Active: true);

    [Fact]
    public void AlwaysTheThreeIdeas_InTheirOrder_WhenNoneApplies()
    {
        var tips = SalesTips.For(Product(), ProductDemand.None, null);

        Assert.Equal([SalesTipKinds.Price, SalesTipKinds.Description, SalesTipKinds.Photos], tips.Select(t => t.Kind));
        Assert.Equal(["Revisa el precio", "Mejora la descripción", "Actualiza las fotos"], tips.Select(t => t.Title));
        Assert.All(tips, tip => Assert.False(tip.Applies));
    }

    [Fact]
    public void APriceAboveTheirBudgets_SaysHowMuchTheyOffer()
    {
        var tips = SalesTips.For(Product(price: 25_000), new ProductDemand([Request(21_000), Request(22_000)], 1), null);

        Assert.Equal(new SalesTipResponse(SalesTipKinds.Price, "Revisa el precio", "Quienes buscan algo así ofrecen hasta $220 y tú pides $250.", Applies: true), tips[0]);
        Assert.Equal("Quien busca algo así ofrece hasta $210 y tú pides $250.", SalesTips.For(Product(), new ProductDemand([Request(21_000)], 0), null)[0].Body);
    }

    [Fact]
    public void APriceWithinTheirBudget_FallsBackToTheCategoryRange()
    {
        var above = SalesTips.For(Product(price: 40_000), new ProductDemand([Request(40_000)], 0), new PriceRangeResponse(18_000, 30_000, 8));
        var within = SalesTips.For(Product(price: 25_000), ProductDemand.None, new PriceRangeResponse(18_000, 30_000, 8));

        Assert.Equal(("Productos parecidos se venden entre $180 y $300.", true), (above[0].Body, above[0].Applies));
        Assert.Equal(("Productos parecidos se venden entre $180 y $300. Tu precio está dentro.", false), (within[0].Body, within[0].Applies));
    }

    [Fact]
    public void AShortDescriptionAndFewPhotos_GoFirst()
    {
        var tips = SalesTips.For(Product(description: "Bata talla M.", photos: 1), ProductDemand.None, null);

        Assert.Equal([SalesTipKinds.Description, SalesTipKinds.Photos, SalesTipKinds.Price], tips.Select(t => t.Kind));
        Assert.Equal("Tiene 1 foto. Agrega más, con luz natural y desde varios ángulos.", tips[1].Body);
        Assert.Equal("Tiene 2 fotos. Agrega más, con luz natural y desde varios ángulos.", SalesTips.For(Product(photos: 2), ProductDemand.None, null)[0].Body);
    }
}
