using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Notifications.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.TestShared;
using UnitTest.Common;

namespace UnitTest.Orders;

/// <summary>
/// HU-62 (RF-31): una hora antes de la entrega de un pedido confirmado, las dos partes reciben "Hoy a la 1:00 p. m. en
/// Biblioteca con Diego" una sola vez; un pedido pendiente, cancelado o completado no recuerda nada. "Ya llegue" avisa a
/// la otra parte con un push y deja un aviso en el chat, una vez por parte y desde 2 horas antes.
/// </summary>
public sealed class DeliveryReminderTests
{
    private const string Uttt = InMemoryCommunityRepository.UtttId;

    // 18:00 UTC = 12:00 del campus; la entrega es a las 19:00 UTC (la 1:00 p. m.).
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateTime DeliveryAt = Now.UtcDateTime.AddHours(1);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryOrderRepository orders = new();
    private readonly RecordingNotificationCenter center = new();
    private readonly RecordingNotices notices = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly OrderArrivalService arrivals;

    public DeliveryReminderTests()
    {
        arrivals = new OrderArrivalService(orders, new MemberDirectory(new InMemoryCommunityRepository(), users), notices, center, time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OneHourBefore_BothPartiesAreReminded_WithTheCampusTimeAndPlace()
    {
        var (ana, diego, order) = await OrderAsync(OrderStatuses.Confirmed);

        Assert.Equal(1, await new DeliveryReminderJob(orders, center).RunAsync(Now.UtcDateTime, Ct));

        var toAna = Assert.Single(center.Sent, n => n.UserId == ana);
        Assert.Equal(NotificationTypes.DeliveryReminder, toAna.Type);
        Assert.Equal("Hoy a la 1:00 p. m. en Biblioteca con Diego.", toAna.Body);
        Assert.Equal(NotificationTargets.Order, toAna.TargetType);
        Assert.Equal(order, toAna.TargetId);
        Assert.Equal(NotificationCategories.Orders, toAna.Category);
        Assert.Equal($"delivery_reminder:{order}:buyer", toAna.DedupeKey);
        var toDiego = Assert.Single(center.Sent, n => n.UserId == diego);
        Assert.Equal("Hoy a la 1:00 p. m. en Biblioteca con Ana.", toDiego.Body);
        Assert.Equal($"delivery_reminder:{order}:seller", toDiego.DedupeKey);
    }

    [Fact]
    public async Task TheReminder_GoesOutOnce_AlthoughTheCheckRunsEveryFewMinutes()
    {
        var notifications = new InMemoryNotificationRepository();
        var real = new NotificationCenter(notifications, new PushQueue(), time, NullLogger<NotificationCenter>.Instance);
        var job = new DeliveryReminderJob(orders, real);
        var (ana, diego, _) = await OrderAsync(OrderStatuses.Confirmed);

        await job.RunAsync(Now.UtcDateTime, Ct);
        await job.RunAsync(Now.UtcDateTime.AddMinutes(5), Ct);
        await job.RunAsync(Now.UtcDateTime.AddMinutes(10), Ct);

        Assert.Single(await notifications.FindPageAsync(ana, null, null, 10, Ct));
        Assert.Single(await notifications.FindPageAsync(diego, null, null, 10, Ct));
    }

    [Theory]
    [InlineData(OrderStatuses.Pending)]
    [InlineData(OrderStatuses.Cancelled)]
    [InlineData(OrderStatuses.Completed)]
    public async Task AnOrderThatIsNotConfirmed_IsNotReminded(string status)
    {
        await OrderAsync(status);

        Assert.Equal(0, await new DeliveryReminderJob(orders, center).RunAsync(Now.UtcDateTime, Ct));
        Assert.Empty(center.Sent);
    }

    [Fact]
    public async Task ADeliveryMoreThanAnHourAway_OrAlreadyPast_IsNotReminded()
    {
        await OrderAsync(OrderStatuses.Confirmed);
        var job = new DeliveryReminderJob(orders, center);

        await job.RunAsync(Now.UtcDateTime.AddMinutes(-1), Ct);
        await job.RunAsync(DeliveryAt, Ct);

        Assert.Empty(center.Sent);
    }

    [Theory]
    [InlineData(19, 0, "la 1:00 p. m.")]
    [InlineData(15, 30, "las 9:30 a. m.")]
    [InlineData(18, 0, "las 12:00 p. m.")]
    [InlineData(6, 15, "las 12:15 a. m.")]
    [InlineData(2, 45, "las 8:45 p. m.")]
    public void TheCampusClock_WritesTheHourLikeTheApp(int utcHour, int minute, string expected) =>
        Assert.Equal(expected, CampusClock.Display(new DateTime(2026, 11, 23, utcHour, minute, 0, DateTimeKind.Utc)));

    [Fact]
    public async Task Arrived_TellsTheOtherParty_AndLeavesANoticeInTheChat()
    {
        var (ana, diego, order) = await OrderAsync(OrderStatuses.Confirmed);

        var result = await arrivals.ArriveAsync(ana, order, Ct);

        Assert.Null(result.Error);
        Assert.Equal(Now.UtcDateTime, result.Value!.Handover.BuyerArrivedAt);
        Assert.Null(result.Value.Handover.SellerArrivedAt);
        var notice = Assert.Single(notices.Posted);
        Assert.Equal((ana, "Ana ya llegó a Biblioteca", (string?)null), (notice.SenderId, notice.Text, notice.PushTitle));
        var push = Assert.Single(center.Sent);
        Assert.Equal(diego, push.UserId);
        Assert.Equal(NotificationTypes.OrderArrived, push.Type);
        Assert.Equal("Ana ya llegó", push.Title);
        Assert.Equal("Te espera en Biblioteca para el pedido #MS-1001.", push.Body);
        Assert.Equal($"order_arrived:{order}:buyer", push.DedupeKey);
    }

    [Fact]
    public async Task Arrived_Twice_TellsOnlyOnce_AndEachPartyHasItsOwn()
    {
        var (ana, diego, order) = await OrderAsync(OrderStatuses.Confirmed);

        await arrivals.ArriveAsync(ana, order, Ct);
        var again = await arrivals.ArriveAsync(ana, order, Ct);
        var seller = await arrivals.ArriveAsync(diego, order, Ct);

        Assert.Null(again.Error);
        Assert.Equal(2, notices.Posted.Count);
        Assert.Equal(2, center.Sent.Count);
        Assert.Equal(ana, center.Sent[1].UserId);
        Assert.NotNull(seller.Value!.Handover.BuyerArrivedAt);
        Assert.NotNull(seller.Value.Handover.SellerArrivedAt);
    }

    [Fact]
    public async Task Arrived_MoreThanTwoHoursBefore_IsTooEarly()
    {
        var (ana, _, order) = await OrderAsync(OrderStatuses.Confirmed, DeliveryAt.AddHours(2));

        var result = await arrivals.ArriveAsync(ana, order, Ct);

        Assert.Equal(OrdersErrors.ArrivalTooEarly, result.Error);
        Assert.Empty(center.Sent);
    }

    [Theory]
    [InlineData(OrderStatuses.Pending, "orders.not_confirmed")]
    [InlineData(OrderStatuses.Cancelled, "orders.closed")]
    [InlineData(OrderStatuses.Completed, "orders.closed")]
    public async Task Arrived_InAnOrderThatIsNotConfirmed_IsRejected(string status, string code)
    {
        var (ana, _, order) = await OrderAsync(status);

        var result = await arrivals.ArriveAsync(ana, order, Ct);

        Assert.Equal(code, result.Error!.Code);
        Assert.Empty(notices.Posted);
    }

    [Fact]
    public async Task Arrived_BySomeoneElse_IsNotFound_AndBySuspendedAccount_IsForbidden()
    {
        var (_, _, order) = await OrderAsync(OrderStatuses.Confirmed);
        var luis = await MemberAsync("Luis Ortega");
        var suspended = await MemberAsync("Sergio Vega", UserStatuses.Suspended);

        Assert.Equal(OrdersErrors.NotFound, (await arrivals.ArriveAsync(luis, order, Ct)).Error);
        Assert.Equal("auth.forbidden", (await arrivals.ArriveAsync(suspended, order, Ct)).Error!.Code);
        Assert.Empty(center.Sent);
    }

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
                EmailVerifiedAt = Now.UtcDateTime.AddDays(-30),
                CreatedAt = Now.UtcDateTime.AddDays(-30),
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    // Ana le compra a Diego la calculadora; la entrega es en la Biblioteca.
    private async Task<(string Ana, string Diego, string Order)> OrderAsync(string status, DateTime? at = null)
    {
        var ana = await MemberAsync("Ana Sofía Ramírez").ConfigureAwait(false);
        var diego = await MemberAsync("Diego Hernández").ConfigureAwait(false);
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
                SourceId = AuthIds.New(),
                Title = "Calculadora Casio fx-991ES Plus",
                PriceCents = 50_000,
                Quantity = 1,
                TotalCents = 50_000,
            },
            Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", At = at ?? DeliveryAt },
            Payment = new OrderPayment { Method = PaymentMethods.Cash, ChosenAt = Now.UtcDateTime },
            Status = status,
            History = [new OrderTransition { Status = status, At = Now.UtcDateTime }],
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        Assert.True(await orders.TryInsertAsync(order, Ct).ConfigureAwait(false));
        return (ana, diego, order.Id);
    }

    private sealed class RecordingNotices : IConversationNotices
    {
        public List<(string ConversationId, string SenderId, string Text, string? PushTitle)> Posted { get; } = [];

        public Task<IReadOnlyList<TranscriptLine>> RecentTextsAsync(string conversationId, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TranscriptLine>>([]);

        public Task PostNoticeAsync(string conversationId, string senderId, string text, string? pushTitle, CancellationToken cancellationToken)
        {
            Posted.Add((conversationId, senderId, text, pushTitle));
            return Task.CompletedTask;
        }

        public Task AgreementChangedAsync(IReadOnlyList<string> participants, string conversationId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
