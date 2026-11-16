using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Orders.Data;

namespace Musubi.TestShared.Orders;

/// <summary>
/// HU-52: comportamiento esperado del repositorio de apartados. Corre contra el fake en memoria (UnitTest) y contra
/// MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class ReservationRepositoryContract
{
    private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(18);

    internal abstract IReservationRepository Reservations { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ReservationDocument Reservation(string? buyerId = null, string? sellerId = null, DateTime? createdAt = null)
    {
        var at = createdAt ?? Now;
        return new ReservationDocument
        {
            Id = AuthIds.New(),
            CommunityId = AuthIds.New(),
            Number = 1001,
            BuyerId = buyerId ?? AuthIds.New(),
            SellerId = sellerId ?? AuthIds.New(),
            BuyerName = "Luis Martínez",
            SellerName = "Valeria Cruz",
            ConversationId = AuthIds.New(),
            ProductId = AuthIds.New(),
            Title = "Juego de escuadras y compás",
            PriceCents = 18_000,
            PointSlug = "biblioteca",
            PointName = "Biblioteca",
            PickupAt = at.AddHours(3),
            Code = "482173",
            CreatedAt = at,
            ExpiresAt = at.AddHours(24),
            UpdatedAt = at,
        };
    }

    [Fact]
    public async Task NextNumber_IsConsecutivePerCampus_From1001()
    {
        var campus = AuthIds.New();

        var first = await Reservations.NextNumberAsync(campus, Ct);
        var second = await Reservations.NextNumberAsync(campus, Ct);

        Assert.Equal((1001, 1002), (first, second));
        Assert.Equal(1001, await Reservations.NextNumberAsync(AuthIds.New(), Ct));
    }

    [Fact]
    public async Task OnlyOneActive_PerBuyer_ButAnotherAfterItEnds()
    {
        var luis = AuthIds.New();
        var first = Reservation(buyerId: luis);
        var second = Reservation(buyerId: luis);
        var third = Reservation(buyerId: luis);

        Assert.True(await Reservations.TryInsertAsync(first, Ct));
        Assert.False(await Reservations.TryInsertAsync(second, Ct));
        Assert.NotNull(await Reservations.CancelAsync(first.Id, luis, OrderRoles.Buyer, Now, Ct));
        Assert.True(await Reservations.TryInsertAsync(third, Ct));
        Assert.True(await Reservations.TryInsertAsync(Reservation(), Ct));
    }

    [Fact]
    public async Task ByUser_SplitsWhatIReserved_FromWhatWasReservedFromMe()
    {
        var valeria = AuthIds.New();
        var mine = Reservation(sellerId: valeria);
        var other = Reservation(sellerId: valeria);
        var bought = Reservation(buyerId: valeria);
        foreach (var r in new[] { mine, other, bought })
        {
            await Reservations.TryInsertAsync(r, Ct);
        }

        var sold = await Reservations.FindByUserAsync(valeria, asSeller: true, 10, Ct);
        var reserved = await Reservations.FindByUserAsync(valeria, asSeller: false, 10, Ct);

        Assert.Equal([other.Id, mine.Id], sold.Select(r => r.Id));
        Assert.Equal([bought.Id], reserved.Select(r => r.Id));
        Assert.Equal((1L, 2L), await Reservations.CountByUserAsync(valeria, Ct));
        Assert.Empty(await Reservations.FindByUserAsync("no-es-un-id", asSeller: true, 10, Ct));
    }

    [Fact]
    public async Task Attempts_AreCountedUpToTheLimit_AndOnlyByTheSeller()
    {
        var reservation = Reservation();
        await Reservations.TryInsertAsync(reservation, Ct);

        Assert.Null(await Reservations.UseAttemptAsync(reservation.Id, reservation.BuyerId, 2, Ct));
        var first = await Reservations.UseAttemptAsync(reservation.Id, reservation.SellerId, 2, Ct);
        var second = await Reservations.UseAttemptAsync(reservation.Id, reservation.SellerId, 2, Ct);
        var third = await Reservations.UseAttemptAsync(reservation.Id, reservation.SellerId, 2, Ct);

        Assert.Equal((1, 2), (first?.CodeAttempts, second?.CodeAttempts));
        Assert.Null(third);
    }

    [Fact]
    public async Task Verified_ThenCollected_OnlyOnce()
    {
        var reservation = Reservation();
        await Reservations.TryInsertAsync(reservation, Ct);
        var orderId = AuthIds.New();

        Assert.Null(await Reservations.MarkCollectedAsync(reservation.Id, reservation.SellerId, orderId, Now, Ct));
        var verified = await Reservations.MarkVerifiedAsync(reservation.Id, reservation.SellerId, Now.AddMinutes(1), Ct);
        var again = await Reservations.MarkVerifiedAsync(reservation.Id, reservation.SellerId, Now.AddMinutes(2), Ct);
        var collected = await Reservations.MarkCollectedAsync(reservation.Id, reservation.SellerId, orderId, Now.AddMinutes(3), Ct);
        var twice = await Reservations.MarkCollectedAsync(reservation.Id, reservation.SellerId, AuthIds.New(), Now.AddMinutes(4), Ct);

        Assert.Equal(Now.AddMinutes(1), verified?.VerifiedAt);
        Assert.Equal(Now.AddMinutes(1), again?.VerifiedAt);
        Assert.Equal((ReservationStatuses.Collected, orderId), (collected?.Status, collected?.OrderId));
        Assert.Null(twice);
    }

    [Fact]
    public async Task Cancel_ByItsParty_KeepsWho()
    {
        var reservation = Reservation();
        await Reservations.TryInsertAsync(reservation, Ct);

        Assert.Null(await Reservations.CancelAsync(reservation.Id, reservation.BuyerId, OrderRoles.Seller, Now, Ct));
        var cancelled = await Reservations.CancelAsync(reservation.Id, reservation.SellerId, OrderRoles.Seller, Now.AddMinutes(1), Ct);

        Assert.Equal((ReservationStatuses.Cancelled, OrderRoles.Seller), (cancelled?.Status, cancelled?.CancelledBy));
        Assert.Null(await Reservations.CancelAsync(reservation.Id, reservation.SellerId, OrderRoles.Seller, Now.AddMinutes(2), Ct));
    }

    [Fact]
    public async Task Expired_AreTheActiveOnesPastTheirDay_MarkedOnce()
    {
        var old = Reservation(createdAt: Now.AddHours(-30));
        var fresh = Reservation(createdAt: Now.AddHours(-2));
        await Reservations.TryInsertAsync(old, Ct);
        await Reservations.TryInsertAsync(fresh, Ct);

        var due = await Reservations.FindExpiredAsync(Now, 50, Ct);

        Assert.Contains(old.Id, due.Select(r => r.Id));
        Assert.DoesNotContain(fresh.Id, due.Select(r => r.Id));
        Assert.True(await Reservations.MarkExpiredAsync(old.Id, Now, Ct));
        Assert.False(await Reservations.MarkExpiredAsync(old.Id, Now, Ct));
        Assert.Equal(ReservationStatuses.Expired, (await Reservations.FindByIdAsync(old.Id, Ct))?.Status);
        Assert.Equal([fresh.Id], (await Reservations.FindActiveByUserAsync(fresh.SellerId, Ct)).Select(r => r.Id));
    }
}
