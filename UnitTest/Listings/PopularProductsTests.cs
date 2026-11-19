using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;

namespace UnitTest.Listings;

/// <summary>
/// HU-49 (SCRUM-120: faltaba su prueba unitaria): "Populares" ordena por vistas del periodo (hoy o los ultimos 7 dias),
/// solo del campus de quien mira, con desempate estable y paginas por cursor.
/// </summary>
public sealed class PopularProductsTests
{
    private readonly ListingsHarness harness = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ShowcaseViewer Ana = new("6716a0000000000000000a01", ListingsHarness.Campus);

    private DateTime Today => harness.Time.GetUtcNow().UtcDateTime.Date;

    private async Task ViewAsync(ProductDocument product, int times, int daysAgo = 0)
    {
        for (var i = 0; i < times; i++)
        {
            await harness.Stats.IncrementViewAsync(product.CommunityId, product.Id, Today.AddDays(-daysAgo), Ct).ConfigureAwait(false);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("month")]
    [InlineData("TODAY")]
    public async Task OnlyTodayOrWeek(string? period)
    {
        var result = await harness.Listings.PopularAsync(Ana, null, period, null, null, Ct);

        Assert.Equal(ListingsErrors.InvalidPopularPeriod, result.Error);
    }

    [Fact]
    public async Task Today_CountsOnlyTodaysViews_MostViewedFirst()
    {
        var calculator = await harness.ProductAsync("seller-1", "Calculadora Casio");
        var coat = await harness.ProductAsync("seller-2", "Bata de laboratorio");
        var oldFavorite = await harness.ProductAsync("seller-3", "Juego de escuadras");
        await ViewAsync(calculator, 3);
        await ViewAsync(coat, 1);
        await ViewAsync(oldFavorite, 5, daysAgo: 3);

        var page = (await harness.Listings.PopularAsync(Ana, null, "today", null, null, Ct)).Value!;

        Assert.Equal([(calculator.Id, 3L), (coat.Id, 1L)], page.Items.Select(i => (i.Product.Id, i.Views)));
        Assert.Equal(2, page.Total);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Week_AddsTheLastSevenDays_ButNotOlder()
    {
        var calculator = await harness.ProductAsync("seller-1", "Calculadora Casio");
        var coat = await harness.ProductAsync("seller-2", "Bata de laboratorio");
        var tooOld = await harness.ProductAsync("seller-3", "Juego de escuadras");
        await ViewAsync(calculator, 2);
        await ViewAsync(coat, 4, daysAgo: 6);
        await ViewAsync(tooOld, 9, daysAgo: 7);

        var page = (await harness.Listings.PopularAsync(Ana, null, "week", null, null, Ct)).Value!;

        Assert.Equal([coat.Id, calculator.Id], page.Items.Select(i => i.Product.Id));
    }

    [Fact]
    public async Task OtherCampus_NeverShows()
    {
        var mine = await harness.ProductAsync("seller-1", "Calculadora Casio");
        var elsewhere = await harness.ProductAsync("seller-9", "Calculadora de otro campus", community: "000000000000000000000099");
        await ViewAsync(mine, 1);
        await ViewAsync(elsewhere, 50);

        var page = (await harness.Listings.PopularAsync(Ana, null, "today", null, null, Ct)).Value!;

        Assert.Equal([mine.Id], page.Items.Select(i => i.Product.Id));
    }

    [Fact]
    public async Task Pages_ContinueWithTheCursor_WithoutRepeating()
    {
        var first = await harness.ProductAsync("seller-1", "Calculadora Casio");
        var second = await harness.ProductAsync("seller-2", "Bata de laboratorio");
        await ViewAsync(first, 2);
        await ViewAsync(second, 1);

        var one = (await harness.Listings.PopularAsync(Ana, null, "today", null, 1, Ct)).Value!;
        var two = (await harness.Listings.PopularAsync(Ana, null, "today", one.NextCursor, 1, Ct)).Value!;

        Assert.Equal([first.Id], one.Items.Select(i => i.Product.Id));
        Assert.NotNull(one.NextCursor);
        Assert.Equal([second.Id], two.Items.Select(i => i.Product.Id));
        Assert.Null(two.NextCursor);
    }

    [Theory]
    [InlineData("no-es-cursor")]
    [InlineData("MXwyfGFiYw")]
    public async Task ABrokenCursor_Is400(string cursor)
    {
        var result = await harness.Listings.PopularAsync(Ana, null, "today", cursor, null, Ct);

        Assert.Equal(ListingsErrors.InvalidCursor, result.Error);
    }

    [Fact]
    public async Task AVisitor_NeedsAKnownCampus()
    {
        var visitor = new ShowcaseViewer(null, null);

        var unknown = await harness.Listings.PopularAsync(visitor, "no-existe", "today", null, null, Ct);
        var uttt = await harness.Listings.PopularAsync(visitor, "uttt", "today", null, null, Ct);

        Assert.Equal(ListingsErrors.UnknownCampus, unknown.Error);
        Assert.Null(uttt.Error);
    }
}
