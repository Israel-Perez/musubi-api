using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.GenAi.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Moderation;
using Musubi.TestShared;
using UnitTest.Common;

namespace UnitTest.Orders;

/// <summary>"Mis pedidos" (HU-25): mis compras por pestana, con cursor y los numeros de las tres.</summary>
public sealed class MyOrdersTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryOrderRepository orders = new();
    private readonly OrderService service;

    public MyOrdersTests()
    {
        var conversations = new InMemoryConversationRepository();
        var time = new ManualTimeProvider(new DateTimeOffset(Now));
        var center = new RecordingNotificationCenter();
        service = new OrderService(
            orders,
            new InMemoryMeetingPointRepository(),
            new ConversationDirectory(conversations),
            new ProductDirectory(new InMemoryProductRepository()),
            new OfferDirectory(new InMemoryOfferRepository(), new InMemoryWantedRepository()),
            new BlockDirectory(new InMemoryBlockRepository()),
            new MemberDirectory(new InMemoryCommunityRepository(), new InMemoryUserRepository()),
            new ProfileDirectory(new InMemoryProfileRepository(), new InMemoryCareerRepository()),
            new InMemoryContentModerator(),
            new AgreementDirectory(
                new InMemoryAgreementRepository(),
                new ConversationNotices(
                    conversations,
                    new InMemoryMessageRepository(),
                    new ChatNotifier(new RecordingHub(), NullLogger<ChatNotifier>.Instance),
                    center,
                    time),
                time),
            center,
            time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<OrderDocument> PlaceAsync(string buyer, string status = OrderStatuses.Pending, string? seller = null)
    {
        var order = new OrderDocument
        {
            Id = AuthIds.New(),
            CommunityId = AuthIds.New(),
            Number = 1001,
            BuyerId = buyer,
            SellerId = seller ?? AuthIds.New(),
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
            Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", At = Now.AddDays(1) },
            Status = status,
            History = [new OrderTransition { Status = status, At = Now }],
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        Assert.True(await orders.TryInsertAsync(order, Ct).ConfigureAwait(false));
        return order;
    }

    [Fact]
    public async Task Mine_OnlyMyPurchases_NewestFirst_WithTheThreeCounts()
    {
        var ana = AuthIds.New();
        var pending = await PlaceAsync(ana);
        var confirmed = await PlaceAsync(ana, OrderStatuses.Confirmed);
        var completed = await PlaceAsync(ana, OrderStatuses.Completed);
        var cancelled = await PlaceAsync(ana, OrderStatuses.Cancelled);
        await PlaceAsync(AuthIds.New(), seller: ana);
        await PlaceAsync(AuthIds.New());

        var page = (await service.MineAsync(ana, null, null, null, Ct)).Value!;

        Assert.Equal([cancelled.Id, completed.Id, confirmed.Id, pending.Id], page.Items.Select(o => o.Id));
        Assert.All(page.Items, o => Assert.Equal(OrderRoles.Buyer, o.Role));
        Assert.Equal(4, page.Total);
        Assert.Equal(new MyOrderCounts(2, 1, 1), page.Counts);
        Assert.Null(page.NextCursor);
    }

    [Theory]
    [InlineData(MyOrderTabs.Active, 2)]
    [InlineData(MyOrderTabs.Completed, 1)]
    [InlineData(MyOrderTabs.Cancelled, 1)]
    public async Task Mine_ByTab_OnlyThatTab_AndItsTotal(string tab, int expected)
    {
        var ana = AuthIds.New();
        await PlaceAsync(ana);
        await PlaceAsync(ana, OrderStatuses.Confirmed);
        await PlaceAsync(ana, OrderStatuses.Completed);
        await PlaceAsync(ana, OrderStatuses.Cancelled);

        var page = (await service.MineAsync(ana, tab, null, null, Ct)).Value!;

        Assert.Equal(expected, page.Items.Count);
        Assert.Equal(expected, page.Total);
        Assert.All(page.Items, o => Assert.Contains(o.Status, MyOrderTabs.Statuses(tab)!));
        Assert.Equal(new MyOrderCounts(2, 1, 1), page.Counts);
    }

    [Fact]
    public async Task Mine_PagesWithACursor_AndTheLastFullPageHasNoCursor()
    {
        var ana = AuthIds.New();
        var first = await PlaceAsync(ana);
        var second = await PlaceAsync(ana);
        var third = await PlaceAsync(ana);

        var one = (await service.MineAsync(ana, null, null, 2, Ct)).Value!;
        var two = (await service.MineAsync(ana, null, one.NextCursor, 2, Ct)).Value!;
        var exact = (await service.MineAsync(ana, null, null, 3, Ct)).Value!;

        Assert.Equal([third.Id, second.Id], one.Items.Select(o => o.Id));
        Assert.Equal(second.Id, one.NextCursor);
        Assert.Equal([first.Id], two.Items.Select(o => o.Id));
        Assert.Null(two.NextCursor);
        Assert.Null(exact.NextCursor);
        Assert.Equal(3, one.Total);
    }

    [Fact]
    public async Task Mine_RejectsAnUnknownTab_AndACursorThatIsNotAnId()
    {
        var ana = AuthIds.New();

        Assert.Equal(OrdersErrors.InvalidTab, (await service.MineAsync(ana, "pending", null, null, Ct)).Error);
        Assert.Equal(OrdersErrors.InvalidCursor, (await service.MineAsync(ana, null, "{\"$gt\":\"\"}", null, Ct)).Error);
    }

    [Fact]
    public async Task Mine_ClampsTheLimit_AndAnEmptyAccountHasZeroCounts()
    {
        var ana = AuthIds.New();
        for (var i = 0; i < 3; i++)
        {
            await PlaceAsync(ana);
        }

        var tiny = (await service.MineAsync(ana, null, null, 0, Ct)).Value!;
        var empty = (await service.MineAsync(AuthIds.New(), null, null, null, Ct)).Value!;

        Assert.Single(tiny.Items);
        Assert.NotNull(tiny.NextCursor);
        Assert.Empty(empty.Items);
        Assert.Equal((0L, new MyOrderCounts(0, 0, 0)), (empty.Total, empty.Counts));
    }
}
