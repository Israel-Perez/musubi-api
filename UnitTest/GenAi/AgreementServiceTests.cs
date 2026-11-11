using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Moderation;
using Musubi.TestShared;
using UnitTest.Common;

namespace UnitTest.GenAi;

/// <summary>
/// HU-32: la IA corre solo a peticion (RN-39), el chat avisa a los dos (RN-40), sale lo minimo (RNF-06) y si falla el
/// chat sigue (RNF-15). HU-33: lo que no se dijo o no pasa las reglas del pedido queda "No identificado" (RN-42).
/// HU-34: los dos corrigen y confirman la misma version (RN-41) y se puede descartar.
/// </summary>
public sealed class AgreementServiceTests
{
    private const string Uttt = InMemoryCommunityRepository.UtttId;

    // 18:00 UTC = 12:00 del martes 10 en el campus (UTC-6).
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 18, 0, 0, TimeSpan.Zero);

    // Manana a la 1:00 p. m. del campus.
    private static readonly DateTime Tomorrow = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryConversationRepository conversations = new();
    private readonly InMemoryMessageRepository messages = new();
    private readonly InMemoryOfferRepository offers = new();
    private readonly InMemoryWantedRepository requests = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly InMemoryOrderRepository orders = new();
    private readonly InMemoryAgreementRepository agreements = new();
    private readonly RecordingHub hub = new();
    private readonly ScriptedExtractor ai = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly AgreementService service;

    public AgreementServiceTests()
    {
        service = new AgreementService(
            agreements,
            ai,
            new ConversationDirectory(conversations),
            new ConversationNotices(conversations, messages, new ChatNotifier(hub, NullLogger<ChatNotifier>.Instance), time),
            new ProductDirectory(products),
            new OfferDirectory(offers, requests),
            new BlockDirectory(blocks),
            new MemberDirectory(new InMemoryCommunityRepository(), users),
            new OpenOrders(orders),
            new MeetingPointDirectory(new InMemoryMeetingPointRepository()),
            new InMemoryContentModerator(),
            Options.Create(new GenAiOptions()),
            time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ExtractedAgreement Found(
        string? product = "Calculadora Casio fx-991ES Plus",
        decimal? price = 450,
        int? quantity = 1,
        string? point = "biblioteca",
        string? detail = "Planta baja",
        DateOnly? date = null,
        TimeOnly? at = null,
        string? conditions = null) =>
        new(product, price, quantity, point, detail, date ?? new DateOnly(2026, 11, 11), at ?? new TimeOnly(13, 0), conditions);

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
                EmailVerifiedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 10, 1, 8, 55, 0, DateTimeKind.Utc),
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    // Diego vende una calculadora (2 piezas) y Ana le escribio.
    private async Task<(string Ana, string Diego, string Conversation)> ChatAsync(params string[] lines)
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
                Stock = 2,
                Status = ProductStatuses.Active,
                Photos = [$"media/products/{diego}/0123456789abcdef0123456789abcdef.webp"],
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false);
        var (conversation, _) = await conversations.InsertOrGetAsync(
            new ConversationDocument
            {
                Id = AuthIds.New(),
                CommunityId = Uttt,
                BuyerId = ana,
                SellerId = diego,
                Participants = [ana, diego],
                BuyerName = "Ana Sofía Ramírez",
                SellerName = "Diego Hernández",
                Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = product, Title = "Calculadora Casio" },
                LastMessageAt = Now.UtcDateTime,
                CreatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false);
        var at = Now.UtcDateTime.AddMinutes(-30);
        for (var i = 0; i < lines.Length; i++)
        {
            await messages.InsertAsync(
                new MessageDocument
                {
                    Id = AuthIds.New(),
                    ConversationId = conversation.Id,
                    CommunityId = Uttt,
                    SenderId = i % 2 == 0 ? ana : diego,
                    Text = lines[i],
                    CreatedAt = at.AddMinutes(i),
                },
                Ct).ConfigureAwait(false);
        }

        return (ana, diego, conversation.Id);
    }

    private Task<(string Ana, string Diego, string Conversation)> TalkedAsync() =>
        ChatAsync("Hola Diego, ¿me la dejas en $450? Mi cel es 773 123 4567", "Va, $450. ¿Mañana a la 1 en la Biblioteca?", "Sí, planta baja");

    private async Task<AgreementResponse> RequestedAsync(string userId, string conversation)
    {
        var result = await service.RequestAsync(userId, new RequestAgreementRequest(conversation), Ct).ConfigureAwait(false);
        Assert.Null(result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task Request_SendsOnlyTheRedactedChat_PostsTheNotice_AndSavesTheProposal()
    {
        var (ana, diego, conversation) = await TalkedAsync();
        ai.Next = Found();

        var agreement = await RequestedAsync(ana, conversation);

        // RNF-06: sin nombres, telefonos ni ids; quien habla es "Comprador" o "Vendedor".
        var prompt = ai.Prompts.Single();
        Assert.Equal(["Comprador", "Vendedor", "Comprador"], prompt.Lines.Select(l => l.Speaker));
        var sent = AgreementPromptText.User(prompt);
        Assert.DoesNotContain("Diego", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("Ana", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("773", sent, StringComparison.Ordinal);
        Assert.DoesNotContain(conversation, sent, StringComparison.Ordinal);
        Assert.Contains("Hola Vendedor", sent, StringComparison.Ordinal);
        Assert.Contains("Precio publicado: 500 MXN", sent, StringComparison.Ordinal);
        Assert.Equal(2, prompt.MaxQuantity);
        Assert.Equal(new DateOnly(2026, 11, 10), prompt.Today);
        Assert.Equal(14, prompt.Points.Count);

        // RN-40: el aviso queda en el chat, cuenta como no leido de Diego y llega en tiempo real a los dos.
        var notice = (await messages.FindPageAsync(conversation, null, 10, Ct))[0];
        Assert.Equal(MessageKinds.Notice, notice.Kind);
        Assert.Equal("Ana pidió un resumen con IA", notice.Text);
        Assert.Equal(1, (await conversations.FindByIdAsync(conversation, Ct))!.SellerUnread);
        Assert.Contains(hub.Sent, e => e.Method == ChatHub.MessageCreated);
        Assert.Contains(hub.Sent, e => e.Method == ChatHub.AgreementChanged && e.Users.SequenceEqual([ana, diego]));

        Assert.Equal(AgreementStatuses.Proposed, agreement.Status);
        Assert.Equal((AgreementRoles.Buyer, AgreementRoles.Buyer, 1), (agreement.Role, agreement.RequestedBy, agreement.Revision));
        Assert.Equal(("Ana Sofía Ramírez", "Diego Hernández"), (agreement.Buyer.Name, agreement.Seller.Name));
        Assert.Equal(45_000, agreement.Proposal.PriceCents);
        Assert.Equal(("biblioteca", "Biblioteca", "Planta baja"), (agreement.Proposal.MeetingPoint, agreement.Proposal.PointName, agreement.Proposal.MeetingDetail));
        Assert.Equal(Tomorrow, agreement.Proposal.DeliveryAt);
        Assert.Empty(agreement.NotIdentified);
        Assert.Empty(agreement.Missing);
        Assert.Equal(2, agreement.MaxQuantity);
        Assert.Equal(Now.UtcDateTime.AddDays(30), agreement.ExpiresAt);
    }

    [Fact]
    public async Task WhatTheAiDidNotFind_OrBreaksTheOrderRules_IsNotIdentified()
    {
        var (_, diego, conversation) = await TalkedAsync();
        // Sin precio, mas piezas de las que hay, un lugar fuera de la lista y una fecha que ya paso.
        ai.Next = Found(product: null, price: null, quantity: 5, point: "auditorio", detail: "[[inapropiado]]", date: new DateOnly(2026, 11, 9));

        var agreement = await RequestedAsync(diego, conversation);

        Assert.Equal("Calculadora Casio fx-991ES Plus", agreement.Proposal.Title);
        Assert.Null(agreement.Proposal.PriceCents);
        Assert.Null(agreement.Proposal.Quantity);
        Assert.Null(agreement.Proposal.MeetingPoint);
        Assert.Null(agreement.Proposal.MeetingDetail);
        Assert.Null(agreement.Proposal.DeliveryAt);
        Assert.Equal([AgreementFields.Price, AgreementFields.Quantity, AgreementFields.MeetingPoint, AgreementFields.DeliveryAt], agreement.NotIdentified);
        Assert.Equal(agreement.NotIdentified, agreement.Missing);
        Assert.Equal((AgreementRoles.Seller, AgreementRoles.Seller), (agreement.Role, agreement.RequestedBy));
        Assert.Equal("Diego pidió un resumen con IA", (await messages.FindPageAsync(conversation, null, 1, Ct)).Single().Text);
    }

    [Fact]
    public async Task IfTheAiFails_ItSaysSo_AndNothingIsSaved_ButTheChatKnowsItWasAsked()
    {
        var (ana, _, conversation) = await TalkedAsync();
        ai.Next = null;

        var result = await service.RequestAsync(ana, new RequestAgreementRequest(conversation), Ct);
        var current = await service.ByConversationAsync(ana, conversation, Ct);

        Assert.Equal("genai.unavailable", result.Error?.Code);
        Assert.Empty(current.Value!.Items);
        Assert.Equal(MessageKinds.Notice, (await messages.FindPageAsync(conversation, null, 1, Ct)).Single().Kind);
    }

    [Fact]
    public async Task WithoutAProvider_NothingLeavesTheServer()
    {
        var (ana, _, conversation) = await TalkedAsync();
        ai.Enabled = false;

        var result = await service.RequestAsync(ana, new RequestAgreementRequest(conversation), Ct);

        Assert.Equal("genai.unavailable", result.Error?.Code);
        Assert.Empty(ai.Prompts);
        Assert.Null((await messages.FindPageAsync(conversation, null, 1, Ct)).Single().Kind);
    }

    [Fact]
    public async Task WithoutMessages_ThereIsNothingToSummarize_AndTheAiIsNotCalled()
    {
        var (ana, _, conversation) = await ChatAsync();

        var result = await service.RequestAsync(ana, new RequestAgreementRequest(conversation), Ct);

        Assert.Equal("agreements.nothing_to_summarize", result.Error?.Code);
        Assert.Empty(ai.Prompts);
    }

    [Fact]
    public async Task OnlyItsParticipants_WithoutABlock_OrAnOpenOrder_CanAskForIt()
    {
        var (ana, diego, conversation) = await TalkedAsync();
        var luis = await MemberAsync("Luis Pérez");

        var stranger = await service.RequestAsync(luis, new RequestAgreementRequest(conversation), Ct);
        var invalid = await service.RequestAsync(ana, new RequestAgreementRequest("no-es-un-id"), Ct);
        await orders.TryInsertAsync(
            new OrderDocument
            {
                Id = AuthIds.New(),
                CommunityId = Uttt,
                Number = 1001,
                BuyerId = ana,
                SellerId = diego,
                BuyerName = "Ana",
                SellerName = "Diego",
                ConversationId = conversation,
                Item = new OrderItem { Source = OrderSources.Product, SourceId = AuthIds.New(), Title = "Calculadora", PriceCents = 1, Quantity = 1, TotalCents = 1 },
                Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", At = Tomorrow },
                Status = OrderStatuses.Pending,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            },
            Ct);
        var withOrder = await service.RequestAsync(ana, new RequestAgreementRequest(conversation), Ct);
        await blocks.InsertIfAbsentAsync(
            new BlockDocument { Id = AuthIds.New(), CommunityId = Uttt, BlockerId = diego, BlockedId = ana, BlockedName = "Ana", CreatedAt = Now.UtcDateTime },
            Ct);
        var blocked = await service.RequestAsync(ana, new RequestAgreementRequest(conversation), Ct);

        Assert.Equal("agreements.conversation_not_found", stranger.Error?.Code);
        Assert.Equal("agreements.invalid_conversation", invalid.Error?.Code);
        Assert.Equal("agreements.order_open", withOrder.Error?.Code);
        Assert.Equal("agreements.blocked", blocked.Error?.Code);
        Assert.Empty(ai.Prompts);
    }

    [Fact]
    public async Task Editing_ClearsBothConfirmations_AndKeepsWhatTheAiMissedMarked()
    {
        var (ana, diego, conversation) = await TalkedAsync();
        ai.Next = Found(date: null, at: null) with { Date = null, Time = null };
        var proposed = await RequestedAsync(ana, conversation);

        var edited = await service.UpdateAsync(
            diego,
            proposed.Id,
            new UpdateAgreementRequest(1, null, 42_000, 2, "cafeteria", "", Tomorrow, "Incluye la funda"),
            Ct);

        var a = edited.Value!;
        Assert.Equal(2, a.Revision);
        Assert.Equal((42_000L, 2), (a.Proposal.PriceCents!.Value, a.Proposal.Quantity!.Value));
        Assert.Equal(("cafeteria", "Cafetería"), (a.Proposal.MeetingPoint, a.Proposal.PointName));
        Assert.Null(a.Proposal.MeetingDetail);
        Assert.Equal(Tomorrow, a.Proposal.DeliveryAt);
        Assert.Equal("Incluye la funda", a.Proposal.Conditions);
        Assert.Equal([AgreementFields.DeliveryAt], a.NotIdentified);
        Assert.Empty(a.Missing);

        var confirmed = await service.ConfirmAsync(ana, a.Id, new ConfirmAgreementRequest(2), Ct);
        var again = await service.UpdateAsync(diego, a.Id, new UpdateAgreementRequest(2, "Calculadora con funda", null, null, null, null, null, null), Ct);
        Assert.NotNull(confirmed.Value!.BuyerConfirmedAt);
        Assert.Null(again.Value!.BuyerConfirmedAt);
        Assert.Equal(AgreementStatuses.Proposed, again.Value.Status);
    }

    [Theory]
    [InlineData(null, null, 3, null, null, null, "agreements.invalid_quantity")]
    [InlineData(null, 50L, null, null, null, null, "agreements.invalid_price")]
    [InlineData(null, null, null, "auditorio", null, null, "agreements.invalid_meeting_point")]
    [InlineData("", null, null, null, null, null, "agreements.invalid_title")]
    [InlineData(null, null, null, null, "[[inapropiado]]", null, "moderation.text_rejected")]
    [InlineData(null, null, null, null, null, 2, "agreements.invalid_delivery_time")]
    public async Task Editing_FollowsTheOrderRules(string? title, long? price, int? quantity, string? point, string? conditions, int? daysAgo, string code)
    {
        var (ana, _, conversation) = await TalkedAsync();
        ai.Next = Found();
        var proposed = await RequestedAsync(ana, conversation);
        DateTime? at = daysAgo is { } d ? Tomorrow.AddDays(-d) : null;

        var result = await service.UpdateAsync(ana, proposed.Id, new UpdateAgreementRequest(1, title, price, quantity, point, null, at, conditions), Ct);

        Assert.Equal(code, result.Error?.Code);
    }

    [Fact]
    public async Task OnlyBothConfirmingTheSameRevision_ConfirmsTheAgreement()
    {
        var (ana, diego, conversation) = await TalkedAsync();
        ai.Next = Found(price: null);
        var proposed = await RequestedAsync(ana, conversation);

        var incomplete = await service.ConfirmAsync(ana, proposed.Id, new ConfirmAgreementRequest(1), Ct);
        await service.UpdateAsync(diego, proposed.Id, new UpdateAgreementRequest(1, null, 45_000, null, null, null, null, null), Ct);
        var stale = await service.ConfirmAsync(ana, proposed.Id, new ConfirmAgreementRequest(1), Ct);
        var noRevision = await service.ConfirmAsync(ana, proposed.Id, new ConfirmAgreementRequest(null), Ct);
        var one = await service.ConfirmAsync(ana, proposed.Id, new ConfirmAgreementRequest(2), Ct);
        var both = await service.ConfirmAsync(diego, proposed.Id, new ConfirmAgreementRequest(2), Ct);

        Assert.Equal("agreements.incomplete", incomplete.Error?.Code);
        Assert.Equal("agreements.changed", stale.Error?.Code);
        Assert.Equal("agreements.invalid_revision", noRevision.Error?.Code);
        Assert.Equal(AgreementStatuses.Proposed, one.Value!.Status);
        Assert.Equal(AgreementStatuses.Confirmed, both.Value!.Status);
        Assert.Equal(Now.UtcDateTime, both.Value.SellerConfirmedAt);
    }

    [Fact]
    public async Task ADateThatAlreadyPassed_IsFixedBeforeConfirming()
    {
        var (ana, _, conversation) = await TalkedAsync();
        ai.Next = Found();
        var proposed = await RequestedAsync(ana, conversation);
        time.Advance(TimeSpan.FromDays(2));

        var result = await service.ConfirmAsync(ana, proposed.Id, new ConfirmAgreementRequest(1), Ct);

        Assert.Equal("agreements.invalid_delivery_time", result.Error?.Code);
    }

    [Fact]
    public async Task Discarding_DeletesIt_ForBoth_AndAStrangerCannotSeeOrTouchIt()
    {
        var (ana, diego, conversation) = await TalkedAsync();
        var luis = await MemberAsync("Luis Pérez");
        ai.Next = Found();
        var proposed = await RequestedAsync(ana, conversation);

        var peek = await service.ByConversationAsync(luis, conversation, Ct);
        var touch = await service.ConfirmAsync(luis, proposed.Id, new ConfirmAgreementRequest(1), Ct);
        var seen = await service.ByConversationAsync(diego, conversation, Ct);
        hub.Sent.Clear();
        var discarded = await service.DiscardAsync(diego, proposed.Id, Ct);
        var after = await service.ByConversationAsync(ana, conversation, Ct);
        var twice = await service.DiscardAsync(diego, proposed.Id, Ct);

        Assert.Equal("agreements.conversation_not_found", peek.Error?.Code);
        Assert.Equal("agreements.not_found", touch.Error?.Code);
        Assert.Equal(AgreementRoles.Seller, seen.Value!.Items.Single().Role);
        Assert.True(discarded.Value);
        Assert.Single(hub.Sent, e => e.Method == ChatHub.AgreementChanged);
        Assert.Empty(after.Value!.Items);
        Assert.Equal("agreements.not_found", twice.Error?.Code);
    }

    [Fact]
    public async Task ASecondSummary_ReplacesTheFirst()
    {
        var (ana, diego, conversation) = await TalkedAsync();
        ai.Next = Found();
        var first = await RequestedAsync(ana, conversation);
        ai.Next = Found(price: 400);

        var second = await RequestedAsync(diego, conversation);
        var current = await service.ByConversationAsync(ana, conversation, Ct);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(40_000, current.Value!.Items.Single().Proposal.PriceCents);
    }

    /// <summary>IA de prueba: devuelve lo que se le pide y guarda lo que recibio.</summary>
    private sealed class ScriptedExtractor : IAgreementExtractor
    {
        public bool Enabled { get; set; } = true;

        public ExtractedAgreement? Next { get; set; }

        public List<AgreementPrompt> Prompts { get; } = [];

        public Task<ExtractedAgreement?> ExtractAsync(AgreementPrompt prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(Next);
        }
    }
}
