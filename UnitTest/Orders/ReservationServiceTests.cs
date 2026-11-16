using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Chat.Data;
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
/// HU-52 (decisiones de la usuaria del 15-Nov-2026): apartar guarda una pieza 24 h (uno activo a la vez), abre la
/// conversacion y avisa a quien vende; solo quien aparta ve el QR y el codigo; quien vende lo verifica (5 intentos) y
/// "Confirmar entrega" crea un pedido completado; si nadie lo recoge, a las 24 h vence y la pieza regresa.
/// </summary>
public sealed class ReservationServiceTests
{
    private const string Uttt = InMemoryCommunityRepository.UtttId;

    // Domingo 15 de noviembre de 2026, 12:00 del campus (18:00 UTC).
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryOrderRepository orders = new();
    private readonly InMemoryReservationRepository reservations = new();
    private readonly InMemoryConversationRepository conversations = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly InMemoryProfileRepository profiles = new();
    private readonly RecordingNotificationCenter center = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly RecordingRestocks restocks = new();
    private readonly ProductStock stock;
    private readonly ReservationService service;
    private readonly ReservationExpiryJob expiry;

    public ReservationServiceTests()
    {
        var members = new MemberDirectory(new InMemoryCommunityRepository(), users);
        stock = new ProductStock(products, new RecordingOutputCacheStore(), restocks, time);
        var lifecycle = new OrderLifecycleService(
            orders,
            stock,
            new ProductDirectory(products),
            new BlockDirectory(blocks),
            members,
            center,
            new ProfileTrades(profiles),
            new RecordingInteractionRecorder(),
            time);
        service = new ReservationService(
            reservations,
            orders,
            new InMemoryMeetingPointRepository(),
            new ProductDirectory(products),
            stock,
            new BlockDirectory(blocks),
            members,
            new ConversationOpener(conversations, members, time),
            center,
            lifecycle,
            time);
        expiry = new ReservationExpiryJob(reservations, stock, service);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 3:00 p. m. del campus de hoy (21:00 UTC).
    private static DateTime Afternoon => Now.UtcDateTime.AddHours(3);

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

    private async Task<string> ProductAsync(string seller, int stock = 1, string title = "Juego de escuadras y compás")
    {
        var id = AuthIds.New();
        await products.InsertAsync(
            new ProductDocument
            {
                Id = id,
                CommunityId = Uttt,
                SellerId = seller,
                Seller = new ProductSeller { Name = "Valeria C." },
                Title = title,
                CategorySlug = "papeleria",
                PriceCents = 18_000,
                Condition = ProductConditions.Used,
                ConditionDetail = "good",
                Stock = stock,
                Status = ProductStatuses.Active,
                Photos = [$"media/products/{seller}/0123456789abcdef0123456789abcdef.webp"],
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false);
        return id;
    }

    // Luis le aparta a Valeria su juego de escuadras para hoy a las 3:00 p. m. en la Biblioteca.
    private async Task<(string Luis, string Valeria, string Product, ReservationResponse Reservation)> ReservedAsync()
    {
        var luis = await MemberAsync("Luis Martínez").ConfigureAwait(false);
        var valeria = await MemberAsync("Valeria Cruz").ConfigureAwait(false);
        var product = await ProductAsync(valeria).ConfigureAwait(false);
        var reserved = await service.CreateAsync(luis, new CreateReservationRequest(product, "biblioteca", Afternoon), Ct).ConfigureAwait(false);
        return (luis, valeria, product, reserved.Value!);
    }

    [Fact]
    public async Task Reserving_KeepsThePiece_OpensTheChat_AndTellsTheSeller()
    {
        var (luis, valeria, product, reserved) = await ReservedAsync();

        Assert.Equal(("AP-1001", ReservationStatuses.Active, "Biblioteca"), (reserved.Number, reserved.Status, reserved.PointName));
        Assert.Matches("^[0-9]{6}$", reserved.Code!);
        Assert.Equal($"musubi:apartado:{reserved.Id}:{reserved.Code}", reserved.QrPayload);
        Assert.Equal(Now.UtcDateTime.AddHours(24), reserved.ExpiresAt);
        Assert.Equal(0, (await products.FindByIdAsync(product, Ct))!.Stock);
        Assert.NotNull(await new ConversationDirectory(conversations).FindAsync(reserved.ConversationId, Ct));
        var notice = Assert.Single(center.Sent);
        Assert.Equal(
            (valeria, NotificationTypes.ReservationCreated, "Luis apartó tu Juego de escuadras y compás", NotificationTargets.Reservation),
            (notice.UserId, notice.Type, notice.Title, notice.TargetType));
        _ = luis;
    }

    [Fact]
    public async Task OnlyTheBuyerSeesTheCode_TheSellerSeesTheAttempts()
    {
        var (_, valeria, _, reserved) = await ReservedAsync();

        var forValeria = (await service.GetAsync(valeria, reserved.Id, Ct)).Value!;

        Assert.Equal((null, null, 5), (forValeria.Code, forValeria.QrPayload, forValeria.CodeAttemptsLeft));
        Assert.Equal(ReservationsErrors.NotFound, (await service.GetAsync(AuthIds.New(), reserved.Id, Ct)).Error);
    }

    [Fact]
    public async Task OneActiveAtATime_AndTheSecondPieceIsNotKept()
    {
        var (luis, valeria, _, _) = await ReservedAsync();
        var another = await ProductAsync(valeria, stock: 2, title: "Calculadora Casio");

        var second = await service.CreateAsync(luis, new CreateReservationRequest(another, "biblioteca", Afternoon), Ct);

        Assert.Equal(ReservationsErrors.LimitReached, second.Error);
        Assert.Equal(2, (await products.FindByIdAsync(another, Ct))!.Stock);
    }

    [Theory]
    [InlineData("auditorio", 3, "reservations.invalid_meeting_point")]
    [InlineData("biblioteca", 30, "reservations.invalid_pickup_time")] // pasado el dia del apartado
    [InlineData("biblioteca", 10, "reservations.invalid_pickup_time")] // 4:00 a. m. del campus
    public async Task ThePlaceAndTheTime_AreChecked(string point, int hours, string code)
    {
        var luis = await MemberAsync("Luis Martínez");
        var product = await ProductAsync(await MemberAsync("Valeria Cruz"));

        var result = await service.CreateAsync(luis, new CreateReservationRequest(product, point, Now.UtcDateTime.AddHours(hours)), Ct);

        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(1, (await products.FindByIdAsync(product, Ct))!.Stock);
    }

    [Fact]
    public async Task MyOwn_SoldOut_OrBlocked_CannotBeReserved()
    {
        var luis = await MemberAsync("Luis Martínez");
        var valeria = await MemberAsync("Valeria Cruz");
        var mine = await ProductAsync(luis);
        var empty = await ProductAsync(valeria, stock: 0);
        var blocked = await ProductAsync(valeria);

        Assert.Equal(ReservationsErrors.OwnProduct, (await service.CreateAsync(luis, new CreateReservationRequest(mine, "biblioteca", Afternoon), Ct)).Error);
        Assert.Equal(ReservationsErrors.ProductUnavailable, (await service.CreateAsync(luis, new CreateReservationRequest(empty, "biblioteca", Afternoon), Ct)).Error);
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = valeria, BlockedId = luis, CreatedAt = Now.UtcDateTime }, Ct);
        Assert.Equal(ReservationsErrors.Blocked, (await service.CreateAsync(luis, new CreateReservationRequest(blocked, "biblioteca", Afternoon), Ct)).Error);
    }

    [Fact]
    public async Task VerifyingAndDelivering_CreatesACompletedOrder_AndTheProductIsSold()
    {
        var (luis, valeria, product, reserved) = await ReservedAsync();

        Assert.Equal(ReservationsErrors.NotVerified, (await service.CompleteAsync(valeria, reserved.Id, Ct)).Error);
        Assert.Equal(ReservationsErrors.OnlySeller, (await service.VerifyAsync(luis, reserved.Id, new VerifyReservationRequest(reserved.Code), Ct)).Error);
        var verified = await service.VerifyAsync(valeria, reserved.Id, new VerifyReservationRequest(reserved.Code![..3] + " " + reserved.Code[3..]), Ct);
        Assert.Equal(Now.UtcDateTime, verified.Value!.VerifiedAt);

        var collected = (await service.CompleteAsync(valeria, reserved.Id, Ct)).Value!;

        Assert.Equal(ReservationStatuses.Collected, collected.Status);
        var order = (await orders.FindByIdAsync(collected.OrderId!, Ct))!;
        Assert.Equal((OrderStatuses.Completed, luis, valeria, 18_000L), (order.Status, order.BuyerId, order.SellerId, order.Item.TotalCents));
        Assert.Equal((Now.UtcDateTime, Now.UtcDateTime), (order.Handover!.BuyerAt, order.Handover.SellerAt));
        Assert.Equal(reserved.ConversationId, order.ConversationId);
        Assert.Equal(ProductStatuses.Sold, (await products.FindByIdAsync(product, Ct))!.Status);
        Assert.Equal((luis, NotificationTypes.OrderCompleted), (center.Sent[^1].UserId, center.Sent[^1].Type));
        Assert.Equal(ReservationsErrors.NotActive, (await service.CompleteAsync(valeria, reserved.Id, Ct)).Error);
    }

    [Fact]
    public async Task AWrongCode_SpendsAnAttempt_AndAfterFiveItLocks()
    {
        var (_, valeria, _, reserved) = await ReservedAsync();
        var wrong = reserved.Code == "000000" ? "111111" : "000000";

        Assert.Equal(ReservationsErrors.InvalidCodeFormat, (await service.VerifyAsync(valeria, reserved.Id, new VerifyReservationRequest("12345"), Ct)).Error);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(ReservationsErrors.InvalidCode, (await service.VerifyAsync(valeria, reserved.Id, new VerifyReservationRequest(wrong), Ct)).Error);
        }

        Assert.Equal(ReservationsErrors.CodeLocked, (await service.VerifyAsync(valeria, reserved.Id, new VerifyReservationRequest(reserved.Code), Ct)).Error);
        Assert.Equal(0, (await service.GetAsync(valeria, reserved.Id, Ct)).Value!.CodeAttemptsLeft);
    }

    [Fact]
    public async Task Cancelling_ReturnsThePiece_AndTellsTheOtherParty()
    {
        var (luis, valeria, product, reserved) = await ReservedAsync();

        var cancelled = await service.CancelAsync(luis, reserved.Id, Ct);

        Assert.Equal((ReservationStatuses.Cancelled, "buyer"), (cancelled.Value!.Status, cancelled.Value.CancelledBy));
        Assert.Null(cancelled.Value.Code);
        Assert.Equal(1, (await products.FindByIdAsync(product, Ct))!.Stock);
        Assert.Equal((valeria, NotificationTypes.ReservationCancelled), (center.Sent[^1].UserId, center.Sent[^1].Type));
        Assert.Equal(ReservationsErrors.NotActive, (await service.CancelAsync(valeria, reserved.Id, Ct)).Error);
        // HU-51: la pieza que regresa revisa "Avísame".
        Assert.Equal([product], restocks.Products);
    }

    [Fact]
    public async Task After24Hours_ItExpires_ThePieceReturns_AndBothAreTold()
    {
        var (luis, valeria, product, reserved) = await ReservedAsync();

        Assert.Equal(0, await expiry.RunAsync(Now.UtcDateTime.AddHours(23), Ct));
        Assert.Equal(1, await expiry.RunAsync(Now.UtcDateTime.AddHours(25), Ct));
        Assert.Equal(0, await expiry.RunAsync(Now.UtcDateTime.AddHours(26), Ct));

        Assert.Equal(ReservationStatuses.Expired, (await service.GetAsync(luis, reserved.Id, Ct)).Value!.Status);
        Assert.Equal(1, (await products.FindByIdAsync(product, Ct))!.Stock);
        Assert.Equal(
            [(luis, NotificationTypes.ReservationExpired), (valeria, NotificationTypes.ReservationExpired)],
            center.Sent.TakeLast(2).Select(n => (n.UserId, n.Type)));
        Assert.Equal([product], restocks.Products);
    }

    [Fact]
    public async Task MyReservations_SplitTheTabs_WithBothCounts()
    {
        var (luis, valeria, _, reserved) = await ReservedAsync();

        var bought = await service.MineAsync(luis, null, Ct);
        var sold = await service.MineAsync(valeria, "seller", Ct);

        Assert.Equal([reserved.Id], bought.Items.Select(r => r.Id));
        Assert.Equal(new ReservationCounts(1, 0), bought.Counts);
        Assert.Equal([reserved.Id], sold.Items.Select(r => r.Id));
        Assert.Equal(new ReservationCounts(0, 1), sold.Counts);
    }

    [Fact]
    public async Task DeletingTheAccount_CancelsItsReservations_AndReturnsThePieces()
    {
        var (luis, _, product, reserved) = await ReservedAsync();

        await new ReservationAccountEraser(reservations, stock, time).EraseAsync(luis, Ct);

        Assert.Equal(ReservationStatuses.Cancelled, (await reservations.FindByIdAsync(reserved.Id, Ct))!.Status);
        Assert.Equal(1, (await products.FindByIdAsync(product, Ct))!.Stock);
    }
}
