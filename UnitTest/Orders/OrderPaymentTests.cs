using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared;
using UnitTest.Common;
using UnitTest.PurchaseRequests;

namespace UnitTest.Orders;

/// <summary>
/// HU-55 (D-01; maquetas <c>Pago-transferencia</c>, <c>Pago-validando</c>, <c>Pago-vendedor</c> y
/// <c>Pedido-efectivo</c>): por transferencia, quien compra ve la cuenta y captura la referencia y quien vende confirma
/// que le llego (el pedido pasa a Confirmado) o dice que todavia no; en efectivo, al aceptar nace un codigo de 4 digitos
/// que solo ve quien compra y que, escrito por quien vende, completa el pedido (hasta 5 intentos).
/// </summary>
public sealed class OrderPaymentTests
{
    private const string Uttt = InMemoryCommunityRepository.UtttId;

    private static readonly DateTimeOffset Now = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryOrderRepository orders = new();
    private readonly InMemoryProfileRepository profiles = new();
    private readonly RecordingNotificationCenter center = new();
    private readonly RecordingInteractionRecorder recorder = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly FakePayoutDirectory payouts = new();
    private readonly OrderLifecycleService lifecycle;
    private readonly OrderPaymentService payments;
    private readonly TransferReminderJob reminders;

    public OrderPaymentTests()
    {
        var members = new MemberDirectory(new InMemoryCommunityRepository(), users);
        lifecycle = new OrderLifecycleService(
            orders,
            new ProductStock(products, new RecordingOutputCacheStore(), new RecordingRestocks(), time),
            new ProductDirectory(products),
            new BlockDirectory(new InMemoryBlockRepository()),
            members,
            center,
            new ProfileTrades(profiles),
            recorder,
            time);
        payments = new OrderPaymentService(orders, payouts, members, center, time);
        reminders = new TransferReminderJob(orders, payments);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync(string name)
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
                Status = UserStatuses.Active,
                EmailVerifiedAt = Now.UtcDateTime.AddDays(-30),
                CreatedAt = Now.UtcDateTime.AddDays(-30),
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    // Ana le compra a Diego la calculadora de $500 (pedido #MS-1001) y eligio como pagar.
    private async Task<(string Ana, string Diego, string Product, string Order)> OrderAsync(string method)
    {
        var ana = await MemberAsync("Ana Sofía Ramírez").ConfigureAwait(false);
        var diego = await MemberAsync("Diego Hernández").ConfigureAwait(false);
        var product = AuthIds.New();
        await products.InsertAsync(
            new ProductDocument
            {
                Id = product,
                CommunityId = Uttt,
                SellerId = diego,
                Seller = new ProductSeller { Name = "Diego H." },
                Title = "Calculadora Casio fx-991ES Plus",
                CategorySlug = "calculadoras",
                PriceCents = 50_000,
                Condition = ProductConditions.Used,
                ConditionDetail = "likeNew",
                Stock = 1,
                Status = ProductStatuses.Active,
                Photos = [$"media/products/{diego}/0123456789abcdef0123456789abcdef.webp"],
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false);
        var order = new OrderDocument
        {
            Id = AuthIds.New(),
            CommunityId = Uttt,
            Number = await orders.NextNumberAsync(Uttt, Ct).ConfigureAwait(false),
            BuyerId = ana,
            SellerId = diego,
            BuyerName = "Ana Sofía Ramírez",
            SellerName = "Diego Hernández",
            ConversationId = AuthIds.New(),
            Item = new OrderItem
            {
                Source = OrderSources.Product,
                SourceId = product,
                ProductId = product,
                Title = "Calculadora Casio fx-991ES Plus",
                PriceCents = 50_000,
                Quantity = 1,
                TotalCents = 50_000,
            },
            Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", At = Now.UtcDateTime.AddDays(1) },
            Payment = new OrderPayment { Method = method, ChosenAt = Now.UtcDateTime },
            Status = OrderStatuses.Pending,
            History = [new OrderTransition { Status = OrderStatuses.Pending, At = Now.UtcDateTime }],
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        Assert.True(await orders.TryInsertAsync(order, Ct).ConfigureAwait(false));
        payouts.Accounts[diego] = new TransferAccount("Diego Hernández López", "BBVA México", PayoutTestKit.BbvaClabe, PayoutTestKit.BbvaClabe[^4..]);
        return (ana, diego, product, order.Id);
    }

    [Fact]
    public async Task TheBuyerSeesTheWholeClabe_TheSellerOnlyItsLastDigits()
    {
        var (ana, diego, _, order) = await OrderAsync(PaymentMethods.Transfer);

        var forAna = (await payments.TransferAsync(ana, order, Ct)).Value!;
        var forDiego = (await payments.TransferAsync(diego, order, Ct)).Value!;

        Assert.Equal(
            new TransferDetailsResponse(50_000, "BBVA México", "Diego Hernández López", Clabe.Format(PayoutTestKit.BbvaClabe), "4829", "MS-1001", null, null, null, null),
            forAna);
        Assert.Equal("•••• 4829", forDiego.Clabe);
        Assert.Equal(OrdersErrors.NotFound, (await payments.TransferAsync(AuthIds.New(), order, Ct)).Error);
    }

    [Fact]
    public async Task WithoutTheSellersAccount_OrPayingCash_ThereIsNoTransfer()
    {
        var (ana, diego, _, order) = await OrderAsync(PaymentMethods.Transfer);
        var (cashBuyer, _, _, cash) = await OrderAsync(PaymentMethods.Cash);
        payouts.Accounts.Remove(diego);

        Assert.Equal(OrdersErrors.TransferUnavailable, (await payments.TransferAsync(ana, order, Ct)).Error);
        Assert.Equal(OrdersErrors.TransferUnavailable, (await payments.ReportTransferAsync(ana, order, new ReportTransferRequest("4829173"), Ct)).Error);
        Assert.Equal(OrdersErrors.NotTransfer, (await payments.TransferAsync(cashBuyer, cash, Ct)).Error);
    }

    [Fact]
    public async Task ReportingTheTransfer_TellsTheSeller_AndOnlyThenCanItBeConfirmed()
    {
        var (ana, diego, product, order) = await OrderAsync(PaymentMethods.Transfer);

        Assert.Equal(OrdersErrors.TransferNotReported, (await lifecycle.AcceptAsync(diego, order, Ct)).Error);
        Assert.Equal(OrdersErrors.OnlyBuyer, (await payments.ReportTransferAsync(diego, order, new ReportTransferRequest("4829173"), Ct)).Error);
        var reported = await payments.ReportTransferAsync(ana, order, new ReportTransferRequest(" 4829 173 "), Ct);

        Assert.Equal(("4829173", Now.UtcDateTime), (reported.Value!.Payment!.Reference, reported.Value.Payment.ReportedAt));
        var notice = Assert.Single(center.Sent);
        Assert.Equal(
            (diego, NotificationTypes.TransferReported, "Ana dice que ya te transfirió", "Pedido #MS-1001 · $500. Búscalo en tu banco y confirma que te llegó."),
            (notice.UserId, notice.Type, notice.Title, notice.Body));

        var accepted = await lifecycle.AcceptAsync(diego, order, Ct);

        Assert.Equal((OrderStatuses.Confirmed, Now.UtcDateTime), (accepted.Value!.Status, accepted.Value.Payment!.ValidatedAt));
        Assert.Equal(0, (await products.FindByIdAsync(product, Ct))!.Stock); // RF-34: la pieza queda apartada
        Assert.Equal(("¡Diego confirmó tu pago!", ana), (center.Sent[^1].Title, center.Sent[^1].UserId));
        Assert.Null(accepted.Value.Payment.DeliveryCode); // por transferencia no hay codigo
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("4829173-ABC-4829173-ABC-4829173")]
    [InlineData("48#29")]
    public async Task AReferenceOutsideTheRules_IsRefused(string reference)
    {
        var (ana, _, _, order) = await OrderAsync(PaymentMethods.Transfer);

        Assert.Equal(OrdersErrors.InvalidReference, (await payments.ReportTransferAsync(ana, order, new ReportTransferRequest(reference), Ct)).Error);
        Assert.Empty(center.Sent);
    }

    [Fact]
    public async Task NotReceivedYet_TellsTheBuyer_WhoCanSendTheReferenceAgain()
    {
        var (ana, diego, _, order) = await OrderAsync(PaymentMethods.Transfer);
        Assert.Equal(OrdersErrors.TransferNotReported, (await payments.NotReceivedAsync(diego, order, Ct)).Error);
        await payments.ReportTransferAsync(ana, order, new ReportTransferRequest("4829173"), Ct);

        Assert.Equal(OrdersErrors.OnlySeller, (await payments.NotReceivedAsync(ana, order, Ct)).Error);
        var notYet = await payments.NotReceivedAsync(diego, order, Ct);

        Assert.Equal(Now.UtcDateTime, notYet.Value!.Payment!.NotReceivedAt);
        Assert.Equal(
            (ana, NotificationTypes.TransferNotReceived, "Diego todavía no ve tu transferencia"),
            (center.Sent[^1].UserId, center.Sent[^1].Type, center.Sent[^1].Title));
        var again = await payments.ReportTransferAsync(ana, order, new ReportTransferRequest("4829174"), Ct);
        Assert.Equal(("4829174", null), (again.Value!.Payment!.Reference, again.Value.Payment.NotReceivedAt));
    }

    [Fact]
    public async Task After24Hours_TheSellerIsRemindedOnce_AndTheOrderStaysPending()
    {
        var (ana, diego, _, order) = await OrderAsync(PaymentMethods.Transfer);
        await payments.ReportTransferAsync(ana, order, new ReportTransferRequest("4829173"), Ct);

        Assert.Equal(0, await reminders.RunAsync(Now.UtcDateTime.AddHours(23), Ct));
        Assert.Equal(1, await reminders.RunAsync(Now.UtcDateTime.AddHours(25), Ct));

        var reminder = center.Sent[^1];
        Assert.Equal(
            (diego, NotificationTypes.TransferReminder, "Confirma la transferencia de Ana", $"transfer_reminder:{order}"),
            (reminder.UserId, reminder.Type, reminder.Title, reminder.DedupeKey));
        // Decision de la usuaria (15-Nov-2026): no se cancela solo.
        Assert.Equal(OrderStatuses.Pending, (await orders.FindByIdAsync(order, Ct))!.Status);
    }

    [Fact]
    public async Task Cash_TheCodeIsBornWhenAccepted_AndOnlyTheBuyerSeesIt()
    {
        var (_, diego, _, order) = await OrderAsync(PaymentMethods.Cash);

        var accepted = (await lifecycle.AcceptAsync(diego, order, Ct)).Value!;
        var stored = (await orders.FindByIdAsync(order, Ct))!;

        Assert.Matches("^[0-9]{4}$", stored.Payment!.DeliveryCode!);
        Assert.Null(accepted.Payment!.DeliveryCode); // la respuesta es para Diego
        Assert.Equal(5, accepted.Payment.CodeAttemptsLeft);
        var forAna = OrderService.PaymentFor(stored, OrderRoles.Buyer)!;
        Assert.Equal((stored.Payment.DeliveryCode, (int?)null), (forAna.DeliveryCode, forAna.CodeAttemptsLeft));
    }

    [Fact]
    public async Task Cash_TheRightCodeCompletesTheOrder_AndTellsTheBuyer()
    {
        var (ana, diego, product, order) = await OrderAsync(PaymentMethods.Cash);
        await lifecycle.AcceptAsync(diego, order, Ct);
        var code = (await orders.FindByIdAsync(order, Ct))!.Payment!.DeliveryCode!;

        Assert.Equal(OrdersErrors.OnlySeller, (await lifecycle.CompleteWithCodeAsync(ana, order, new DeliveryCodeRequest(code), Ct)).Error);
        Assert.Equal(OrdersErrors.InvalidCodeFormat, (await lifecycle.CompleteWithCodeAsync(diego, order, new DeliveryCodeRequest("12a"), Ct)).Error);
        var completed = await lifecycle.CompleteWithCodeAsync(diego, order, new DeliveryCodeRequest(code), Ct);

        Assert.Equal(OrderStatuses.Completed, completed.Value!.Status);
        Assert.Equal((Now.UtcDateTime, Now.UtcDateTime), (completed.Value.Handover.BuyerConfirmedAt, completed.Value.Handover.SellerConfirmedAt));
        Assert.Equal(ProductStatuses.Sold, (await products.FindByIdAsync(product, Ct))!.Status); // RN-05
        var notice = center.Sent[^1];
        Assert.Equal(
            (ana, NotificationTypes.OrderCompleted, "¡Pedido completado!", "Diego ingresó tu código y confirmó el pago. Toca para calificar."),
            (notice.UserId, notice.Type, notice.Title, notice.Body));
    }

    [Fact]
    public async Task Cash_AfterFiveWrongCodes_ItLocks_AndTheyConfirmTheHandoverAsBefore()
    {
        var (ana, diego, _, order) = await OrderAsync(PaymentMethods.Cash);
        await lifecycle.AcceptAsync(diego, order, Ct);
        var code = (await orders.FindByIdAsync(order, Ct))!.Payment!.DeliveryCode!;
        var wrong = code == "0000" ? "1111" : "0000";

        var errors = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            errors.Add((await lifecycle.CompleteWithCodeAsync(diego, order, new DeliveryCodeRequest(wrong), Ct)).Error!.Code);
        }

        Assert.All(errors, e => Assert.Equal("orders.invalid_code", e));
        // Ni el correcto entra ya (fuerza bruta: 5 de 10 000).
        Assert.Equal(OrdersErrors.CodeLocked, (await lifecycle.CompleteWithCodeAsync(diego, order, new DeliveryCodeRequest(code), Ct)).Error);
        Assert.Equal(0, OrderService.PaymentFor((await orders.FindByIdAsync(order, Ct))!, OrderRoles.Seller)!.CodeAttemptsLeft);
        await lifecycle.ConfirmHandoverAsync(ana, order, Ct);
        Assert.Equal(OrderStatuses.Completed, (await lifecycle.ConfirmHandoverAsync(diego, order, Ct)).Value!.Status);
    }

    [Fact]
    public async Task TheCode_IsOnlyForCashOrders_AlreadyAccepted()
    {
        var (ana, diego, _, order) = await OrderAsync(PaymentMethods.Transfer);
        var (_, cashSeller, _, cash) = await OrderAsync(PaymentMethods.Cash);
        await payments.ReportTransferAsync(ana, order, new ReportTransferRequest("4829173"), Ct);
        await lifecycle.AcceptAsync(diego, order, Ct);

        Assert.Equal(OrdersErrors.NotCash, (await lifecycle.CompleteWithCodeAsync(diego, order, new DeliveryCodeRequest("1234"), Ct)).Error);
        Assert.Equal(OrdersErrors.NotConfirmed, (await lifecycle.CompleteWithCodeAsync(cashSeller, cash, new DeliveryCodeRequest("1234"), Ct)).Error);
    }
}
