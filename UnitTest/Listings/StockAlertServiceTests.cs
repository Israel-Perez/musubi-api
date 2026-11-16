using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;

namespace UnitTest.Listings;

/// <summary>
/// HU-51: "Avísame" solo en una agotada visible que no es mia; pedirlo otra vez no duplica; hasta 20 por persona;
/// quitarlo es idempotente y se borra con la cuenta (HU-46). Un aviso tomado ya no existe (se usa una vez).
/// </summary>
public sealed class StockAlertServiceTests
{
    private readonly ListingsHarness harness = new();
    private readonly InMemoryStockAlertRepository repository = new();
    private readonly StockAlertService alerts;
    private readonly string ana = AuthIds.New();
    private readonly string valeria = AuthIds.New();

    public StockAlertServiceTests() => alerts = new StockAlertService(repository, harness.Listings, harness.Time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASoldOutProduct_CanBeWatched_AndAskingAgainDoesNotDuplicate()
    {
        var soldOut = await harness.ProductAsync(valeria, stock: 0);

        var before = await alerts.StatusAsync(ana, ListingsHarness.Campus, soldOut.Id, Ct);
        var first = await alerts.SubscribeAsync(ana, ListingsHarness.Campus, soldOut.Id, Ct);
        var again = await alerts.SubscribeAsync(ana, ListingsHarness.Campus, soldOut.Id, Ct);
        var after = await alerts.StatusAsync(ana, ListingsHarness.Campus, soldOut.Id, Ct);

        Assert.False(before.Value!.Active);
        Assert.True(first.Value!.Active);
        Assert.True(again.Value!.Active);
        Assert.True(after.Value!.Active);
        Assert.Equal(1, await repository.CountByUserAsync(ana, ListingsHarness.Now.UtcDateTime, Ct));
    }

    [Fact]
    public async Task AnAvailable_OrMyOwn_OrAnInvisibleProduct_CannotBeWatched()
    {
        var available = await harness.ProductAsync(valeria, stock: 2);
        var mine = await harness.ProductAsync(ana, stock: 0);
        var paused = await harness.ProductAsync(valeria, stock: 0, status: ProductStatuses.Hidden);
        var elsewhere = await harness.ProductAsync(valeria, stock: 0, community: AuthIds.New());

        Assert.Equal(ListingsErrors.ProductAvailable, (await alerts.SubscribeAsync(ana, ListingsHarness.Campus, available.Id, Ct)).Error);
        Assert.Equal(ListingsErrors.OwnProduct, (await alerts.SubscribeAsync(ana, ListingsHarness.Campus, mine.Id, Ct)).Error);
        Assert.Equal(ListingsErrors.ProductNotFound, (await alerts.SubscribeAsync(ana, ListingsHarness.Campus, paused.Id, Ct)).Error);
        Assert.Equal(ListingsErrors.ProductNotFound, (await alerts.SubscribeAsync(ana, ListingsHarness.Campus, elsewhere.Id, Ct)).Error);
        Assert.Equal(ListingsErrors.ProductNotFound, (await alerts.StatusAsync(ana, ListingsHarness.Campus, elsewhere.Id, Ct)).Error);
        Assert.Equal(0, await repository.CountByUserAsync(ana, ListingsHarness.Now.UtcDateTime, Ct));
    }

    [Fact]
    public async Task WithABlock_TheProductDoesNotExist()
    {
        var soldOut = await harness.ProductAsync(valeria, stock: 0);
        await harness.BlockAsync(valeria, ana);

        Assert.Equal(ListingsErrors.ProductNotFound, (await alerts.SubscribeAsync(ana, ListingsHarness.Campus, soldOut.Id, Ct)).Error);
    }

    [Fact]
    public async Task TwentyAtMost_ButRenewingOneStillWorks()
    {
        var watched = new List<string>();
        for (var i = 0; i < StockAlertService.MaxPerUser; i++)
        {
            var product = await harness.ProductAsync(valeria, $"Calculadora {i}", stock: 0);
            Assert.True((await alerts.SubscribeAsync(ana, ListingsHarness.Campus, product.Id, Ct)).Value!.Active);
            watched.Add(product.Id);
        }

        var extra = await harness.ProductAsync(valeria, "Una más", stock: 0);

        Assert.Equal(ListingsErrors.TooManyStockAlerts, (await alerts.SubscribeAsync(ana, ListingsHarness.Campus, extra.Id, Ct)).Error);
        Assert.True((await alerts.SubscribeAsync(ana, ListingsHarness.Campus, watched[0], Ct)).Value!.Active);
    }

    [Fact]
    public async Task Removing_IsIdempotent_Taking_UsesThemOnce_AndTheAccountEraserDeletesThem()
    {
        var (casio, bata) = (await harness.ProductAsync(valeria, stock: 0), await harness.ProductAsync(valeria, "Bata", stock: 0));
        await alerts.SubscribeAsync(ana, ListingsHarness.Campus, casio.Id, Ct);
        await alerts.SubscribeAsync(ana, ListingsHarness.Campus, bata.Id, Ct);

        await alerts.UnsubscribeAsync(ana, casio.Id, Ct);
        await alerts.UnsubscribeAsync(ana, casio.Id, Ct);
        var taken = await alerts.TakeAsync(bata.Id, [valeria], 10, Ct);
        var again = await alerts.TakeAsync(bata.Id, [valeria], 10, Ct);
        await alerts.SubscribeAsync(ana, ListingsHarness.Campus, casio.Id, Ct);
        await ((IAccountDataEraser)alerts).EraseAsync(ana, Ct);

        Assert.Equal(ana, Assert.Single(taken).UserId);
        Assert.Empty(again);
        Assert.Equal(0, await repository.CountByUserAsync(ana, ListingsHarness.Now.UtcDateTime, Ct));
    }

    [Fact]
    public async Task AnAlert_LastsNinetyDays()
    {
        var soldOut = await harness.ProductAsync(valeria, stock: 0);
        await alerts.SubscribeAsync(ana, ListingsHarness.Campus, soldOut.Id, Ct);

        harness.Time.Advance(StockAlertService.Lifetime + TimeSpan.FromMinutes(1));

        Assert.False((await alerts.StatusAsync(ana, ListingsHarness.Campus, soldOut.Id, Ct)).Value!.Active);
    }
}
