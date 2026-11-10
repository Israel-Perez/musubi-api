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
        string status = OrderStatuses.Pending) => new()
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
    }
}
