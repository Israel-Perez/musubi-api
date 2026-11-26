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
        bool paid = false,
        string? method = null,
        DateTime? deliveryAt = null) => new()
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
            Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", Detail = "Planta baja", At = deliveryAt ?? Now.AddDays(1) },
            Payment = paid || method is not null ? new OrderPayment { Method = method ?? PaymentMethods.Cash, ChosenAt = Now } : null,
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

        var transfer = Order(sellerId: seller, method: PaymentMethods.Transfer);
        await Orders.TryInsertAsync(transfer, Ct);

        Assert.Null(await Orders.AcceptAsync(order.Id, order.BuyerId, Now, "1234", Ct));
        Assert.Null(await Orders.AcceptAsync(unpaid.Id, seller, Now, "1234", Ct));
        Assert.Null(await Orders.AcceptAsync("no-es-un-id", seller, Now, "1234", Ct));
        // HU-55: la transferencia se acepta al validarla (ConfirmTransferAsync), no aqui.
        Assert.Null(await Orders.AcceptAsync(transfer.Id, seller, Now, "1234", Ct));
        var accepted = await Orders.AcceptAsync(order.Id, seller, Now.AddMinutes(5), "0731", Ct);
        var twice = await Orders.AcceptAsync(order.Id, seller, Now.AddMinutes(6), "9999", Ct);

        Assert.Equal(OrderStatuses.Confirmed, accepted?.Status);
        Assert.Equal("0731", accepted?.Payment?.DeliveryCode);
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

    [Fact]
    public async Task FindByBuyer_OnlyItsPurchases_NewestFirst_ByStatus_AndCounts()
    {
        // HU-25: "Mis pedidos" pide los de quien compra, por pestana y con cursor.
        var buyer = AuthIds.New();
        var pending = Order(buyerId: buyer);
        var confirmed = Order(buyerId: buyer, status: OrderStatuses.Confirmed);
        var completed = Order(buyerId: buyer, status: OrderStatuses.Completed);
        var cancelled = Order(buyerId: buyer, status: OrderStatuses.Cancelled);
        var asSeller = Order(sellerId: buyer);
        var other = Order();
        foreach (var order in new[] { pending, confirmed, completed, cancelled, asSeller, other })
        {
            await Orders.TryInsertAsync(order, Ct);
        }

        var all = await Orders.FindByBuyerAsync(buyer, null, null, 10, Ct);
        var active = await Orders.FindByBuyerAsync(buyer, [OrderStatuses.Pending, OrderStatuses.Confirmed], null, 10, Ct);
        var firstTwo = await Orders.FindByBuyerAsync(buyer, null, null, 2, Ct);
        var rest = await Orders.FindByBuyerAsync(buyer, null, firstTwo[^1].Id, 10, Ct);
        var counts = await Orders.CountByBuyerAsync(buyer, Ct);

        Assert.Equal([cancelled.Id, completed.Id, confirmed.Id, pending.Id], all.Select(o => o.Id));
        Assert.Equal([confirmed.Id, pending.Id], active.Select(o => o.Id));
        Assert.Equal([confirmed.Id, pending.Id], rest.Select(o => o.Id));
        Assert.Equal(4, counts.Values.Sum());
        Assert.Equal(1, counts[OrderStatuses.Cancelled]);
        Assert.Empty(await Orders.FindByBuyerAsync(AuthIds.New(), null, null, 10, Ct));
        Assert.Empty(await Orders.CountByBuyerAsync(AuthIds.New(), Ct));
    }

    [Fact]
    public async Task MarkRated_OncePerSide_OnlyACompletedOrder_OnlyItsParties()
    {
        // HU-27/HU-45 (RN-24): cada parte califica una vez y solo un pedido completado.
        var buyer = AuthIds.New();
        var seller = AuthIds.New();
        var order = Order(buyerId: buyer, sellerId: seller, status: OrderStatuses.Completed);
        var open = Order(buyerId: buyer, sellerId: seller, status: OrderStatuses.Confirmed);
        await Orders.TryInsertAsync(order, Ct);
        await Orders.TryInsertAsync(open, Ct);

        Assert.False(await Orders.MarkRatedAsync(order.Id, AuthIds.New(), OrderRoles.Buyer, Now, Ct));
        Assert.False(await Orders.MarkRatedAsync(order.Id, seller, OrderRoles.Buyer, Now, Ct));
        Assert.False(await Orders.MarkRatedAsync(open.Id, buyer, OrderRoles.Buyer, Now, Ct));
        Assert.True(await Orders.MarkRatedAsync(order.Id, buyer, OrderRoles.Buyer, Now, Ct));
        Assert.False(await Orders.MarkRatedAsync(order.Id, buyer, OrderRoles.Buyer, Now.AddHours(1), Ct));
        Assert.True(await Orders.MarkRatedAsync(order.Id, seller, OrderRoles.Seller, Now.AddHours(2), Ct));
        Assert.False(await Orders.MarkRatedAsync("no-es-un-id", buyer, OrderRoles.Buyer, Now, Ct));

        var rated = await Orders.FindByIdAsync(order.Id, Ct);
        Assert.Equal(((DateTime?)Now, (DateTime?)Now.AddHours(2)), (rated!.Ratings!.BuyerAt, rated.Ratings.SellerAt));
        Assert.Equal(Now + OrderRatingWindow.Length, OrderRatingWindow.Until(rated));
        Assert.Null(OrderRatingWindow.Until(open));
    }

    [Fact]
    public async Task Sales_OnlyTheCompletedOfTheSeller_WithTheSummaryAndTheirProducts()
    {
        // HU-26: ventas completadas (con cursor), su numero y total, y las publicaciones que vendieron.
        var seller = AuthIds.New();
        var product = AuthIds.New();
        var sold = Order(sellerId: seller, productId: product, status: OrderStatuses.Completed);
        var direct = Order(sellerId: seller, status: OrderStatuses.Completed);
        var open = Order(sellerId: seller, productId: AuthIds.New(), status: OrderStatuses.Confirmed);
        var bought = Order(buyerId: seller, productId: AuthIds.New(), status: OrderStatuses.Completed);
        foreach (var order in new[] { sold, direct, open, bought })
        {
            await Orders.TryInsertAsync(order, Ct);
        }

        var page = await Orders.FindSalesAsync(seller, null, 1, Ct);
        var rest = await Orders.FindSalesAsync(seller, page[^1].Id, 10, Ct);
        var summary = await Orders.SalesSummaryAsync(seller, Ct);

        Assert.Equal([direct.Id], page.Select(o => o.Id));
        Assert.Equal([sold.Id], rest.Select(o => o.Id));
        Assert.Equal((2L, 100_000L), summary);
        Assert.Equal([product], await Orders.SoldProductIdsAsync(seller, Ct));
        Assert.Equal((0L, 0L), await Orders.SalesSummaryAsync(AuthIds.New(), Ct));
    }

    [Fact]
    public async Task Transfer_TheBuyerReports_TheSellerSaysNotYet_AndThenConfirms()
    {
        var buyer = AuthIds.New();
        var seller = AuthIds.New();
        var order = Order(buyerId: buyer, sellerId: seller, method: PaymentMethods.Transfer);
        var cash = Order(buyerId: buyer, sellerId: seller, paid: true);
        await Orders.TryInsertAsync(order, Ct);
        await Orders.TryInsertAsync(cash, Ct);

        // Antes de avisar, quien vende no puede decir "no me llega" ni confirmar.
        Assert.Null(await Orders.MarkTransferNotReceivedAsync(order.Id, seller, Now, Ct));
        Assert.Null(await Orders.ConfirmTransferAsync(order.Id, seller, Now, Ct));
        Assert.Null(await Orders.ReportTransferAsync(order.Id, seller, "4829173", Now, Ct));
        Assert.Null(await Orders.ReportTransferAsync(cash.Id, buyer, "4829173", Now, Ct));
        var reported = await Orders.ReportTransferAsync(order.Id, buyer, "4829173", Now.AddMinutes(1), Ct);
        Assert.Equal(("4829173", Now.AddMinutes(1)), (reported?.Payment?.Reference, reported?.Payment?.ReportedAt));
        // Ya aviso: cambiar la forma de pago dejaria el dinero sin pedido.
        Assert.Null(await Orders.SetPaymentAsync(order.Id, buyer, PaymentMethods.Cash, Now.AddMinutes(2), Ct));

        Assert.Null(await Orders.MarkTransferNotReceivedAsync(order.Id, buyer, Now, Ct));
        var notYet = await Orders.MarkTransferNotReceivedAsync(order.Id, seller, Now.AddMinutes(3), Ct);
        Assert.Equal(Now.AddMinutes(3), notYet?.Payment?.NotReceivedAt);
        var again = await Orders.ReportTransferAsync(order.Id, buyer, "4829174", Now.AddMinutes(4), Ct);
        Assert.Equal(("4829174", null), (again?.Payment?.Reference, again?.Payment?.NotReceivedAt));

        Assert.Null(await Orders.ConfirmTransferAsync(order.Id, buyer, Now, Ct));
        var confirmed = await Orders.ConfirmTransferAsync(order.Id, seller, Now.AddMinutes(5), Ct);
        Assert.Equal((OrderStatuses.Confirmed, Now.AddMinutes(5)), (confirmed?.Status, confirmed?.Payment?.ValidatedAt));
        Assert.Equal([OrderStatuses.Pending, OrderStatuses.Confirmed], confirmed!.History.Select(h => h.Status));
        Assert.Null(await Orders.ConfirmTransferAsync(order.Id, seller, Now.AddMinutes(6), Ct));
        Assert.Null(await Orders.ReportTransferAsync(order.Id, buyer, "4829175", Now.AddMinutes(7), Ct));
    }

    [Fact]
    public async Task DeliveryCode_CountsEachAttempt_UpToTheLimit()
    {
        var seller = AuthIds.New();
        var order = Order(sellerId: seller, paid: true);
        await Orders.TryInsertAsync(order, Ct);
        await Orders.AcceptAsync(order.Id, seller, Now, "7319", Ct);

        Assert.Null(await Orders.UseCodeAttemptAsync(order.Id, order.BuyerId, 3, Ct));
        var counts = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            counts.Add((await Orders.UseCodeAttemptAsync(order.Id, seller, 3, Ct))?.Payment?.CodeAttempts ?? -1);
        }

        Assert.Equal([1, 2, 3, -1], counts);
        Assert.Equal("7319", (await Orders.FindByIdAsync(order.Id, Ct))?.Payment?.DeliveryCode);
    }

    [Fact]
    public async Task CompleteWithCode_CountsAsBothHandovers_AndKeepsAnEarlierOne()
    {
        var buyer = AuthIds.New();
        var seller = AuthIds.New();
        var order = Order(buyerId: buyer, sellerId: seller, paid: true);
        var transfer = Order(buyerId: buyer, sellerId: seller, method: PaymentMethods.Transfer, status: OrderStatuses.Confirmed);
        await Orders.TryInsertAsync(order, Ct);
        await Orders.TryInsertAsync(transfer, Ct);
        await Orders.AcceptAsync(order.Id, seller, Now, "7319", Ct);
        await Orders.ConfirmHandoverAsync(order.Id, buyer, OrderRoles.Buyer, Now.AddMinutes(1), Ct);

        Assert.Null(await Orders.CompleteWithCodeAsync(order.Id, buyer, Now.AddMinutes(2), Ct));
        Assert.Null(await Orders.CompleteWithCodeAsync(transfer.Id, seller, Now.AddMinutes(2), Ct));
        var completed = await Orders.CompleteWithCodeAsync(order.Id, seller, Now.AddMinutes(2), Ct);

        Assert.Equal(OrderStatuses.Completed, completed?.Status);
        Assert.Equal((Now.AddMinutes(1), Now.AddMinutes(2)), (completed?.Handover?.BuyerAt, completed?.Handover?.SellerAt));
        Assert.Equal(OrderStatuses.Completed, completed!.History[^1].Status);
        Assert.Null(await Orders.CompleteWithCodeAsync(order.Id, seller, Now.AddMinutes(3), Ct));
    }

    [Fact]
    public async Task DeliveriesBetween_OnlyConfirmedInsideTheWindow_ClosestFirst()
    {
        // HU-62: una ventana propia (2030) para no ver los pedidos de las otras pruebas en la misma base.
        var from = new DateTime(2030, 3, 4, 18, 0, 0, DateTimeKind.Utc);
        var later = Order(status: OrderStatuses.Confirmed, deliveryAt: from.AddMinutes(50));
        var sooner = Order(status: OrderStatuses.Confirmed, deliveryAt: from.AddMinutes(10));
        var edge = Order(status: OrderStatuses.Confirmed, deliveryAt: from.AddHours(1));
        var past = Order(status: OrderStatuses.Confirmed, deliveryAt: from);
        var far = Order(status: OrderStatuses.Confirmed, deliveryAt: from.AddMinutes(61));
        var pending = Order(deliveryAt: from.AddMinutes(20));
        var cancelled = Order(status: OrderStatuses.Cancelled, deliveryAt: from.AddMinutes(20));
        foreach (var order in new[] { later, sooner, edge, past, far, pending, cancelled })
        {
            await Orders.TryInsertAsync(order, Ct);
        }

        var due = await Orders.FindDeliveriesBetweenAsync(from, from.AddHours(1), 10, Ct);

        Assert.Equal([sooner.Id, later.Id, edge.Id], due.Select(o => o.Id));
        Assert.Single(await Orders.FindDeliveriesBetweenAsync(from, from.AddHours(1), 1, Ct));
    }

    [Fact]
    public async Task MarkArrived_OncePerSide_OnlyAConfirmedOrder_OnlyItsParties_AndHandoverKeepsIt()
    {
        var buyer = AuthIds.New();
        var seller = AuthIds.New();
        var order = Order(buyerId: buyer, sellerId: seller, status: OrderStatuses.Confirmed);
        var pending = Order(buyerId: buyer, sellerId: seller);
        await Orders.TryInsertAsync(order, Ct);
        await Orders.TryInsertAsync(pending, Ct);

        Assert.Null(await Orders.MarkArrivedAsync(pending.Id, buyer, OrderRoles.Buyer, Now, Ct));
        Assert.Null(await Orders.MarkArrivedAsync(order.Id, seller, OrderRoles.Buyer, Now, Ct));
        Assert.Null(await Orders.MarkArrivedAsync("no-es-un-id", buyer, OrderRoles.Buyer, Now, Ct));
        var arrived = await Orders.MarkArrivedAsync(order.Id, buyer, OrderRoles.Buyer, Now, Ct);
        var twice = await Orders.MarkArrivedAsync(order.Id, buyer, OrderRoles.Buyer, Now.AddMinutes(5), Ct);
        var seller1 = await Orders.MarkArrivedAsync(order.Id, seller, OrderRoles.Seller, Now.AddMinutes(10), Ct);
        var delivered = await Orders.ConfirmHandoverAsync(order.Id, seller, OrderRoles.Seller, Now.AddMinutes(20), Ct);

        Assert.Equal(((DateTime?)Now, (DateTime?)null), (arrived!.Handover!.BuyerArrivedAt, arrived.Handover.SellerArrivedAt));
        Assert.Null(twice);
        Assert.Equal(((DateTime?)Now, (DateTime?)Now.AddMinutes(10)), (seller1!.Handover!.BuyerArrivedAt, seller1.Handover.SellerArrivedAt));
        Assert.Equal(((DateTime?)Now, (DateTime?)Now.AddMinutes(10)), (delivered!.Handover!.BuyerArrivedAt, delivered.Handover.SellerArrivedAt));
        Assert.Equal(Now.AddMinutes(20), delivered.Handover.SellerAt);
    }

    [Fact]
    public async Task CompletedByCommunity_OnlyCompleted_OfThatCampus_NewestFirst()
    {
        // HU-58: lo que de verdad se pago en el campus.
        var older = Order(status: OrderStatuses.Completed);
        var campus = older.CommunityId;
        var newer = Same(Order(status: OrderStatuses.Completed), campus);
        var open = Same(Order(status: OrderStatuses.Confirmed), campus);
        var cancelled = Same(Order(status: OrderStatuses.Cancelled), campus);
        var elsewhere = Order(status: OrderStatuses.Completed);
        foreach (var order in new[] { older, newer, open, cancelled, elsewhere })
        {
            await Orders.TryInsertAsync(order, Ct);
        }

        var completed = await Orders.FindCompletedByCommunityAsync(campus, 10, Ct);

        Assert.Equal([newer.Id, older.Id], completed.Select(o => o.Id));
        Assert.Single(await Orders.FindCompletedByCommunityAsync(campus, 1, Ct));
        Assert.Empty(await Orders.FindCompletedByCommunityAsync("no-es-un-id", 10, Ct));
    }

    // El mismo pedido en otro campus (Order() le da uno nuevo a cada uno).
    private static OrderDocument Same(OrderDocument o, string communityId) => new()
    {
        Id = o.Id,
        CommunityId = communityId,
        Number = o.Number,
        BuyerId = o.BuyerId,
        SellerId = o.SellerId,
        BuyerName = o.BuyerName,
        SellerName = o.SellerName,
        ConversationId = o.ConversationId,
        Item = o.Item,
        Delivery = o.Delivery,
        Payment = o.Payment,
        Status = o.Status,
        History = o.History,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
    };

    [Fact]
    public async Task TransfersToRemind_ArePendingAndReportedBeforeTheCut()
    {
        var old = Order(method: PaymentMethods.Transfer);
        var recent = Order(method: PaymentMethods.Transfer);
        var silent = Order(method: PaymentMethods.Transfer);
        var done = Order(method: PaymentMethods.Transfer);
        foreach (var order in new[] { old, recent, silent, done })
        {
            await Orders.TryInsertAsync(order, Ct);
        }

        await Orders.ReportTransferAsync(old.Id, old.BuyerId, "1111", Now.AddHours(-30), Ct);
        await Orders.ReportTransferAsync(recent.Id, recent.BuyerId, "2222", Now.AddHours(-2), Ct);
        await Orders.ReportTransferAsync(done.Id, done.BuyerId, "3333", Now.AddHours(-40), Ct);
        await Orders.ConfirmTransferAsync(done.Id, done.SellerId, Now.AddHours(-39), Ct);

        var due = await Orders.FindTransfersToRemindAsync(Now.AddHours(-24), 10, Ct);

        Assert.Contains(old.Id, due.Select(o => o.Id));
        Assert.DoesNotContain(recent.Id, due.Select(o => o.Id));
        Assert.DoesNotContain(silent.Id, due.Select(o => o.Id));
        Assert.DoesNotContain(done.Id, due.Select(o => o.Id));
    }

    [Fact]
    public async Task Usage_CountsTheOrdersCompletedInThePeriod_ItsPieces_AndTheConversationsWithAnOrder()
    {
        // HU-42 (operaciones, chat y vendedores).
        var product = AuthIds.New();
        var conversation = AuthIds.New();
        var done = Order(conversationId: conversation, productId: product, status: OrderStatuses.Completed);
        Assert.True(await Orders.TryInsertAsync(done, Ct));
        Assert.True(await Orders.TryInsertAsync(Order(status: OrderStatuses.Pending), Ct));

        var usage = await Orders.UsageAsync(done.CommunityId, Now.AddHours(-1), Now.AddHours(1), Ct);
        var withOrders = await Orders.CountConversationsWithOrdersAsync([conversation, AuthIds.New(), "no-es-un-id"], Ct);
        var sold = await Orders.CompletedForProductsAsync([product, AuthIds.New()], Ct);

        Assert.Equal(new OrderUsage(1, 1, 0), usage);
        Assert.Equal(new OrderUsage(0, 0, 0), await Orders.UsageAsync(done.CommunityId, Now.AddHours(1), Now.AddHours(2), Ct));
        Assert.Equal(1, withOrders);
        Assert.Equal((product, Now), Assert.Single(sold));
        Assert.Empty(await Orders.CompletedForProductsAsync([], Ct));
    }
}
