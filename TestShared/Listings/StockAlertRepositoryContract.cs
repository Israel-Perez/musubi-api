using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.TestShared.Listings;

/// <summary>
/// HU-51: comportamiento esperado del repositorio de "Avísame". Corre contra el fake en memoria (UnitTest) y contra
/// MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class StockAlertRepositoryContract
{
    // Relativa a hoy: la coleccion tiene TTL y MongoDB borra lo que ya vencio en la fecha real.
    private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(18);

    internal abstract IStockAlertRepository Alerts { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StockAlertDocument Alert(string user, string product, int days = 90) => new()
    {
        Id = AuthIds.New(),
        UserId = user,
        CommunityId = AuthIds.New(),
        ProductId = product,
        CreatedAt = Now,
        ExpiresAt = Now.AddDays(days),
    };

    [Fact]
    public async Task OnePerPersonAndProduct_SavingAgainOnlyRenewsIt()
    {
        var (ana, casio) = (AuthIds.New(), AuthIds.New());

        await Alerts.SaveAsync(Alert(ana, casio, days: 1), Ct);
        await Alerts.SaveAsync(Alert(ana, casio), Ct);
        await Alerts.SaveAsync(Alert(ana, AuthIds.New()), Ct);

        Assert.True(await Alerts.ExistsAsync(ana, casio, Now.AddDays(10), Ct));
        Assert.Equal(2, await Alerts.CountByUserAsync(ana, Now, Ct));
        Assert.False(await Alerts.ExistsAsync(AuthIds.New(), casio, Now, Ct));
    }

    [Fact]
    public async Task Expired_DoNotCount_AndAreNotTaken()
    {
        var (ana, casio) = (AuthIds.New(), AuthIds.New());
        await Alerts.SaveAsync(Alert(ana, casio, days: 1), Ct);

        Assert.False(await Alerts.ExistsAsync(ana, casio, Now.AddDays(2), Ct));
        Assert.Equal(0, await Alerts.CountByUserAsync(ana, Now.AddDays(2), Ct));
        Assert.Empty(await Alerts.TakeAsync(casio, [], Now.AddDays(2), 10, Ct));
    }

    [Fact]
    public async Task Take_ReturnsTheProductsAlerts_WithoutTheExcluded_AndDeletesThem()
    {
        var (ana, luis, diego, casio) = (AuthIds.New(), AuthIds.New(), AuthIds.New(), AuthIds.New());
        await Alerts.SaveAsync(Alert(ana, casio), Ct);
        await Alerts.SaveAsync(Alert(luis, casio), Ct);
        await Alerts.SaveAsync(Alert(diego, casio), Ct);
        await Alerts.SaveAsync(Alert(ana, AuthIds.New()), Ct);

        var taken = await Alerts.TakeAsync(casio, [diego], Now, 10, Ct);
        var again = await Alerts.TakeAsync(casio, [diego], Now, 10, Ct);

        Assert.Equal(new[] { ana, luis }.Order(StringComparer.Ordinal), taken.Select(a => a.UserId).Order(StringComparer.Ordinal));
        Assert.Empty(again);
        // El de quien quedo fuera (un bloqueo) se queda para despues.
        Assert.True(await Alerts.ExistsAsync(diego, casio, Now, Ct));
        Assert.False(await Alerts.ExistsAsync(ana, casio, Now, Ct));
        Assert.Equal(1, await Alerts.CountByUserAsync(ana, Now, Ct));
    }

    [Fact]
    public async Task Take_RespectsTheLimit()
    {
        var casio = AuthIds.New();
        for (var i = 0; i < 3; i++)
        {
            await Alerts.SaveAsync(Alert(AuthIds.New(), casio), Ct);
        }

        Assert.Equal(2, (await Alerts.TakeAsync(casio, [], Now, 2, Ct)).Count);
        Assert.Single(await Alerts.TakeAsync(casio, [], Now, 2, Ct));
    }

    [Fact]
    public async Task Delete_IsIdempotent_AndDeleteByUser_RemovesOnlyTheirs()
    {
        var (ana, luis, casio) = (AuthIds.New(), AuthIds.New(), AuthIds.New());
        await Alerts.SaveAsync(Alert(ana, casio), Ct);
        await Alerts.SaveAsync(Alert(ana, AuthIds.New()), Ct);
        await Alerts.SaveAsync(Alert(luis, casio), Ct);

        Assert.True(await Alerts.DeleteAsync(ana, casio, Ct));
        Assert.False(await Alerts.DeleteAsync(ana, casio, Ct));
        Assert.Equal(1, await Alerts.DeleteByUserAsync(ana, Ct));
        Assert.Equal(0, await Alerts.CountByUserAsync(ana, Now, Ct));
        Assert.True(await Alerts.ExistsAsync(luis, casio, Now, Ct));
    }

    [Fact]
    public async Task InvalidIds_AreIgnored()
    {
        Assert.False(await Alerts.ExistsAsync("no-es-un-id", AuthIds.New(), Now, Ct));
        Assert.Equal(0, await Alerts.CountByUserAsync("no-es-un-id", Now, Ct));
        Assert.False(await Alerts.DeleteAsync("no-es-un-id", "tampoco", Ct));
        Assert.Empty(await Alerts.TakeAsync("no-es-un-id", [], Now, 10, Ct));
    }
}
