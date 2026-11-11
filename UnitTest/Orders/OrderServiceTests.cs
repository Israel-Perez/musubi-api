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

/// <summary>
/// HU-23: quien compra crea el pedido desde su conversacion (publicacion u oferta aceptada), nace Pendiente
/// (RN-16), con cantidad dentro de lo disponible (RN-17), punto, detalle moderado y fecha y hora del campus; uno
/// abierto por conversacion; solo lo ven sus partes; el pago se registra en efectivo (RN-18).
/// </summary>
public sealed class OrderServiceTests
{
    private const string Uttt = InMemoryCommunityRepository.UtttId;

    // 18:00 UTC = 12:00 en el campus (UTC-6).
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 18, 0, 0, TimeSpan.Zero);

    // Manana a la 1:00 p. m. del campus.
    private static readonly DateTime Tomorrow = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryConversationRepository conversations = new();
    private readonly InMemoryOfferRepository offers = new();
    private readonly InMemoryWantedRepository requests = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly InMemoryProfileRepository profiles = new();
    private readonly InMemoryOrderRepository orders = new();
    private readonly InMemoryAgreementRepository agreements = new();
    private readonly RecordingHub hub = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly OrderService service;

    public OrderServiceTests()
    {
        service = new OrderService(
            orders,
            new InMemoryMeetingPointRepository(),
            new ConversationDirectory(conversations),
            new ProductDirectory(products),
            new OfferDirectory(offers, requests),
            new BlockDirectory(blocks),
            new MemberDirectory(new InMemoryCommunityRepository(), users),
            new ProfileDirectory(profiles, new InMemoryCareerRepository()),
            new InMemoryContentModerator(),
            new AgreementDirectory(
                agreements,
                new ConversationNotices(conversations, new InMemoryMessageRepository(), new ChatNotifier(hub, NullLogger<ChatNotifier>.Instance), time),
                time),
            time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateOrderRequest Body(
        string conversationId,
        int? quantity = 1,
        string? point = "biblioteca",
        string? detail = "Planta baja, junto a la entrada",
        DateTime? at = null) => new(conversationId, quantity, point, detail, at ?? Tomorrow);

    private async Task<string> MemberAsync(string name = "Ana Sofía Ramírez", string status = UserStatuses.Active)
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

    private async Task<string> ProductAsync(string seller, int stock = 1, string status = ProductStatuses.Active, long price = 50_000)
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
                PriceCents = price,
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

    private async Task<string> ConversationAsync(string buyer, string seller, string contextId, string type = ConversationContextTypes.Product)
    {
        var (conversation, _) = await conversations.InsertOrGetAsync(
            new ConversationDocument
            {
                Id = AuthIds.New(),
                CommunityId = Uttt,
                BuyerId = buyer,
                SellerId = seller,
                Participants = [buyer, seller],
                Context = new ConversationContext { Type = type, Id = contextId, Title = "Calculadora Casio" },
                LastMessageAt = Now.UtcDateTime,
                CreatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false);
        return conversation.Id;
    }

    // Diego vende una calculadora y Ana le escribio para comprarla.
    private async Task<(string Ana, string Diego, string Product, string Conversation)> ChatAsync(int stock = 1, string status = ProductStatuses.Active)
    {
        var ana = await MemberAsync().ConfigureAwait(false);
        var diego = await MemberAsync("Diego Hernández").ConfigureAwait(false);
        var product = await ProductAsync(diego, stock, status).ConfigureAwait(false);
        return (ana, diego, product, await ConversationAsync(ana, diego, product).ConfigureAwait(false));
    }

    private async Task<string> OfferAsync(string requester, string seller, string status, string? productId = null, int requestQuantity = 1)
    {
        var request = new WantedRequestDocument
        {
            Id = AuthIds.New(),
            CommunityId = Uttt,
            RequesterId = requester,
            RequesterName = "Ana S.",
            Title = "Calculadora científica",
            CategorySlug = "calculadoras",
            CategoryName = "Calculadoras",
            BudgetCents = 60_000,
            Quantity = requestQuantity,
            ExpiresAt = Now.UtcDateTime.AddDays(5),
            CreatedAt = Now.UtcDateTime.AddHours(-1),
        };
        await requests.InsertAsync(request, Ct).ConfigureAwait(false);
        var offer = new OfferDocument
        {
            Id = AuthIds.New(),
            CommunityId = Uttt,
            RequestId = request.Id,
            RequesterId = requester,
            SellerId = seller,
            SellerName = "Diego H.",
            Kind = productId is null ? OfferKinds.Direct : OfferKinds.Listing,
            ProductId = productId,
            Title = "Casio fx-991ES Plus con tapa",
            PriceCents = 42_000,
            Status = status,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        Assert.True(await offers.TryInsertAsync(offer, Ct).ConfigureAwait(false));
        return offer.Id;
    }

    [Fact]
    public async Task Create_FromTheProductChat_IsPending_CopiesTheProduct_AndIsNumbered()
    {
        var (ana, diego, product, conversation) = await ChatAsync(stock: 3);

        var result = await service.CreateAsync(ana, Body(conversation, quantity: 2), Ct);

        var order = result.Value!;
        Assert.Null(result.Error);
        Assert.Equal("MS-1001", order.Number);
        Assert.Equal(OrderStatuses.Pending, order.Status);
        Assert.Equal("buyer", order.Role);
        Assert.Equal(conversation, order.ConversationId);
        Assert.Equal(OrderSources.Product, order.Item.Source);
        Assert.Equal(product, order.Item.ProductId);
        Assert.Equal("Calculadora Casio fx-991ES Plus", order.Item.Title);
        Assert.Equal($"/media/products/{diego}/0123456789abcdef0123456789abcdef.webp", order.Item.Photo);
        Assert.Equal(50_000, order.Item.PriceCents);
        Assert.Equal(2, order.Item.Quantity);
        Assert.Equal(100_000, order.Item.TotalCents);
        Assert.Equal(new OrderPartyResponse(ana, "Ana Sofía Ramírez"), order.Buyer);
        Assert.Equal(new OrderPartyResponse(diego, "Diego Hernández"), order.Seller);
        Assert.Equal(new OrderDeliveryResponse("biblioteca", "Biblioteca", "Planta baja, junto a la entrada", Tomorrow), order.Delivery);
        Assert.Null(order.Payment);
        Assert.False(order.TransferAvailable);
        var stored = await orders.FindByIdAsync(order.Id, Ct);
        Assert.Equal([OrderStatuses.Pending], stored!.History.Select(h => h.Status));
        // RF-34: crear el pedido no aparta piezas; eso pasa al aceptarlo (HU-24).
        Assert.Equal(3, (await products.FindByIdAsync(product, Ct))!.Stock);
    }

    [Fact]
    public async Task Create_Twice_InTheSameConversation_IsAlreadyOpen()
    {
        var (ana, _, _, conversation) = await ChatAsync();

        await service.CreateAsync(ana, Body(conversation), Ct);
        var again = await service.CreateAsync(ana, Body(conversation), Ct);

        Assert.Equal(OrdersErrors.AlreadyOpen, again.Error);
        Assert.Single(await orders.FindByConversationAsync(conversation, 10, Ct));
    }

    [Fact]
    public async Task Create_NumbersAreConsecutive_AcrossConversations()
    {
        var first = await ChatAsync();
        var second = await ChatAsync();

        var one = await service.CreateAsync(first.Ana, Body(first.Conversation), Ct);
        var two = await service.CreateAsync(second.Ana, Body(second.Conversation), Ct);

        Assert.Equal("MS-1001", one.Value!.Number);
        Assert.Equal("MS-1002", two.Value!.Number);
    }

    [Fact]
    public async Task Create_ByTheSeller_IsOnlyBuyer()
    {
        var (_, diego, _, conversation) = await ChatAsync();

        var result = await service.CreateAsync(diego, Body(conversation), Ct);

        Assert.Equal(OrdersErrors.OnlyBuyer, result.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_OnSomeoneElsesOrAnUnknownConversation_IsNotFound(bool existing)
    {
        var (_, _, _, conversation) = await ChatAsync();
        var stranger = await MemberAsync("Luis Pérez");

        var result = await service.CreateAsync(stranger, Body(existing ? conversation : AuthIds.New()), Ct);
        var invalid = await service.CreateAsync(stranger, Body("no-es-un-id"), Ct);

        Assert.Equal(OrdersErrors.ConversationNotFound, result.Error);
        Assert.Equal(OrdersErrors.ConversationNotFound, invalid.Error);
    }

    [Fact]
    public async Task Create_WithASuspendedAccount_IsForbidden()
    {
        var (_, diego, product, _) = await ChatAsync();
        var suspended = await MemberAsync("Luis Pérez", UserStatuses.Suspended);
        var conversation = await ConversationAsync(suspended, diego, product);

        var result = await service.CreateAsync(suspended, Body(conversation), Ct);

        Assert.Equal(403, result.Error?.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(null)]
    public async Task Create_QuantityOutOf1To10_IsInvalid(int? quantity)
    {
        var (ana, _, _, conversation) = await ChatAsync(stock: 20);

        var result = await service.CreateAsync(ana, Body(conversation, quantity: quantity), Ct);

        Assert.Equal(OrdersErrors.InvalidQuantity, result.Error);
    }

    [Fact]
    public async Task Create_MorePiecesThanAvailable_IsNotEnoughStock()
    {
        var (ana, _, _, conversation) = await ChatAsync(stock: 2);

        var tooMany = await service.CreateAsync(ana, Body(conversation, quantity: 3), Ct);
        var all = await service.CreateAsync(ana, Body(conversation, quantity: 2), Ct);

        Assert.Equal(OrdersErrors.NotEnoughStock, tooMany.Error);
        Assert.Null(all.Error);
    }

    [Theory]
    [InlineData(ProductStatuses.Hidden)]
    [InlineData(ProductStatuses.SoldOut)]
    [InlineData(ProductStatuses.Removed)]
    public async Task Create_WhenTheListingIsNotAvailable_IsProductUnavailable(string status)
    {
        var (ana, _, _, conversation) = await ChatAsync(status: status);

        var result = await service.CreateAsync(ana, Body(conversation), Ct);

        Assert.Equal(OrdersErrors.ProductUnavailable, result.Error);
    }

    [Fact]
    public async Task Create_WhenTheListingWasDeleted_IsProductUnavailable()
    {
        var (ana, diego, product, conversation) = await ChatAsync();
        await products.DeleteAsync(product, diego, Ct);

        var result = await service.CreateAsync(ana, Body(conversation), Ct);

        Assert.Equal(OrdersErrors.ProductUnavailable, result.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_WithABlockInEitherDirection_IsBlocked(bool buyerBlocked)
    {
        var (ana, diego, _, conversation) = await ChatAsync();
        await blocks.InsertIfAbsentAsync(
            new BlockDocument
            {
                Id = AuthIds.New(),
                CommunityId = Uttt,
                BlockerId = buyerBlocked ? ana : diego,
                BlockedId = buyerBlocked ? diego : ana,
                BlockedName = "Alguien",
                CreatedAt = Now.UtcDateTime,
            },
            Ct);

        var result = await service.CreateAsync(ana, Body(conversation), Ct);

        Assert.Equal(OrdersErrors.Blocked, result.Error);
    }

    [Theory]
    [InlineData("estacionamiento")]
    [InlineData(null)]
    public async Task Create_WithAPointThatIsNotOnTheCampusList_IsInvalid(string? point)
    {
        var (ana, _, _, conversation) = await ChatAsync();

        var result = await service.CreateAsync(ana, Body(conversation, point: point), Ct);

        Assert.Equal(OrdersErrors.InvalidMeetingPoint, result.Error);
    }

    [Fact]
    public async Task Create_DetailOver120Characters_IsInvalid_AndABlankOneIsDropped()
    {
        var (ana, _, _, conversation) = await ChatAsync();

        var tooLong = await service.CreateAsync(ana, Body(conversation, detail: new string('a', 121)), Ct);
        var blank = await service.CreateAsync(ana, Body(conversation, detail: "   "), Ct);

        Assert.Equal(OrdersErrors.InvalidDetail, tooLong.Error);
        Assert.Null(blank.Value!.Delivery.Detail);
    }

    [Fact]
    public async Task Create_ARejectedDetail_IsNotSaved()
    {
        var (ana, _, _, conversation) = await ChatAsync();

        var result = await service.CreateAsync(ana, Body(conversation, detail: "Atrás " + InMemoryContentModerator.RejectMarker), Ct);

        Assert.Equal("moderation.text_rejected", result.Error?.Code);
        Assert.Empty(await orders.FindByConversationAsync(conversation, 10, Ct));
    }

    public static TheoryData<DateTime?> BadTimes => new()
    {
        // En 20 minutos (antes de 30).
        Now.UtcDateTime.AddMinutes(20),
        // En 15 dias.
        Now.UtcDateTime.AddDays(15),
        // Manana a las 6:30 y a las 21:30 del campus.
        new DateTime(2026, 11, 11, 12, 30, 0, DateTimeKind.Utc),
        new DateTime(2026, 11, 12, 3, 30, 0, DateTimeKind.Utc),
    };

    [Theory]
    [MemberData(nameof(BadTimes))]
    public async Task Create_OutsideTheDeliveryWindow_IsInvalid(DateTime? at)
    {
        var (ana, _, _, conversation) = await ChatAsync();

        var result = await service.CreateAsync(ana, Body(conversation, at: at), Ct);

        Assert.Equal(OrdersErrors.InvalidDeliveryTime, result.Error);
    }

    [Fact]
    public async Task Create_At7And21OfTheCampus_AndIn14Days_AreAllowed()
    {
        var early = await ChatAsync();
        var late = await ChatAsync();
        var far = await ChatAsync();

        var at7 = await service.CreateAsync(early.Ana, Body(early.Conversation, at: new DateTime(2026, 11, 11, 13, 0, 0, DateTimeKind.Utc)), Ct);
        var at21 = await service.CreateAsync(late.Ana, Body(late.Conversation, at: new DateTime(2026, 11, 12, 3, 0, 0, DateTimeKind.Utc)), Ct);
        var in14 = await service.CreateAsync(far.Ana, Body(far.Conversation, at: Now.UtcDateTime.AddDays(14)), Ct);

        Assert.Null(at7.Error);
        Assert.Null(at21.Error);
        Assert.Null(in14.Error);
    }

    [Fact]
    public async Task Create_FromAnAcceptedOffer_UsesTheOfferAndItsPrice()
    {
        var ana = await MemberAsync();
        var diego = await MemberAsync("Diego Hernández");
        var offer = await OfferAsync(ana, diego, OfferStatuses.Accepted);
        var conversation = await ConversationAsync(ana, diego, offer, ConversationContextTypes.Offer);

        var result = await service.CreateAsync(ana, Body(conversation), Ct);

        var order = result.Value!;
        Assert.Equal(OrderSources.Offer, order.Item.Source);
        Assert.Equal(offer, order.Item.SourceId);
        Assert.Null(order.Item.ProductId);
        Assert.Equal("Casio fx-991ES Plus con tapa", order.Item.Title);
        Assert.Equal(42_000, order.Item.PriceCents);
    }

    [Fact]
    public async Task Create_FromAnOfferThatIsNotAccepted_IsOfferNotAccepted()
    {
        var ana = await MemberAsync();
        var diego = await MemberAsync("Diego Hernández");
        var offer = await OfferAsync(ana, diego, OfferStatuses.Sent);
        var conversation = await ConversationAsync(ana, diego, offer, ConversationContextTypes.Offer);

        var result = await service.CreateAsync(ana, Body(conversation), Ct);

        Assert.Equal(OrdersErrors.OfferNotAccepted, result.Error);
    }

    [Fact]
    public async Task Create_FromADirectOffer_IsLimitedToTheRequestQuantity()
    {
        var ana = await MemberAsync();
        var diego = await MemberAsync("Diego Hernández");
        var offer = await OfferAsync(ana, diego, OfferStatuses.Accepted, requestQuantity: 2);
        var conversation = await ConversationAsync(ana, diego, offer, ConversationContextTypes.Offer);

        var tooMany = await service.CreateAsync(ana, Body(conversation, quantity: 3), Ct);
        var two = await service.CreateAsync(ana, Body(conversation, quantity: 2), Ct);

        Assert.Equal(OrdersErrors.NotEnoughStock, tooMany.Error);
        Assert.Equal(84_000, two.Value!.Item.TotalCents);
    }

    [Fact]
    public async Task Create_FromALinkedOffer_NeedsTheListingAvailable_AndWithPieces()
    {
        var ana = await MemberAsync();
        var diego = await MemberAsync("Diego Hernández");
        var paused = await ProductAsync(diego, status: ProductStatuses.Hidden);
        var available = await ProductAsync(diego, stock: 1);
        var onPaused = await ConversationAsync(ana, diego, await OfferAsync(ana, diego, OfferStatuses.Accepted, paused), ConversationContextTypes.Offer);
        var onAvailable = await ConversationAsync(ana, diego, await OfferAsync(ana, diego, OfferStatuses.Accepted, available), ConversationContextTypes.Offer);

        var unavailable = await service.CreateAsync(ana, Body(onPaused), Ct);
        var tooMany = await service.CreateAsync(ana, Body(onAvailable, quantity: 2), Ct);
        var ok = await service.CreateAsync(ana, Body(onAvailable), Ct);

        Assert.Equal(OrdersErrors.ProductUnavailable, unavailable.Error);
        Assert.Equal(OrdersErrors.NotEnoughStock, tooMany.Error);
        Assert.Equal(available, ok.Value!.Item.ProductId);
        Assert.Equal($"/media/products/{diego}/0123456789abcdef0123456789abcdef.webp", ok.Value.Item.Photo);
    }

    [Fact]
    public async Task Create_WhenTheConversationDoesNotMatchTheListingOrTheOffer_IsProductUnavailable()
    {
        var ana = await MemberAsync();
        var diego = await MemberAsync("Diego Hernández");
        var luis = await MemberAsync("Luis Pérez");
        // Una conversacion de Ana con Luis que apunta a la publicacion de Diego, y otra a una oferta de Diego.
        var listingOfSomeoneElse = await ConversationAsync(ana, luis, await ProductAsync(diego));
        var offerOfSomeoneElse = await ConversationAsync(ana, luis, await OfferAsync(ana, diego, OfferStatuses.Accepted), ConversationContextTypes.Offer);

        var onListing = await service.CreateAsync(ana, Body(listingOfSomeoneElse), Ct);
        var onOffer = await service.CreateAsync(ana, Body(offerOfSomeoneElse), Ct);

        Assert.Equal(OrdersErrors.ProductUnavailable, onListing.Error);
        Assert.Equal(OrdersErrors.ProductUnavailable, onOffer.Error);
    }

    [Fact]
    public async Task Get_OnlyTheBuyerAndTheSeller_SeeTheOrder()
    {
        var (ana, diego, _, conversation) = await ChatAsync();
        var stranger = await MemberAsync("Luis Pérez");
        var id = (await service.CreateAsync(ana, Body(conversation), Ct)).Value!.Id;

        var asBuyer = await service.GetAsync(ana, id, Ct);
        var asSeller = await service.GetAsync(diego, id, Ct);
        var asStranger = await service.GetAsync(stranger, id, Ct);
        var unknown = await service.GetAsync(ana, AuthIds.New(), Ct);

        Assert.Equal("buyer", asBuyer.Value!.Role);
        Assert.Equal("seller", asSeller.Value!.Role);
        Assert.Equal(OrdersErrors.NotFound, asStranger.Error);
        Assert.Equal(OrdersErrors.NotFound, unknown.Error);
    }

    [Fact]
    public async Task ByConversation_ListsItsOrders_OnlyForItsParticipants()
    {
        var (ana, diego, _, conversation) = await ChatAsync();
        var stranger = await MemberAsync("Luis Pérez");
        var id = (await service.CreateAsync(ana, Body(conversation), Ct)).Value!.Id;

        var forSeller = await service.ByConversationAsync(diego, conversation, Ct);
        var forStranger = await service.ByConversationAsync(stranger, conversation, Ct);
        var missing = await service.ByConversationAsync(ana, null, Ct);

        Assert.Equal([id], forSeller.Value!.Items.Select(o => o.Id));
        Assert.Equal(OrdersErrors.ConversationNotFound, forStranger.Error);
        Assert.Equal(OrdersErrors.InvalidConversation, missing.Error);
    }

    [Fact]
    public async Task SetPayment_Cash_IsRegistered_ByTheBuyer()
    {
        var (ana, _, _, conversation) = await ChatAsync();
        var id = (await service.CreateAsync(ana, Body(conversation), Ct)).Value!.Id;
        time.Advance(TimeSpan.FromMinutes(2));

        var result = await service.SetPaymentAsync(ana, id, new SetPaymentRequest(PaymentMethods.Cash), Ct);

        Assert.Equal(new OrderPaymentResponse(PaymentMethods.Cash, Now.UtcDateTime.AddMinutes(2)), result.Value!.Payment);
        Assert.Equal(OrderStatuses.Pending, result.Value.Status);
    }

    [Theory]
    [InlineData(PaymentMethods.Transfer, "orders.payment_unavailable")]
    [InlineData("tarjeta", "orders.invalid_payment")]
    [InlineData(null, "orders.invalid_payment")]
    public async Task SetPayment_OtherThanCash_IsRejected(string? method, string code)
    {
        var (ana, _, _, conversation) = await ChatAsync();
        var id = (await service.CreateAsync(ana, Body(conversation), Ct)).Value!.Id;

        var result = await service.SetPaymentAsync(ana, id, new SetPaymentRequest(method), Ct);

        Assert.Equal(code, result.Error?.Code);
        Assert.Null((await orders.FindByIdAsync(id, Ct))!.Payment);
    }

    [Fact]
    public async Task SetPayment_BySellerOrStranger_IsRejected_WithoutChanges()
    {
        var (ana, diego, _, conversation) = await ChatAsync();
        var stranger = await MemberAsync("Luis Pérez");
        var id = (await service.CreateAsync(ana, Body(conversation), Ct)).Value!.Id;

        var bySeller = await service.SetPaymentAsync(diego, id, new SetPaymentRequest(PaymentMethods.Cash), Ct);
        var byStranger = await service.SetPaymentAsync(stranger, id, new SetPaymentRequest(PaymentMethods.Cash), Ct);

        Assert.Equal(OrdersErrors.OnlyBuyer, bySeller.Error);
        Assert.Equal(OrdersErrors.NotFound, byStranger.Error);
        Assert.Null((await orders.FindByIdAsync(id, Ct))!.Payment);
    }

    [Fact]
    public async Task SetPayment_OnAnOrderThatIsNoLongerPending_IsNotPending()
    {
        var (ana, _, _, conversation) = await ChatAsync();
        var id = (await service.CreateAsync(ana, Body(conversation), Ct)).Value!.Id;
        await orders.ReleaseAccountAsync(ana, OrderService.DeletedName, Now.UtcDateTime, Ct);

        var result = await service.SetPaymentAsync(ana, id, new SetPaymentRequest(PaymentMethods.Cash), Ct);

        Assert.Equal(OrdersErrors.NotPending, result.Error);
    }

    [Fact]
    public async Task MeetingPoints_AreTheCampusList_WithMyCareerBuildingSuggested()
    {
        var ana = await MemberAsync();
        var luis = await MemberAsync("Luis Pérez");
        await profiles.SetDetailsAsync(new ProfileOwner(ana, Uttt, "Ana Sofía Ramírez"), "ing-software", 8, Now.UtcDateTime, Ct);

        var mine = (await service.MeetingPointsAsync(ana, Ct)).Value!.Items;
        var noCareer = (await service.MeetingPointsAsync(luis, Ct)).Value!.Items;

        Assert.Equal(14, mine.Count);
        Assert.Equal("biblioteca", mine[0].Slug);
        Assert.Equal(["tecnologias-informacion"], mine.Where(p => p.Suggested).Select(p => p.Slug));
        Assert.DoesNotContain(noCareer, p => p.Suggested);
    }

    [Fact]
    public async Task MeetingPoints_SeedMatchesTheScript_AndEveryCareerExists()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(RepoPaths.Root, "db", "014-pedidos.js"), Ct);
        var careers = InMemoryCareerRepository.Seed.Select(c => c.Slug).ToHashSet(StringComparer.Ordinal);

        var at = 0;
        foreach (var (slug, name, pointCareers) in InMemoryMeetingPointRepository.Seed)
        {
            var found = script.IndexOf($"['{slug}', '{name}', [", at, StringComparison.Ordinal);
            Assert.True(found >= 0, $"{slug} no esta en db/014-pedidos.js en ese orden");
            at = found;
            Assert.All(pointCareers, c => Assert.Contains(c, careers));
            Assert.All(pointCareers, c => Assert.Contains($"'{c}'", script, StringComparison.Ordinal));
        }

        var assigned = InMemoryMeetingPointRepository.Seed.SelectMany(p => p.Careers).ToList();
        Assert.Equal(careers.Order(StringComparer.Ordinal), assigned.Order(StringComparer.Ordinal));
    }

    // HU-34: un acuerdo con IA de la conversacion, como lo deja AgreementService.
    private async Task<AgreementDocument> AgreementAsync(string ana, string diego, string conversation, bool confirmed = true, long price = 42_000, int quantity = 2)
    {
        var agreement = new AgreementDocument
        {
            Id = AuthIds.New(),
            CommunityId = Uttt,
            ConversationId = conversation,
            BuyerId = ana,
            SellerId = diego,
            RequestedBy = ana,
            Proposal = new AgreementProposal
            {
                Title = "Calculadora Casio con funda",
                PriceCents = price,
                Quantity = quantity,
                PointSlug = "cafeteria",
                PointName = "Cafetería",
                Detail = "Junto a la caja",
                At = Tomorrow,
            },
            Status = confirmed ? AgreementStatuses.Confirmed : AgreementStatuses.Proposed,
            BuyerConfirmedAt = Now.UtcDateTime,
            SellerConfirmedAt = confirmed ? Now.UtcDateTime : null,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
            ExpiresAt = Now.UtcDateTime.AddDays(30),
        };
        return await agreements.ReplaceForConversationAsync(agreement, Ct).ConfigureAwait(false);
    }

    [Fact]
    public async Task FromAConfirmedAgreement_TheOrderTakesItsData_AndTheAgreementIsDeleted()
    {
        var (ana, diego, _, conversation) = await ChatAsync(stock: 3);
        var agreement = await AgreementAsync(ana, diego, conversation);

        // RF-47: lo del cuerpo no cuenta; manda el acuerdo que confirmaron los dos (RN-41).
        var result = await service.CreateAsync(ana, new CreateOrderRequest(null, 1, "biblioteca", null, null, agreement.Id), Ct);

        var order = result.Value!;
        Assert.Equal(conversation, order.ConversationId);
        Assert.Equal(("Calculadora Casio con funda", 42_000L, 2, 84_000L), (order.Item.Title, order.Item.PriceCents, order.Item.Quantity, order.Item.TotalCents));
        Assert.Equal(("cafeteria", "Junto a la caja", Tomorrow), (order.Delivery.Point, order.Delivery.Detail, order.Delivery.At));
        Assert.Equal(OrderStatuses.Pending, order.Status);
        Assert.Null(await agreements.FindByIdAsync(agreement.Id, Now.UtcDateTime, Ct));
        Assert.Contains(hub.Sent, e => e.Method == ChatHub.AgreementChanged && e.Users.SequenceEqual([ana, diego]));
    }

    [Fact]
    public async Task FromAnAgreement_OnlyTheBuyer_OnceBothConfirmed_AndWithinTheStock()
    {
        var (ana, diego, _, conversation) = await ChatAsync(stock: 1);
        var luis = await MemberAsync("Luis Pérez");
        var pending = await AgreementAsync(ana, diego, conversation, confirmed: false, quantity: 1);

        var notConfirmed = await service.CreateAsync(ana, new CreateOrderRequest(null, null, null, null, null, pending.Id), Ct);
        var tooMany = await AgreementAsync(ana, diego, conversation, quantity: 2);
        var noStock = await service.CreateAsync(ana, new CreateOrderRequest(null, null, null, null, null, tooMany.Id), Ct);
        var bySeller = await service.CreateAsync(diego, new CreateOrderRequest(null, null, null, null, null, tooMany.Id), Ct);
        var byStranger = await service.CreateAsync(luis, new CreateOrderRequest(null, null, null, null, null, tooMany.Id), Ct);
        var missing = await service.CreateAsync(ana, new CreateOrderRequest(null, null, null, null, null, AuthIds.New()), Ct);

        Assert.Equal("orders.agreement_not_confirmed", notConfirmed.Error?.Code);
        Assert.Equal("orders.not_enough_stock", noStock.Error?.Code);
        Assert.Equal("orders.only_buyer", bySeller.Error?.Code);
        Assert.Equal("orders.agreement_not_found", byStranger.Error?.Code);
        Assert.Equal("orders.agreement_not_found", missing.Error?.Code);
        Assert.NotNull(await agreements.FindByIdAsync(tooMany.Id, Now.UtcDateTime, Ct));
    }
}
