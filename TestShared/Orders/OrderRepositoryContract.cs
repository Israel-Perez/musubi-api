using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Orders.Data;

namespace Musubi.TestShared.Orders;

/// <summary>
/// HU-23: comportamiento esperado del repositorio de pedidos. Corre contra el fake en memoria (UnitTest) y contra
/// MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa sus propios ids.
/// </summary>
public abstract class OrderRepositoryContract
{
    private static readonly DateTime Now = new(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc);

    internal abstract IOrderRepository Orders { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OrderDocument Order(
        string? conversationId = null,
        string? buyerId = null,
        string? sellerId = null,
        string? productId = null,
        string status = OrderStatuses.Pending,
        bool paid = false) => new()
        {
            Id = AuthIds.New(),
            CommunityId = AuthIds.New(),
            Number = 1001,
            BuyerId = buyerId ?? AuthIds.New(),
            SellerId = sellerId ?? AuthIds.New(),
            BuyerName = "Ana Sofía Ramírez",
            SellerName = "Diego Hernández",
            ConversationId = conversationId ?? AuthIds.New(),
            Item = new OrderItem
            {
                Source = OrderSources.Product,
                SourceId = productId ?? AuthIds.New(),
                ProductId = productId,
                Title = "Calculadora Casio fx-991ES Plus",
                PriceCents = 50_000,
                Quantity = 1,
                TotalCents = 50_000,
            },
            Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", Detail = "Planta baja", At = Now.AddDays(1) },
            Payment = paid ? new OrderPayment { Method = PaymentMethods.Cash, ChosenAt = Now } : null,
            Status = status,
            History = [new OrderTransition { Status = status, At = Now }],
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    [Fact]
    public async Task NextNumber_IsConsecutivePerCampus_From1001()
    {
        var campus = AuthIds.New();
        var other = AuthIds.New();

        var first = await Orders.NextNumberAsync(campus, Ct);
        var second = await Orders.NextNumberAsync(campus, Ct);
        var elsewhere = await Orders.NextNumberAsync(other, Ct);

        Assert.Equal(OrderNumbers.First, first);
        Assert.Equal(OrderNumbers.First + 1, second);
        Assert.Equal(OrderNumbers.First, elsewhere);
    }

    [Fact]
    public async Task TryInsert_OnlyOneOpenOrderPerConversation()
    {
        var conversation = AuthIds.New();

        var cancelled = await Orders.TryInsertAsync(Order(conversation, status: OrderStatuses.Cancelled), Ct);
        var open = await Orders.TryInsertAsync(Order(conversation), Ct);
        var twice = await Orders.TryInsertAsync(Order(conversation), Ct);
        var confirmed = await Orders.TryInsertAsync(Order(conversation, status: OrderStatuses.Confirmed), Ct);
        var elsewhere = await Orders.TryInsertAsync(Order(), Ct);

        Assert.True(cancelled);
        Assert.True(open);
        Assert.False(twice);
        Assert.False(confirmed);
        Assert.True(elsewhere);
        Assert.Equal(2, (await Orders.FindByConversationAsync(conversation, 10, Ct)).Count);
    }

    [Fact]
    public async Task FindById_RoundTripsTheDocument_AndUnknownIsNull()
    {
        var order = Order(productId: AuthIds.New());
        await Orders.TryInsertAsync(order, Ct);

        var found = await Orders.FindByIdAsync(order.Id, Ct);

        Assert.NotNull(found);
        Assert.Equal(order.Item.ProductId, found.Item.ProductId);
        Assert.Equal("Planta baja", found.Delivery.Detail);
        Assert.Equal(order.Delivery.At, found.Delivery.At);
        Assert.Null(found.Payment);
        Assert.Single(found.History);
        Assert.Null(await Orders.FindByIdAsync(AuthIds.New(), Ct));
        Assert.Null(await Orders.FindByIdAsync("no-es-un-id", Ct));
    }

    [Fact]
    public async Task FindByConversation_IsNewestFirst_OnlyThatConversation_AndLimited()
    {
        var conversation = AuthIds.New();
        var older = Order(conversation, status: OrderStatuses.Cancelled);
        var newer = Order(conversation);
        await Orders.TryInsertAsync(older, Ct);
        await Orders.TryInsertAsync(newer, Ct);
        await Orders.TryInsertAsync(Order(), Ct);

        var all = await Orders.FindByConversationAsync(conversation, 10, Ct);
        var one = await Orders.FindByConversationAsync(conversation, 1, Ct);

        Assert.Equal([newer.Id, older.Id], all.Select(o => o.Id));
        Assert.Equal([newer.Id], one.Select(o => o.Id));
        Assert.Empty(await Orders.FindByConversationAsync("no-es-un-id", 10, Ct));
    }

    [Fact]
    public async Task SetPayment_OnlyTheBuyer_AndOnlyWhilePending()
    {
        var pending = Order();
        var confirmed = Order(status: OrderStatuses.Confirmed);
        await Orders.TryInsertAsync(pending, Ct);
        await Orders.TryInsertAsync(confirmed, Ct);

        var bySeller = await Orders.SetPaymentAsync(pending.Id, pending.SellerId, PaymentMethods.Cash, Now, Ct);
        var paid = await Orders.SetPaymentAsync(pending.Id, pending.BuyerId, PaymentMethods.Cash, Now.AddMinutes(5), Ct);
        var late = await Orders.SetPaymentAsync(confirmed.Id, confirmed.BuyerId, PaymentMethods.Cash, Now, Ct);

        Assert.Null(bySeller);
        Assert.NotNull(paid);
        Assert.Equal(PaymentMethods.Cash, paid.Payment?.Method);
        Assert.Equal(Now.AddMinutes(5), paid.Payment?.ChosenAt);
        Assert.Equal(PaymentMethods.Cash, (await Orders.FindByIdAsync(pending.Id, Ct))?.Payment?.Method);
        Assert.Null(late);
    }

    [Fact]
    public async Task HasOpenForProduct_CountsPendingAndConfirmed_NotClosed()
    {
        var withPending = AuthIds.New();
        var withConfirmed = AuthIds.New();
        var onlyCancelled = AuthIds.New();
        await Orders.TryInsertAsync(Order(productId: withPending), Ct);
        await Orders.TryInsertAsync(Order(productId: withConfirmed, status: OrderStatuses.Confirmed), Ct);
        await Orders.TryInsertAsync(Order(productId: onlyCancelled, status: OrderStatuses.Cancelled), Ct);
        await Orders.TryInsertAsync(Order(productId: onlyCancelled, status: OrderStatuses.Completed), Ct);

        Assert.True(await Orders.HasOpenForProductAsync(withPending, Ct));
        Assert.True(await Orders.HasOpenForProductAsync(withConfirmed, Ct));
        Assert.False(await Orders.HasOpenForProductAsync(onlyCancelled, Ct));
        Assert.False(await Orders.HasOpenForProductAsync(AuthIds.New(), Ct));
    }

    [Fact]
    public async Task ReleaseAccount_CancelsOpenOrders_AndReplacesTheName()
    {
        var gone = AuthIds.New();
        var bought = Order(buyerId: gone);
        var sold = Order(sellerId: gone, status: OrderStatuses.Confirmed);
        var done = Order(buyerId: gone, status: OrderStatuses.Completed);
        var others = Order();
        foreach (var order in new[] { bought, sold, done, others })
        {
            await Orders.TryInsertAsync(order, Ct);
        }

        var cancelled = await Orders.ReleaseAccountAsync(gone, "Usuario eliminado", Now.AddHours(1), Ct);

        Assert.Equal(2, cancelled);
        var afterBought = await Orders.FindByIdAsync(bought.Id, Ct);
        Assert.Equal(OrderStatuses.Cancelled, afterBought?.Status);
        Assert.Equal("Usuario eliminado", afterBought?.BuyerName);
        Assert.Equal("Diego Hernández", afterBought?.SellerName);
        Assert.Equal(CancelReasons.AccountDeleted, afterBought?.History[^1].Reason);
        var afterSold = await Orders.FindByIdAsync(sold.Id, Ct);
        Assert.Equal(OrderStatuses.Cancelled, afterSold?.Status);
        Assert.Equal("Usuario eliminado", afterSold?.SellerName);
        var afterDone = await Orders.FindByIdAsync(done.Id, Ct);
        Assert.Equal(OrderStatuses.Completed, afterDone?.Status);
        Assert.Equal("Usuario eliminado", afterDone?.BuyerName);
        Assert.Single(afterDone!.History);
        var untouched = await Orders.FindByIdAsync(others.Id, Ct);
        Assert.Equal(OrderStatuses.Pending, untouched?.Status);
        Assert.Equal("Ana Sofía Ramírez", untouched?.BuyerName);
        Assert.Equal((OrderRoles.System, CancelReasons.AccountDeleted), (afterSold!.Cancellation!.By, afterSold.Cancellation.Reason));
    }

    [Fact]
    public async Task Accept_OnlyTheSeller_OnlyPending_AndOnlyWithThePayment()
    {
        var seller = AuthIds.New();
        var order = Order(sellerId: seller, paid: true);
        var unpaid = Order(sellerId: seller);
        await Orders.TryInsertAsync(order, Ct);
        await Orders.TryInsertAsync(unpaid, Ct);

        Assert.Null(await Orders.AcceptAsync(order.Id, order.BuyerId, Now, Ct));
        Assert.Null(await Orders.AcceptAsync(unpaid.Id, seller, Now, Ct));
        Assert.Null(await Orders.AcceptAsync("no-es-un-id", seller, Now, Ct));
        var accepted = await Orders.AcceptAsync(order.Id, seller, Now.AddMinutes(5), Ct);
        var twice = await Orders.AcceptAsync(order.Id, seller, Now.AddMinutes(6), Ct);

        Assert.Equal(OrderStatuses.Confirmed, accepted?.Status);
        Assert.Equal([OrderStatuses.Pending, OrderStatuses.Confirmed], accepted!.History.Select(h => h.Status));
        Assert.Equal(Now.AddMinutes(5), accepted.UpdatedAt);
        Assert.Null(twice);
    }

    [Fact]
    public async Task Cancel_ByItsParty_KeepsWhoAndWhy_AndAClosedOneDoesNotChange()
    {
        var buyer = AuthIds.New();
        var order = Order(buyerId: buyer, status: OrderStatuses.Confirmed);
        await Orders.TryInsertAsync(order, Ct);

        Assert.Null(await Orders.CancelAsync(order.Id, buyer, OrderRoles.Seller, CancelReasons.Other, Now, Ct));
        Assert.Null(await Orders.CancelAsync(order.Id, AuthIds.New(), OrderRoles.Buyer, CancelReasons.Other, Now, Ct));
        var cancelled = await Orders.CancelAsync(order.Id, buyer, OrderRoles.Buyer, CancelReasons.NoShow, Now.AddHours(1), Ct);
        var again = await Orders.CancelAsync(order.Id, buyer, OrderRoles.Buyer, CancelReasons.Other, Now.AddHours(2), Ct);

        Assert.Equal(OrderStatuses.Cancelled, cancelled?.Status);
        Assert.Equal((OrderRoles.Buyer, CancelReasons.NoShow, Now.AddHours(1)), (cancelled!.Cancellation!.By, cancelled.Cancellation.Reason, cancelled.Cancellation.At));
        Assert.Equal((OrderStatuses.Confirmed, CancelReasons.NoShow), (cancelled.History[^2].Status, cancelled.History[^1].Reason));
        Assert.Null(again);
        var read = await Orders.FindByIdAsync(order.Id, Ct);
        Assert.Equal(CancelReasons.NoShow, read!.Cancellation!.Reason);
    }

    [Fact]
    public async Task Handover_KeepsTheFirstTime_AndCompletesOnlyWithBothSides()
    {
        var buyer = AuthIds.New();
        var seller = AuthIds.New();
        var order = Order(buyerId: buyer, sellerId: seller, status: OrderStatuses.Confirmed);
        var pending = Order(buyerId: buyer, sellerId: seller);
        await Orders.TryInsertAsync(order, Ct);
        await Orders.TryInsertAsync(pending, Ct);

        Assert.Null(await Orders.ConfirmHandoverAsync(pending.Id, seller, OrderRoles.Seller, Now, Ct));
        Assert.Null(await Orders.ConfirmHandoverAsync(order.Id, buyer, OrderRoles.Seller, Now, Ct));
        var delivered = await Orders.ConfirmHandoverAsync(order.Id, seller, OrderRoles.Seller, Now, Ct);
        var repeated = await Orders.ConfirmHandoverAsync(order.Id, seller, OrderRoles.Seller, Now.AddHours(1), Ct);
        var early = await Orders.CompleteAsync(order.Id, Now, Ct);
        var received = await Orders.ConfirmHandoverAsync(order.Id, buyer, OrderRoles.Buyer, Now.AddHours(2), Ct);
        var completed = await Orders.CompleteAsync(order.Id, Now.AddHours(2), Ct);
        var twice = await Orders.CompleteAsync(order.Id, Now.AddHours(3), Ct);

        Assert.Equal(((DateTime?)Now, (DateTime?)null), (delivered!.Handover!.SellerAt, delivered.Handover.BuyerAt));
        Assert.Equal(Now, repeated!.Handover!.SellerAt);
        Assert.Null(early);
        Assert.Equal(((DateTime?)Now, (DateTime?)Now.AddHours(2)), (received!.Handover!.SellerAt, received.Handover.BuyerAt));
        Assert.Equal(OrderStatuses.Completed, completed?.Status);
        Assert.Equal(OrderStatuses.Completed, completed!.History[^1].Status);
        Assert.Null(twice);
        Assert.Null(await Orders.ConfirmHandoverAsync(order.Id, buyer, OrderRoles.Buyer, Now.AddHours(4), Ct));
    }

    [Fact]
    public async Task FindConfirmedByUser_OnlyItsConfirmedOrders()
    {
        var user = AuthIds.New();
        var asBuyer = Order(buyerId: user, status: OrderStatuses.Confirmed);
        var asSeller = Order(sellerId: user, status: OrderStatuses.Confirmed);
        var pending = Order(buyerId: user);
        var other = Order(status: OrderStatuses.Confirmed);
        foreach (var order in new[] { asBuyer, asSeller, pending, other })
        {
            await Orders.TryInsertAsync(order, Ct);
        }

        var found = await Orders.FindConfirmedByUserAsync(user, Ct);

        Assert.Equal(new[] { asBuyer.Id, asSeller.Id }.Order(StringComparer.Ordinal), found.Select(o => o.Id).Order(StringComparer.Ordinal));
        Assert.Empty(await Orders.FindConfirmedByUserAsync("no-es-un-id", Ct));
    }
}
