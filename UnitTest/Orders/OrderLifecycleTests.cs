using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared;
using UnitTest.Common;
using UnitTest.PurchaseRequests;

namespace UnitTest.Orders;

/// <summary>
/// HU-24: quien vende acepta un pedido pendiente con el pago elegido y aparta las piezas (RF-34); cualquiera lo
/// rechaza o cancela con un motivo de su lista y las piezas regresan (RN-20); se completa cuando los dos confirman la
/// entrega (RN-19) y la publicacion sin piezas pasa a vendida (RN-05); cada cambio avisa a la otra parte.
/// </summary>
public sealed class OrderLifecycleTests
{
    private const string Uttt = InMemoryCommunityRepository.UtttId;

    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryOrderRepository orders = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly RecordingNotificationCenter center = new();
    private readonly RecordingOutputCacheStore cache = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly ProductStock stock;
    private readonly OrderLifecycleService service;

    public OrderLifecycleTests()
    {
        stock = new ProductStock(products, cache, time);
        service = new OrderLifecycleService(
            orders,
            stock,
            new ProductDirectory(products),
            new BlockDirectory(blocks),
            new MemberDirectory(new InMemoryCommunityRepository(), users),
            center,
            time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync(string name, string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        Assert.True(await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = Uttt,
                Email = $"u.{id}@uttt.edu.mx",
                EmailNormalized = $"u.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = name,
                Status = status,
                EmailVerifiedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 10, 1, 8, 55, 0, DateTimeKind.Utc),
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private async Task<string> ProductAsync(string seller, int stock, string status = ProductStatuses.Active)
    {
        var id = AuthIds.New();
        await products.InsertAsync(
            new ProductDocument
            {
                Id = id,
                CommunityId = Uttt,
                SellerId = seller,
                Seller = new ProductSeller { Name = "Diego H." },
                Title = "Calculadora Casio fx-991ES Plus",
                CategorySlug = "calculadoras",
                PriceCents = 50_000,
                Condition = ProductConditions.Used,
                ConditionDetail = "likeNew",
                Stock = stock,
                Status = status,
                Photos = [$"media/products/{seller}/0123456789abcdef0123456789abcdef.webp"],
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false);
        return id;
    }

    private async Task<string> OrderAsync(string buyer, string seller, string? productId, int quantity = 1, bool paid = true)
    {
        var order = new OrderDocument
        {
            Id = AuthIds.New(),
            CommunityId = Uttt,
            Number = await orders.NextNumberAsync(Uttt, Ct).ConfigureAwait(false),
            BuyerId = buyer,
            SellerId = seller,
            BuyerName = "Ana Sofía Ramírez",
            SellerName = "Diego Hernández",
            ConversationId = AuthIds.New(),
            Item = new OrderItem
            {
                Source = productId is null ? OrderSources.Offer : OrderSources.Product,
                SourceId = productId ?? AuthIds.New(),
                ProductId = productId,
                Title = "Calculadora Casio fx-991ES Plus",
                PriceCents = 50_000,
                Quantity = quantity,
                TotalCents = 50_000L * quantity,
            },
            Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", At = Now.UtcDateTime.AddDays(1) },
            Payment = paid ? new OrderPayment { Method = PaymentMethods.Cash, ChosenAt = Now.UtcDateTime } : null,
            Status = OrderStatuses.Pending,
            History = [new OrderTransition { Status = OrderStatuses.Pending, At = Now.UtcDateTime }],
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        Assert.True(await orders.TryInsertAsync(order, Ct).ConfigureAwait(false));
        return order.Id;
    }

    // Diego vende la calculadora y Ana ya la pidio y eligio pagar en efectivo.
    private async Task<(string Ana, string Diego, string Product, string Order)> PaidOrderAsync(int stock = 3, int quantity = 1)
    {
        var ana = await MemberAsync("Ana Sofía Ramírez").ConfigureAwait(false);
        var diego = await MemberAsync("Diego Hernández").ConfigureAwait(false);
        var product = await ProductAsync(diego, stock).ConfigureAwait(false);
        return (ana, diego, product, await OrderAsync(ana, diego, product, quantity).ConfigureAwait(false));
    }

    private async Task<ProductDocument> ProductOfAsync(string id) => (await products.FindByIdAsync(id, Ct).ConfigureAwait(false))!;

    [Fact]
    public async Task Accept_BySeller_ConfirmsReservesThePieces_AndTellsTheBuyer()
    {
        var (ana, diego, product, order) = await PaidOrderAsync(stock: 3, quantity: 2);

        var result = await service.AcceptAsync(diego, order, Ct);

        Assert.Null(result.Error);
        Assert.Equal(("confirmed", "seller"), (result.Value!.Status, result.Value.Role));
        var listing = await ProductOfAsync(product);
        Assert.Equal((1, ProductStatuses.Active), (listing.Stock, listing.Status));
        var notice = Assert.Single(center.Sent);
        Assert.Equal((ana, NotificationTypes.OrderConfirmed, NotificationCategories.Orders, NotificationTargets.Order, order), (notice.UserId, notice.Type, notice.Category, notice.TargetType, notice.TargetId));
        Assert.Equal("Diego aceptó tu pedido #MS-1001. Se ven en Biblioteca.", notice.Body);
        Assert.Contains(CachingTag, cache.EvictedTags);
    }

    [Fact]
    public async Task Accept_TheLastPieces_LeavesTheListingSoldOut()
    {
        var (_, diego, product, order) = await PaidOrderAsync(stock: 2, quantity: 2);

        await service.AcceptAsync(diego, order, Ct);

        var listing = await ProductOfAsync(product);
        Assert.Equal((0, ProductStatuses.SoldOut), (listing.Stock, listing.Status));
    }

    [Fact]
    public async Task Accept_OnlyTheSeller_AndOnlyAParty()
    {
        var (ana, _, product, order) = await PaidOrderAsync();
        var stranger = await MemberAsync("Luis Pérez");

        Assert.Equal("orders.only_seller", (await service.AcceptAsync(ana, order, Ct)).Error!.Code);
        Assert.Equal("orders.not_found", (await service.AcceptAsync(stranger, order, Ct)).Error!.Code);
        Assert.Equal("orders.not_found", (await service.AcceptAsync(ana, AuthIds.New(), Ct)).Error!.Code);
        Assert.Equal(3, (await ProductOfAsync(product)).Stock);
    }

    [Fact]
    public async Task Accept_WaitsForThePayment()
    {
        var ana = await MemberAsync("Ana Sofía Ramírez");
        var diego = await MemberAsync("Diego Hernández");
        var product = await ProductAsync(diego, 1);
        var order = await OrderAsync(ana, diego, product, paid: false);

        var result = await service.AcceptAsync(diego, order, Ct);

        Assert.Equal("orders.payment_missing", result.Error!.Code);
        Assert.Equal(1, (await ProductOfAsync(product)).Stock);
    }

    [Fact]
    public async Task Accept_WithoutEnoughPieces_OrWithAPausedListing_IsRejected()
    {
        var (_, diego, product, order) = await PaidOrderAsync(stock: 1, quantity: 2);
        var paused = await ProductAsync(diego, 3, ProductStatuses.Hidden);
        var ana = await MemberAsync("Ana Sofía Ramírez");
        var other = await OrderAsync(ana, diego, paused);

        Assert.Equal("orders.not_enough_stock", (await service.AcceptAsync(diego, order, Ct)).Error!.Code);
        Assert.Equal("orders.product_unavailable", (await service.AcceptAsync(diego, other, Ct)).Error!.Code);
        Assert.Equal(1, (await ProductOfAsync(product)).Stock);
        Assert.Empty(center.Sent);
    }

    [Fact]
    public async Task Accept_Twice_IsNotPending_AndDoesNotReserveAgain()
    {
        var (_, diego, product, order) = await PaidOrderAsync(stock: 3);

        await service.AcceptAsync(diego, order, Ct);
        var again = await service.AcceptAsync(diego, order, Ct);

        Assert.Equal("orders.not_pending", again.Error!.Code);
        Assert.Equal(2, (await ProductOfAsync(product)).Stock);
    }

    [Fact]
    public async Task Accept_WithABlock_IsBlocked()
    {
        var (ana, diego, product, order) = await PaidOrderAsync();
        await blocks.InsertIfAbsentAsync(
            new BlockDocument { Id = AuthIds.New(), CommunityId = Uttt, BlockerId = ana, BlockedId = diego, BlockedName = "Diego H.", CreatedAt = Now.UtcDateTime },
            Ct);

        Assert.Equal("orders.blocked", (await service.AcceptAsync(diego, order, Ct)).Error!.Code);
        Assert.Equal(3, (await ProductOfAsync(product)).Stock);
    }

    [Fact]
    public async Task Accept_ADirectOffer_DoesNotTouchAnyListing()
    {
        var ana = await MemberAsync("Ana Sofía Ramírez");
        var diego = await MemberAsync("Diego Hernández");
        var order = await OrderAsync(ana, diego, productId: null);

        var result = await service.AcceptAsync(diego, order, Ct);

        Assert.Equal("confirmed", result.Value!.Status);
        Assert.Empty(cache.EvictedTags);
    }

    [Fact]
    public async Task Accept_ByASuspendedAccount_IsForbidden()
    {
        var ana = await MemberAsync("Ana Sofía Ramírez");
        var diego = await MemberAsync("Diego Hernández", UserStatuses.Suspended);
        var product = await ProductAsync(diego, 1);
        var order = await OrderAsync(ana, diego, product);

        Assert.Equal("auth.forbidden", (await service.AcceptAsync(diego, order, Ct)).Error!.Code);
    }

    [Fact]
    public async Task Cancel_AConfirmedOrder_ReleasesThePieces_AndTellsTheOtherParty()
    {
        var (ana, diego, product, order) = await PaidOrderAsync(stock: 1);
        await service.AcceptAsync(diego, order, Ct);
        center.Sent.Clear();

        var result = await service.CancelAsync(ana, order, new CancelOrderRequest(CancelReasons.ChangedMind), Ct);

        var cancelled = result.Value!;
        Assert.Equal(("cancelled", "buyer", "changed_mind"), (cancelled.Status, cancelled.Cancellation!.By, cancelled.Cancellation.Reason));
        var listing = await ProductOfAsync(product);
        Assert.Equal((1, ProductStatuses.Active), (listing.Stock, listing.Status));
        var notice = Assert.Single(center.Sent);
        Assert.Equal((diego, NotificationTypes.OrderCancelled, "Pedido cancelado"), (notice.UserId, notice.Type, notice.Title));
        Assert.Equal("Ana canceló el pedido #MS-1001: Ya no lo necesito.", notice.Body);
    }

    [Fact]
    public async Task Reject_APendingOrder_BySeller_KeepsThePieces()
    {
        var (ana, diego, product, order) = await PaidOrderAsync(stock: 2);

        var result = await service.CancelAsync(diego, order, new CancelOrderRequest(CancelReasons.Unavailable), Ct);

        Assert.Equal(("cancelled", "seller"), (result.Value!.Status, result.Value.Cancellation!.By));
        Assert.Equal(2, (await ProductOfAsync(product)).Stock);
        var notice = Assert.Single(center.Sent);
        Assert.Equal((ana, "Tu pedido fue rechazado", "Diego rechazó el pedido #MS-1001: Ya no tengo el producto."), (notice.UserId, notice.Title, notice.Body));
    }

    [Theory]
    [InlineData("buyer", "unavailable")]
    [InlineData("seller", "changed_mind")]
    [InlineData("buyer", "account_deleted")]
    [InlineData("seller", "lo-que-sea")]
    [InlineData("buyer", null)]
    public async Task Cancel_WithAReasonThatIsNotOfItsList_IsInvalid(string role, string? reason)
    {
        var (ana, diego, _, order) = await PaidOrderAsync();

        var result = await service.CancelAsync(role == "buyer" ? ana : diego, order, new CancelOrderRequest(reason), Ct);

        Assert.Equal("orders.invalid_reason", result.Error!.Code);
    }

    [Fact]
    public async Task Cancel_AClosedOrder_IsClosed_AndAStrangerSeesNothing()
    {
        var (ana, _, _, order) = await PaidOrderAsync();
        var stranger = await MemberAsync("Luis Pérez");
        await service.CancelAsync(ana, order, new CancelOrderRequest(CancelReasons.Other), Ct);

        Assert.Equal("orders.closed", (await service.CancelAsync(ana, order, new CancelOrderRequest(CancelReasons.Other), Ct)).Error!.Code);
        Assert.Equal("orders.not_found", (await service.CancelAsync(stranger, order, new CancelOrderRequest(CancelReasons.Other), Ct)).Error!.Code);
    }

    [Fact]
    public async Task Handover_FromBothParties_CompletesTheOrder_AndTheEmptyListingIsSold()
    {
        var (ana, diego, product, order) = await PaidOrderAsync(stock: 1);
        await service.AcceptAsync(diego, order, Ct);
        center.Sent.Clear();

        var delivered = await service.ConfirmHandoverAsync(diego, order, Ct);
        var repeated = await service.ConfirmHandoverAsync(diego, order, Ct);
        time.Advance(TimeSpan.FromMinutes(5));
        var received = await service.ConfirmHandoverAsync(ana, order, Ct);

        Assert.Equal(("confirmed", Now.UtcDateTime), (delivered.Value!.Status, delivered.Value.Handover.SellerConfirmedAt));
        Assert.Null(delivered.Value.Handover.BuyerConfirmedAt);
        Assert.Equal(Now.UtcDateTime, repeated.Value!.Handover.SellerConfirmedAt);
        Assert.Equal("completed", received.Value!.Status);
        var listing = await ProductOfAsync(product);
        Assert.Equal((ProductStatuses.Sold, Now.UtcDateTime.AddMinutes(5)), (listing.Status, listing.SoldAt));
        Assert.Equal(
            [(ana, NotificationTypes.OrderHandover), (diego, NotificationTypes.OrderCompleted)],
            center.Sent.Select(n => (n.UserId, n.Type)));
        Assert.Equal("Diego marcó el pedido #MS-1001 como entregado. Confírmalo cuando lo tengas.", center.Sent[0].Body);
    }

    [Fact]
    public async Task Handover_FirstByTheBuyer_AsksTheSeller()
    {
        var (ana, diego, _, order) = await PaidOrderAsync();
        await service.AcceptAsync(diego, order, Ct);
        center.Sent.Clear();

        await service.ConfirmHandoverAsync(ana, order, Ct);

        var notice = Assert.Single(center.Sent);
        Assert.Equal((diego, "Confirma la entrega"), (notice.UserId, notice.Title));
    }

    [Fact]
    public async Task Handover_OnlyOnAConfirmedOrder()
    {
        var (ana, diego, _, order) = await PaidOrderAsync();
        var (_, _, _, cancelled) = await PaidOrderAsync();

        Assert.Equal("orders.not_confirmed", (await service.ConfirmHandoverAsync(ana, order, Ct)).Error!.Code);
        var cancelledSeller = (await orders.FindByIdAsync(cancelled, Ct))!.SellerId;
        await service.CancelAsync(cancelledSeller, cancelled, new CancelOrderRequest(CancelReasons.Other), Ct);
        Assert.Equal("orders.closed", (await service.ConfirmHandoverAsync(cancelledSeller, cancelled, Ct)).Error!.Code);

        await service.AcceptAsync(diego, order, Ct);
        await service.ConfirmHandoverAsync(diego, order, Ct);
        await service.ConfirmHandoverAsync(ana, order, Ct);
        var late = await service.ConfirmHandoverAsync(diego, order, Ct);
        Assert.Equal("completed", late.Value!.Status);
    }

    [Fact]
    public async Task Complete_WithAnotherOpenOrder_KeepsTheListingSoldOut_UntilTheLastOne()
    {
        var (ana, diego, product, first) = await PaidOrderAsync(stock: 2);
        var luis = await MemberAsync("Luis Pérez");
        var second = await OrderAsync(luis, diego, product);
        await service.AcceptAsync(diego, first, Ct);
        await service.AcceptAsync(diego, second, Ct);

        await service.ConfirmHandoverAsync(diego, first, Ct);
        await service.ConfirmHandoverAsync(ana, first, Ct);
        Assert.Equal(ProductStatuses.SoldOut, (await ProductOfAsync(product)).Status);

        await service.ConfirmHandoverAsync(diego, second, Ct);
        await service.ConfirmHandoverAsync(luis, second, Ct);
        Assert.Equal(ProductStatuses.Sold, (await ProductOfAsync(product)).Status);
    }

    [Fact]
    public async Task EraseAccount_ReleasesThePiecesOfItsConfirmedOrders()
    {
        var (ana, diego, product, order) = await PaidOrderAsync(stock: 1);
        await service.AcceptAsync(diego, order, Ct);

        await new OrderAccountEraser(orders, stock, time).EraseAsync(ana, Ct);

        var listing = await ProductOfAsync(product);
        Assert.Equal((1, ProductStatuses.Active), (listing.Stock, listing.Status));
        var erased = (await orders.FindByIdAsync(order, Ct))!;
        Assert.Equal(("cancelled", OrderRoles.System, CancelReasons.AccountDeleted), (erased.Status, erased.Cancellation!.By, erased.Cancellation.Reason));
    }

    private static string CachingTag => Musubi.Api.Platform.Caching.CachingServiceCollectionExtensions.ShowcaseTag;
}
