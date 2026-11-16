using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.Notifications;
using UnitTest.Common;
using UnitTest.Listings;

namespace UnitTest.Matching;

/// <summary>
/// HU-51: cuando una agotada vuelve a tener piezas, a quien pidio "Avísame" le llega "¡Volvió lo que esperabas!" que
/// abre la publicacion, una sola vez (decision de la usuaria del 16-Nov-2026). Si no volvio, nadie recibe nada; nunca a
/// quien vende ni entre personas bloqueadas (HU-57).
/// </summary>
public sealed class BackInStockTests
{
    private readonly ListingsHarness harness = new();
    private readonly InMemoryStockAlertRepository repository = new();
    private readonly RecordingNotificationCenter center = new();
    private readonly StockAlertService alerts;
    private readonly BackInStockNotifier notifier;
    private readonly string valeria = AuthIds.New();

    public BackInStockTests()
    {
        alerts = new StockAlertService(repository, harness.Listings, harness.Time);
        notifier = new BackInStockNotifier(new ProductDirectory(harness.Products), alerts, harness.BlockDirectory, center);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> WatcherAsync(ProductDocument product)
    {
        var who = AuthIds.New();
        Assert.True((await alerts.SubscribeAsync(who, ListingsHarness.Campus, product.Id, Ct).ConfigureAwait(false)).Value!.Active);
        return who;
    }

    private async Task RestockAsync(ProductDocument product, int stock) =>
        await harness.Products.SetStockAsync(product.Id, product.SellerId, stock, ListingsHarness.Now.UtcDateTime, Ct).ConfigureAwait(false);

    [Fact]
    public async Task WhenItComesBack_EachWatcherIsToldOnce_AndTheNoticeOpensTheProduct()
    {
        var casio = await harness.ProductAsync(valeria, "Calculadora Casio fx-991ES Plus", stock: 0, sellerName: "Valeria Cruz");
        var ana = await WatcherAsync(casio);
        var luis = await WatcherAsync(casio);
        await RestockAsync(casio, 2);

        var told = await notifier.NotifyAsync(casio.Id, Ct);
        var again = await notifier.NotifyAsync(casio.Id, Ct);

        Assert.Equal((2, 0), (told, again));
        Assert.Equal(new[] { ana, luis }.Order(StringComparer.Ordinal), center.Sent.Select(n => n.UserId).Order(StringComparer.Ordinal));
        var notice = center.Sent[0];
        Assert.Equal(NotificationTypes.BackInStock, notice.Type);
        Assert.Equal("¡Volvió lo que esperabas!", notice.Title);
        Assert.Equal("Valeria repuso «Calculadora Casio fx-991ES Plus». Quedan 2.", notice.Body);
        Assert.Equal((NotificationTargets.Product, casio.Id, NotificationCategories.Matches), (notice.TargetType, notice.TargetId, notice.Category));
        Assert.StartsWith("back_in_stock:", notice.DedupeKey, StringComparison.Ordinal);
        Assert.False((await alerts.StatusAsync(ana, ListingsHarness.Campus, casio.Id, Ct)).Value!.Active);
    }

    [Fact]
    public async Task OnePiece_SaysQueda1()
    {
        var casio = await harness.ProductAsync(valeria, stock: 0);
        await WatcherAsync(casio);
        await RestockAsync(casio, 1);

        await notifier.NotifyAsync(casio.Id, Ct);

        Assert.EndsWith("Queda 1.", Assert.Single(center.Sent).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IfItIsStillSoldOut_OrPaused_NobodyIsTold_AndTheAlertsStay()
    {
        var casio = await harness.ProductAsync(valeria, stock: 0);
        var ana = await WatcherAsync(casio);

        var soldOut = await notifier.NotifyAsync(casio.Id, Ct);
        await harness.Products.SetPausedAsync(casio.Id, valeria, paused: true, ListingsHarness.Now.UtcDateTime, Ct);
        await RestockAsync(casio, 3);
        var paused = await notifier.NotifyAsync(casio.Id, Ct);

        Assert.Equal((0, 0), (soldOut, paused));
        Assert.Empty(center.Sent);
        Assert.True(await repository.ExistsAsync(ana, casio.Id, ListingsHarness.Now.UtcDateTime, Ct));
    }

    [Fact]
    public async Task ABlockAfterAsking_KeepsTheNoticeAway()
    {
        var casio = await harness.ProductAsync(valeria, stock: 0);
        var ana = await WatcherAsync(casio);
        var luis = await WatcherAsync(casio);
        await harness.BlockAsync(valeria, ana);
        await RestockAsync(casio, 1);

        await notifier.NotifyAsync(casio.Id, Ct);

        Assert.Equal(luis, Assert.Single(center.Sent).UserId);
    }

    [Fact]
    public async Task AnUnknownProduct_TellsNobody() =>
        Assert.Equal(0, await notifier.NotifyAsync(AuthIds.New(), Ct));
}
