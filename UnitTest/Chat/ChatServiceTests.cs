using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;
using Musubi.TestShared;
using SkiaSharp;
using StackExchange.Redis;
using UnitTest.Common;

namespace UnitTest.Chat;

/// <summary>
/// HU-20: reglas del chat que el flujo HTTP no alcanza facil (otro campus, publicacion pausada o quitada,
/// cuenta suspendida), la degradacion controlada si Redis (el backplane) no responde, el bloqueo (HU-57) y la
/// foto de la otra persona.
/// </summary>
public sealed class ChatServiceTests
{
    private const string OtherCommunity = "000000000000000000000002";

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryConversationRepository conversations = new();
    private readonly InMemoryMessageRepository messages = new();
    private readonly FakeProducts products = new();
    private readonly FakeHub hub = new();
    private readonly InMemoryProfileRepository profiles = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly InMemoryMediaStorage media = new();
    private readonly ManualTimeProvider time = new(new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero));
    private readonly ChatService service;

    private readonly RecordingNotificationCenter center = new();
    public ChatServiceTests()
    {
        service = new ChatService(
            conversations,
            messages,
            products,
            new MemberDirectory(new InMemoryCommunityRepository(), users),
            new ProfileDirectory(profiles, new InMemoryCareerRepository()),
            new BlockDirectory(blocks),
            new InMemoryContentModerator(),
            new ChatNotifier(hub, NullLogger<ChatNotifier>.Instance),
            center,
            new ProfileResponses(profiles),
            media,
            time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync(string communityId = InMemoryCommunityRepository.UtttId, string status = UserStatuses.Active, string name = "Ana Sofía Ramírez")
    {
        var id = AuthIds.New();
        Assert.True(await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = communityId,
                Email = $"ana.{id}@uttt.edu.mx",
                EmailNormalized = $"ana.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = name,
                Status = status,
                EmailVerifiedAt = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 10, 1, 8, 55, 0, DateTimeKind.Utc),
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private ProductSummary Product(string sellerId, string communityId = InMemoryCommunityRepository.UtttId, bool visible = true)
    {
        var product = new ProductSummary(AuthIds.New(), communityId, sellerId, "Calculadora Casio fx-991ES Plus", 45_000, "used", "good", "/media/products/x.webp", visible);
        products.Items[product.Id] = product;
        return product;
    }

    // HU-65: una conversacion de Ana (compra) con Diego (vende).
    private async Task<(string Buyer, string Seller, string Conversation)> ConversationAsync()
    {
        var seller = await MemberAsync(name: "Diego Hernández").ConfigureAwait(false);
        var buyer = await MemberAsync().ConfigureAwait(false);
        var started = await service.StartFromProductAsync(buyer, new StartConversationRequest(Product(seller).Id), Ct).ConfigureAwait(false);
        return (buyer, seller, started.Value!.Conversation.Id);
    }

    private static FormFile Photo(SKColor color)
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = png.ToArray();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photo", "foto.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png",
        };
    }

    [Fact]
    public async Task SendPhoto_IsModerated_SavedOutsideTheCdn_AndDeliveredLikeAMessage()
    {
        var (buyer, seller, conversation) = await ConversationAsync();

        var sent = await service.SendPhotoAsync(buyer, conversation, Photo(SKColors.Teal), Ct);

        Assert.Null(sent.Error);
        var message = sent.Value!;
        Assert.Equal(("photo", ChatService.PhotoText), (message.Kind, message.Text));
        Assert.Matches($"^/api/v1/conversations/{conversation}/photos/[0-9a-f]{{32}}\\.webp$", message.Photo);
        var key = $"chat/{conversation}/{message.Photo![^37..]}";
        Assert.Equal("image/webp", media.Find(key)!.ContentType);
        Assert.Equal(ChatHub.MessageCreated, hub.LastMethod);
        var push = Assert.Single(center.Sent);
        Assert.Equal((seller, ChatService.PhotoText), (push.UserId, push.Body));
        var inbox = await service.InboxAsync(seller, null, null, null, Ct);
        Assert.Equal(ChatService.PhotoText, inbox.Value!.Items.Single().LastMessage.Text);
    }

    [Fact]
    public async Task SendPhoto_Rejected_NeverReachesStorage_AndABlockOrAStrangerCannotSend()
    {
        var (buyer, seller, conversation) = await ConversationAsync();
        var stranger = await MemberAsync();

        var rejected = await service.SendPhotoAsync(buyer, conversation, Photo(new SKColor(255, 0, 255)), Ct);
        var notMine = await service.SendPhotoAsync(stranger, conversation, Photo(SKColors.Teal), Ct);
        var empty = await service.SendPhotoAsync(buyer, conversation, null, Ct);
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = seller, BlockedId = buyer, CreatedAt = time.GetUtcNow().UtcDateTime }, Ct);
        var blocked = await service.SendPhotoAsync(buyer, conversation, Photo(SKColors.Teal), Ct);

        Assert.Equal("moderation.image_rejected", rejected.Error!.Code);
        Assert.Equal(ChatErrors.ConversationNotFound, notMine.Error);
        Assert.Equal("listings.photo_unsupported", empty.Error!.Code);
        Assert.Equal(ChatErrors.Blocked, blocked.Error);
        Assert.Equal(0, media.Count);
        Assert.Empty(center.Sent);
    }

    [Fact]
    public async Task APhoto_OnlyItsParticipantsGetIt_AndANameThatIsNotAPhotoIsNotFound()
    {
        var (buyer, seller, conversation) = await ConversationAsync();
        var stranger = await MemberAsync();
        var name = (await service.SendPhotoAsync(buyer, conversation, Photo(SKColors.Teal), Ct)).Value!.Photo![^37..];

        var mine = await service.PhotoAsync(buyer, conversation, name, Ct);
        var theirs = await service.PhotoAsync(seller, conversation, name, Ct);
        var other = await service.PhotoAsync(stranger, conversation, name, Ct);
        var traversal = await service.PhotoAsync(buyer, conversation, "../products/x.webp", Ct);
        var missing = await service.PhotoAsync(buyer, conversation, new string('a', 32) + ".webp", Ct);

        Assert.NotEmpty(mine.Value!);
        Assert.Equal(mine.Value, theirs.Value);
        Assert.Equal(ChatErrors.PhotoNotFound, other.Error);
        Assert.Equal(ChatErrors.PhotoNotFound, traversal.Error);
        Assert.Equal(ChatErrors.PhotoNotFound, missing.Error);
    }

    [Fact]
    public async Task Typing_ReachesOnlyTheOtherPerson_AndNotWithABlock()
    {
        var (buyer, seller, conversation) = await ConversationAsync();
        var stranger = await MemberAsync();

        Assert.True(await service.TypingAsync(buyer, conversation, Ct));
        Assert.Equal((seller, ChatHub.TypingEventName), (hub.LastUser, hub.LastMethod));
        Assert.Equal(new TypingEvent(conversation, buyer), hub.LastArgs![0]);
        Assert.False(await service.TypingAsync(stranger, conversation, Ct));
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = buyer, BlockedId = seller, CreatedAt = time.GetUtcNow().UtcDateTime }, Ct);
        Assert.False(await service.TypingAsync(seller, conversation, Ct));
    }

    [Fact]
    public async Task Reading_SavesWhen_TellsBothInRealTime_AndTheOtherSeesItUnlessBlocked()
    {
        var (buyer, seller, conversation) = await ConversationAsync();
        time.Advance(TimeSpan.FromMinutes(3));
        var readAt = time.GetUtcNow().UtcDateTime;

        await service.MarkReadAsync(seller, conversation, Ct);
        var event_ = hub.LastArgs![0];
        var forBuyer = (await service.GetAsync(buyer, conversation, Ct)).Value!;
        var forSeller = (await service.GetAsync(seller, conversation, Ct)).Value!;
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = seller, BlockedId = buyer, CreatedAt = readAt }, Ct);
        var blocked = (await service.GetAsync(buyer, conversation, Ct)).Value!;
        hub.Reset();
        await service.MarkReadAsync(seller, conversation, Ct);

        Assert.Equal(new ConversationReadEvent(conversation, seller, readAt), event_);
        Assert.Equal(readAt, forBuyer.CounterpartReadAt);
        Assert.Null(forSeller.CounterpartReadAt);
        Assert.Null(blocked.CounterpartReadAt);
        Assert.Null(hub.LastMethod);
    }

    [Fact]
    public async Task Start_OnAProductOfAnotherCampus_IsUnavailable()
    {
        var buyer = await MemberAsync();
        var product = Product(await MemberAsync(OtherCommunity), OtherCommunity);

        var result = await service.StartFromProductAsync(buyer, new StartConversationRequest(product.Id), Ct);

        Assert.Equal(ChatErrors.ProductUnavailable, result.Error);
    }

    [Fact]
    public async Task Start_OnAPausedOrRemovedProduct_IsUnavailable()
    {
        var buyer = await MemberAsync();
        var product = Product(await MemberAsync(), visible: false);

        var result = await service.StartFromProductAsync(buyer, new StartConversationRequest(product.Id), Ct);

        Assert.Equal(ChatErrors.ProductUnavailable, result.Error);
    }

    [Fact]
    public async Task Start_ByASuspendedAccount_IsForbidden()
    {
        var buyer = await MemberAsync(status: UserStatuses.Suspended);
        var product = Product(await MemberAsync());

        var result = await service.StartFromProductAsync(buyer, new StartConversationRequest(product.Id), Ct);

        Assert.Equal("auth.forbidden", result.Error?.Code);
    }

    [Fact]
    public async Task Get_AfterTheProductIsRemoved_KeepsTheSavedTitle_AndIsNotAvailable()
    {
        var buyer = await MemberAsync();
        var seller = await MemberAsync(name: "Valeria Cruz");
        var product = Product(seller);
        var started = (await service.StartFromProductAsync(buyer, new StartConversationRequest(product.Id), Ct)).Value!;
        products.Items.Remove(product.Id);

        var conversation = (await service.GetAsync(buyer, started.Conversation.Id, Ct)).Value!;

        Assert.Equal("Valeria Cruz", conversation.Counterpart.Name);
        Assert.Equal(new ConversationContextResponse("product", product.Id, "Calculadora Casio fx-991ES Plus", null, null, null, null, false), conversation.Context);
    }

    [Fact]
    public async Task Send_ByAParticipantWhoseAccountIsNoLongerActive_IsForbidden_AndNotSaved()
    {
        var buyer = await MemberAsync();
        var product = Product(await MemberAsync());
        var started = (await service.StartFromProductAsync(buyer, new StartConversationRequest(product.Id), Ct)).Value!;
        // HU-40 suspendera cuentas; hoy una cuenta deja de estar activa al borrarse (el token sigue vigente unos minutos).
        await users.DeleteAsync(buyer, Ct);

        var result = await service.SendAsync(buyer, started.Conversation.Id, new SendMessageRequest("Hola"), Ct);

        Assert.Equal("auth.forbidden", result.Error?.Code);
        Assert.Empty(await messages.FindPageAsync(started.Conversation.Id, null, 10, Ct));
    }

    [Fact]
    public async Task Send_WhenTheBackplaneIsDown_StillSavesTheMessage()
    {
        var buyer = await MemberAsync();
        var product = Product(await MemberAsync());
        var started = (await service.StartFromProductAsync(buyer, new StartConversationRequest(product.Id), Ct)).Value!;
        hub.Failure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, CommandFlags.None, "Redis no responde", null, CommandStatus.Unknown);

        var result = await service.SendAsync(buyer, started.Conversation.Id, new SendMessageRequest("¿Sigue disponible?"), Ct);

        Assert.Null(result.Error);
        Assert.Single(await messages.FindPageAsync(started.Conversation.Id, null, 10, Ct));
        Assert.Equal("¿Sigue disponible?", (await conversations.FindByIdAsync(started.Conversation.Id, Ct))!.LastMessage!.Text);
    }

    [Fact]
    public async Task Send_NotifiesBothParticipants_AndCopiesAShortPreview()
    {
        var buyer = await MemberAsync();
        var seller = await MemberAsync();
        var product = Product(seller);
        var started = (await service.StartFromProductAsync(buyer, new StartConversationRequest(product.Id), Ct)).Value!;

        await service.SendAsync(buyer, started.Conversation.Id, new SendMessageRequest(new string('a', 300)), Ct);

        Assert.Equal([buyer, seller], hub.LastUsers);
        Assert.Equal(ChatHub.MessageCreated, hub.LastMethod);
        Assert.Equal(ChatService.PreviewLength, (await conversations.FindByIdAsync(started.Conversation.Id, Ct))!.LastMessage!.Text.Length);
    }

    [Fact]
    public async Task Send_OnlyTheSellersReplyToTheBuyer_CountsForTheResponseTime()
    {
        // HU-64: "Responde en ~N" mide cuanto tarda quien vende en contestar a quien compra.
        var buyer = await MemberAsync();
        var seller = await MemberAsync(name: "Valeria Cruz");
        var started = (await service.StartFromProductAsync(buyer, new StartConversationRequest(Product(seller).Id), Ct)).Value!;
        var id = started.Conversation.Id;

        await service.SendAsync(seller, id, new SendMessageRequest("¡Hola!"), Ct);
        await service.SendAsync(buyer, id, new SendMessageRequest("¿Sigue disponible?"), Ct);
        time.Advance(TimeSpan.FromMinutes(42));
        await service.SendAsync(seller, id, new SendMessageRequest("Sí, aquí está"), Ct);
        await service.SendAsync(seller, id, new SendMessageRequest("¿Cuándo pasas?"), Ct);
        await service.SendAsync(buyer, id, new SendMessageRequest("Mañana"), Ct);

        var sample = Assert.Single((await profiles.FindAsync(seller, Ct))!.Responses);
        Assert.Equal(42, sample.Minutes);
        Assert.Null(await profiles.FindAsync(buyer, Ct));
    }

    [Fact]
    public async Task Start_WithABlockInEitherDirection_IsBlocked()
    {
        var buyer = await MemberAsync();
        var seller = await MemberAsync();
        var product = Product(seller);
        await BlockAsync(seller, buyer);

        var result = await service.StartFromProductAsync(buyer, new StartConversationRequest(product.Id), Ct);

        Assert.Equal(ChatErrors.Blocked, result.Error);
    }

    [Fact]
    public async Task Send_AfterABlock_IsBlocked_NotSaved_AndTheConversationCannotBeAnswered()
    {
        var buyer = await MemberAsync();
        var seller = await MemberAsync();
        var started = (await service.StartFromProductAsync(buyer, new StartConversationRequest(Product(seller).Id), Ct)).Value!;
        Assert.True(started.Conversation.CanReply);
        await BlockAsync(buyer, seller);

        var bySeller = await service.SendAsync(seller, started.Conversation.Id, new SendMessageRequest("Hola"), Ct);
        var byBuyer = await service.SendAsync(buyer, started.Conversation.Id, new SendMessageRequest("Hola"), Ct);
        var forSeller = (await service.GetAsync(seller, started.Conversation.Id, Ct)).Value!;

        Assert.Equal(ChatErrors.Blocked, bySeller.Error);
        Assert.Equal(ChatErrors.Blocked, byBuyer.Error);
        Assert.Empty(await messages.FindPageAsync(started.Conversation.Id, null, 10, Ct));
        Assert.False(forSeller.CanReply);
    }

    [Fact]
    public async Task Counterpart_CarriesTheProfilePhoto_InTheConversationAndTheInbox_OrNullWithoutOne()
    {
        var buyer = await MemberAsync();
        var seller = await MemberAsync(name: "Valeria Cruz");
        var started = (await service.StartFromProductAsync(buyer, new StartConversationRequest(Product(seller).Id), Ct)).Value!;
        await service.SendAsync(buyer, started.Conversation.Id, new SendMessageRequest("¿Sigue disponible?"), Ct);
        var key = ProfileService.NewAvatarKey(seller);
        await profiles.SetAvatarAsync(new ProfileOwner(seller, InMemoryCommunityRepository.UtttId, "Valeria Cruz"), key, DateTime.UtcNow, Ct);

        var conversation = (await service.GetAsync(buyer, started.Conversation.Id, Ct)).Value!;
        var inbox = (await service.InboxAsync(buyer, null, null, null, Ct)).Value!;
        var sellerInbox = (await service.InboxAsync(seller, null, null, null, Ct)).Value!;

        Assert.Equal("/" + key, conversation.Counterpart.Photo);
        Assert.Equal("/" + key, Assert.Single(inbox.Items).Counterpart.Photo);
        Assert.Null(Assert.Single(sellerInbox.Items).Counterpart.Photo);
    }

    [Theory]
    [InlineData("no-es-un-cursor")]
    [InlineData("6716a00000000000000000zz")]
    public async Task Messages_WithACursorThatIsNotAnId_IsRejected(string cursor)
    {
        var result = await service.MessagesAsync(await MemberAsync(), AuthIds.New(), cursor, null, Ct);

        Assert.Equal(ChatErrors.InvalidCursor, result.Error);
    }

    private Task<bool> BlockAsync(string blocker, string blocked) =>
        blocks.InsertIfAbsentAsync(
            new BlockDocument { Id = AuthIds.New(), CommunityId = InMemoryCommunityRepository.UtttId, BlockerId = blocker, BlockedId = blocked, BlockedName = "Bloqueada" },
            Ct);

    private sealed class FakeProducts : IProductDirectory
    {
        public Dictionary<string, ProductSummary> Items { get; } = new(StringComparer.Ordinal);

        public Task<ProductSummary?> FindAsync(string productId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(productId));
    }

    /// <summary>Hub falso: registra a quien se envia o falla como Redis caido.</summary>
    private sealed class FakeHub : IHubContext<ChatHub>, IHubClients, IClientProxy
    {
        public Exception? Failure { get; set; }

        public void Reset()
        {
            LastMethod = null;
            LastArgs = null;
            LastUser = null;
        }

        public IReadOnlyList<string>? LastUsers { get; private set; }

        public string? LastMethod { get; private set; }

        IHubClients IHubContext<ChatHub>.Clients => this;

        IGroupManager IHubContext<ChatHub>.Groups => throw new NotSupportedException();

        public IClientProxy All => throw new NotSupportedException();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Client(string connectionId) => throw new NotSupportedException();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

        public IClientProxy Group(string groupName) => throw new NotSupportedException();

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public string? LastUser { get; private set; }

        public object?[]? LastArgs { get; private set; }

        public IClientProxy User(string userId)
        {
            LastUser = userId;
            return this;
        }

        public IClientProxy Users(IReadOnlyList<string> userIds)
        {
            LastUsers = userIds;
            return this;
        }

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            LastMethod = method;
            LastArgs = args;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    [Fact]
    public async Task Send_PushesTheMessageToTheOtherParticipant_WithTheSendersFirstName()
    {
        var buyer = await MemberAsync(name: "Ana Sofía Ramírez");
        var seller = await MemberAsync(name: "Diego Hernández");
        var started = (await service.StartFromProductAsync(buyer, new StartConversationRequest(Product(seller).Id), Ct)).Value!;

        await service.SendAsync(buyer, started.Conversation.Id, new SendMessageRequest("¿Sigue disponible?"), Ct);

        var notice = center.Sent.Single();
        Assert.Equal((seller, "message", "Ana", "¿Sigue disponible?"), (notice.UserId, notice.Type, notice.Title, notice.Body));
        Assert.Equal(("conversation", started.Conversation.Id), (notice.TargetType, notice.TargetId));
        Assert.Null(notice.Category);
    }
}
