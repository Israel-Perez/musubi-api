using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;
using Musubi.TestShared;
using UnitTest.Common;

namespace UnitTest.PurchaseRequests;

/// <summary>
/// HU-16: vincular una publicacion propia disponible u ofrecer sin publicar; una por persona y solicitud, editable
/// (RN-14); nunca a una propia (RN-06); presupuesto rebasado se permite y se marca; moderacion (02-07); quien no
/// puede ver la solicitud recibe <c>wanted.not_found</c>. HU-17: quien busca ve las ofertas, las rechaza, las
/// acepta (se abre la conversacion) o abre el chat.
/// </summary>
public sealed class OfferServiceTests
{
    private const string Uttt = InMemoryCommunityRepository.UtttId;
    private const string OtherCommunity = "000000000000000000000002";

    private static readonly DateTimeOffset Now = new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryWantedRepository requests = new();
    private readonly InMemoryOfferRepository offers = new();
    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryMediaStorage media = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly InMemoryProfileRepository profiles = new();
    private readonly InMemoryConversationRepository conversations = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly OfferService service;

    private readonly RecordingNotificationCenter center = new();
    public OfferServiceTests()
    {
        var members = new MemberDirectory(new InMemoryCommunityRepository(), users);
        service = new OfferService(
            offers,
            requests,
            members,
            new ProductDirectory(products),
            new ListingPhotos(media),
            new BlockDirectory(blocks),
            new ProfileDirectory(profiles, new InMemoryCareerRepository()),
            new ConversationOpener(conversations, members, time),
            new InMemoryContentModerator(),
            center,
            time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OfferRequest Direct(
        string? description = "Bata blanca de algodón, talla M. La usé dos semestres.",
        long? price = 28_000,
        string? condition = "used",
        string? photo = null,
        string? message = null) => new(null, description, price, condition, photo, message);

    private async Task<string> MemberAsync(string communityId = Uttt, string name = "Valeria Cruz")
    {
        var id = AuthIds.New();
        Assert.True(await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = communityId,
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

    private async Task<WantedRequestDocument> RequestAsync(string requester, string communityId = Uttt, DateTime? expiresAt = null)
    {
        var request = new WantedRequestDocument
        {
            Id = AuthIds.New(),
            CommunityId = communityId,
            RequesterId = requester,
            RequesterName = "Fernanda O.",
            Title = "Bata de laboratorio talla M",
            CategorySlug = "laboratorio",
            CategoryName = "Laboratorio",
            BudgetCents = 30_000,
            ExpiresAt = expiresAt ?? Now.UtcDateTime.AddDays(5),
            CreatedAt = Now.UtcDateTime.AddHours(-1),
        };
        await requests.InsertAsync(request, Ct).ConfigureAwait(false);
        return request;
    }

    private async Task<string> ProductAsync(string seller, string status = ProductStatuses.Active, string communityId = Uttt)
    {
        var id = AuthIds.New();
        await products.InsertAsync(
            new ProductDocument
            {
                Id = id,
                CommunityId = communityId,
                SellerId = seller,
                Seller = new ProductSeller { Name = "Valeria C." },
                Title = "Bata de laboratorio blanca, talla M",
                CategorySlug = "laboratorio",
                PriceCents = 25_000,
                Condition = ProductConditions.Used,
                ConditionDetail = "good",
                Stock = 1,
                Status = status,
                Photos = [$"media/products/{seller}/0123456789abcdef0123456789abcdef.webp"],
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false);
        return id;
    }

    [Fact]
    public async Task Linking_AnAvailableListingOfMine_CopiesItsPriceConditionAndPhoto_AndCountsTheOffer()
    {
        var fernanda = await MemberAsync(name: "Fernanda Ortiz");
        var me = await MemberAsync();
        var request = await RequestAsync(fernanda);
        var product = await ProductAsync(me);

        var (offer, created) = (await service.SaveAsync(me, Uttt, request.Id, new OfferRequest(product, null, null, null, null, "  ¡Hola! Te la llevo.  "), Ct)).Value;

        Assert.True(created);
        Assert.Equal(("listing", product, "Bata de laboratorio blanca, talla M", 25_000L, "used"), (offer.Kind, offer.ProductId, offer.Title, offer.PriceCents, offer.Condition));
        Assert.Equal($"/media/products/{me}/0123456789abcdef0123456789abcdef.webp", offer.Photo);
        Assert.Equal(("¡Hola! Te la llevo.", "sent", false), (offer.Message, offer.Status, offer.OverBudget));
        Assert.Equal(1, (await requests.FindByIdAsync(request.Id, Ct))!.OffersCount);
        Assert.Equal("Valeria C.", Assert.Single(offers.All()).SellerName);
    }

    [Fact]
    public async Task ADirectOffer_AboveTheBudget_IsAllowed_AndMarked()
    {
        var fernanda = await MemberAsync();
        var me = await MemberAsync();
        var request = await RequestAsync(fernanda);
        var photo = $"media/products/{me}/abcdefabcdefabcdefabcdefabcdefab.webp";
        await media.PutAsync(photo, [1, 2, 3], "image/webp", Ct);

        var offer = (await service.SaveAsync(me, Uttt, request.Id, Direct(price: 35_000, condition: "new", photo: photo), Ct)).Value.Offer;

        Assert.Equal(("direct", (string?)null, 35_000L, "new", true), (offer.Kind, offer.ProductId, offer.PriceCents, offer.Condition, offer.OverBudget));
        Assert.Equal("/" + photo, offer.Photo);
    }

    [Fact]
    public async Task OnePerRequest_TheSecondSendEditsIt_WithoutCountingAgain()
    {
        var fernanda = await MemberAsync();
        var me = await MemberAsync();
        var request = await RequestAsync(fernanda);
        var first = (await service.SaveAsync(me, Uttt, request.Id, Direct(), Ct)).Value.Offer;
        time.Advance(TimeSpan.FromMinutes(10));

        var (edited, created) = (await service.SaveAsync(me, Uttt, request.Id, Direct(description: "Bata talla M, como nueva", price: 25_000), Ct)).Value;
        var mine = (await service.MineAsync(me, Uttt, request.Id, Ct)).Value!;

        Assert.False(created);
        Assert.Equal(first.Id, edited.Id);
        Assert.Equal(("Bata talla M, como nueva", 25_000L), (mine.Title, mine.PriceCents));
        Assert.Equal((first.CreatedAt, Now.UtcDateTime.AddMinutes(10)), (mine.CreatedAt, mine.UpdatedAt));
        Assert.Equal(1, (await requests.FindByIdAsync(request.Id, Ct))!.OffersCount);
    }

    [Fact]
    public async Task Mine_WithoutAnOffer_IsOffersNotFound()
    {
        var fernanda = await MemberAsync();
        var me = await MemberAsync();
        var request = await RequestAsync(fernanda);

        Assert.Equal(OfferErrors.NotFound, (await service.MineAsync(me, Uttt, request.Id, Ct)).Error);
    }

    [Fact]
    public async Task MyOwnRequest_CannotBeAnswered()
    {
        var me = await MemberAsync();
        var request = await RequestAsync(me);

        Assert.Equal(OfferErrors.OwnRequest, (await service.SaveAsync(me, Uttt, request.Id, Direct(), Ct)).Error);
        Assert.Equal(OfferErrors.OwnRequest, (await service.MineAsync(me, Uttt, request.Id, Ct)).Error);
    }

    [Fact]
    public async Task ARequestOfAnotherCampus_Expired_Missing_OrWithABlock_IsNotFound()
    {
        var fernanda = await MemberAsync();
        var me = await MemberAsync();
        var blocked = await MemberAsync();
        var elsewhere = await RequestAsync(await MemberAsync(OtherCommunity), OtherCommunity);
        var expired = await RequestAsync(fernanda, expiresAt: Now.UtcDateTime.AddMinutes(-1));
        var open = await RequestAsync(fernanda);
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), CommunityId = Uttt, BlockerId = fernanda, BlockedId = blocked, BlockedName = "Diego" }, Ct);

        foreach (var (who, id) in new[] { (me, elsewhere.Id), (me, expired.Id), (me, AuthIds.New()), (me, "no-es-un-id"), (blocked, open.Id) })
        {
            Assert.Equal(WantedErrors.NotFound, (await service.SaveAsync(who, Uttt, id, Direct(), Ct)).Error);
        }

        Assert.Empty(offers.All());
    }

    [Fact]
    public async Task OnlyAnAvailableListingOfMineFromMyCampus_CanBeLinked()
    {
        var fernanda = await MemberAsync();
        var me = await MemberAsync();
        var someoneElse = await MemberAsync();
        var request = await RequestAsync(fernanda);

        foreach (var product in new[]
        {
            await ProductAsync(someoneElse),
            await ProductAsync(me, ProductStatuses.SoldOut),
            await ProductAsync(me, ProductStatuses.Hidden),
            await ProductAsync(me, communityId: OtherCommunity),
            AuthIds.New(),
        })
        {
            Assert.Equal(
                OfferErrors.ProductUnavailable,
                (await service.SaveAsync(me, Uttt, request.Id, new OfferRequest(product, null, null, null, null, null), Ct)).Error);
        }
    }

    public static TheoryData<OfferRequest, string> InvalidDirect() => new()
    {
        { Direct(description: null), "offers.invalid_description" },
        { Direct(description: "ab"), "offers.invalid_description" },
        { Direct(description: new string('a', 301)), "offers.invalid_description" },
        { Direct(price: null), "offers.invalid_price" },
        { Direct(price: 99), "offers.invalid_price" },
        { Direct(price: 100_000_000), "offers.invalid_price" },
        { Direct(condition: null), "offers.invalid_condition" },
        { Direct(condition: "roto"), "offers.invalid_condition" },
        { Direct(photo: "media/products/6716a0000000000000000001/0123456789abcdef0123456789abcdef.webp"), "offers.invalid_photo" },
        { Direct(photo: "../../etc/passwd"), "offers.invalid_photo" },
        { Direct(message: new string('a', 301)), "offers.invalid_message" },
    };

    [Theory]
    [MemberData(nameof(InvalidDirect))]
    public async Task InvalidFields_AreRejected_WithTheirCode(OfferRequest body, string code)
    {
        var fernanda = await MemberAsync();
        var me = await MemberAsync();
        var request = await RequestAsync(fernanda);

        Assert.Equal(code, (await service.SaveAsync(me, Uttt, request.Id, body, Ct)).Error?.Code);
        Assert.Empty(offers.All());
    }

    [Fact]
    public async Task ThePhotoOfSomeoneElse_IsRejected_EvenIfItExists()
    {
        var fernanda = await MemberAsync();
        var me = await MemberAsync();
        var request = await RequestAsync(fernanda);
        var theirs = $"media/products/{fernanda}/abcdefabcdefabcdefabcdefabcdefab.webp";
        await media.PutAsync(theirs, [1], "image/webp", Ct);

        Assert.Equal(OfferErrors.InvalidPhoto, (await service.SaveAsync(me, Uttt, request.Id, Direct(photo: theirs), Ct)).Error);
    }

    [Theory]
    [InlineData("Bata [[inapropiado]]", null, "la descripción")]
    [InlineData("Bata blanca de algodón", "Hola [[inapropiado]]", "el mensaje")]
    public async Task RejectedText_IsNotSaved_AndNamesTheField(string description, string? message, string field)
    {
        var fernanda = await MemberAsync();
        var me = await MemberAsync();
        var request = await RequestAsync(fernanda);

        var result = await service.SaveAsync(me, Uttt, request.Id, Direct(description: description, message: message), Ct);

        Assert.Equal("moderation.text_rejected", result.Error?.Code);
        Assert.Contains(field, result.Error!.Message, StringComparison.Ordinal);
        Assert.Empty(offers.All());
    }

    /// <summary>Fernanda busca; Valeria y Diego le ofrecen (Diego vincula una publicacion "como nueva").</summary>
    private async Task<(string Fernanda, WantedRequestDocument Request, string Valeria, OfferResponse ValeriaOffer, string Diego, OfferResponse DiegoOffer)> OffersAsync()
    {
        var fernanda = await MemberAsync(name: "Fernanda Ortiz").ConfigureAwait(false);
        var valeria = await MemberAsync(name: "Valeria Cruz").ConfigureAwait(false);
        var diego = await MemberAsync(name: "Diego Hernández").ConfigureAwait(false);
        var request = await RequestAsync(fernanda).ConfigureAwait(false);
        var valeriaOffer = (await service.SaveAsync(valeria, Uttt, request.Id, Direct(price: 28_000, message: "Te la llevo."), Ct).ConfigureAwait(false)).Value.Offer;
        var product = await ProductAsync(diego).ConfigureAwait(false);
        var diegoOffer = (await service.SaveAsync(diego, Uttt, request.Id, new OfferRequest(product, null, null, null, null, null), Ct).ConfigureAwait(false)).Value.Offer;
        return (fernanda, request, valeria, valeriaOffer, diego, diegoOffer);
    }

    [Fact]
    public async Task Received_ListsTheOffersOfMyRequest_WithWhoOffers_NewestFirst()
    {
        var (fernanda, request, valeria, valeriaOffer, diego, diegoOffer) = await OffersAsync();
        await profiles.SetDetailsAsync(new ProfileOwner(diego, Uttt, "Diego Hernández"), "ing-software", 9, Now.UtcDateTime, Ct);

        var items = (await service.ReceivedAsync(fernanda, request.Id, Ct)).Value!.Items;

        Assert.Equal([diegoOffer.Id, valeriaOffer.Id], items.Select(o => o.Id));
        var first = items[0];
        Assert.Equal(("listing", "used", "sent"), (first.Kind, first.Condition, first.Status));
        Assert.Equal(new OffererResponse(diego, "Diego Hernández", null, "Ing. Software", null, 0), first.Offerer);
        Assert.Equal(("Valeria Cruz", "Te la llevo.", 28_000L), (items[1].Offerer.Name, items[1].Message, items[1].PriceCents));
        Assert.Equal(valeria, items[1].Offerer.Id);
    }

    [Fact]
    public async Task Received_IsOnlyForTheOwner_AndHidesRejectedAndBlocked()
    {
        var (fernanda, request, valeria, valeriaOffer, diego, diegoOffer) = await OffersAsync();
        Assert.True((await service.RejectAsync(fernanda, request.Id, valeriaOffer.Id, Ct)).Value);
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), CommunityId = Uttt, BlockerId = diego, BlockedId = fernanda, BlockedName = "Fernanda" }, Ct);

        Assert.Empty((await service.ReceivedAsync(fernanda, request.Id, Ct)).Value!.Items);
        Assert.Equal(WantedErrors.NotFound, (await service.ReceivedAsync(valeria, request.Id, Ct)).Error);
        Assert.Equal(WantedErrors.NotFound, (await service.ReceivedAsync(fernanda, "no-es-un-id", Ct)).Error);
        Assert.Equal(OfferErrors.OfferNotFound, (await service.ChatAsync(fernanda, request.Id, diegoOffer.Id, Ct)).Error);
    }

    [Fact]
    public async Task Reject_OnlyOnce_AndTheOffererCanNoLongerEditIt()
    {
        var (fernanda, request, valeria, valeriaOffer, _, _) = await OffersAsync();

        Assert.True((await service.RejectAsync(fernanda, request.Id, valeriaOffer.Id, Ct)).Value);

        Assert.Equal(OfferErrors.NotPending, (await service.RejectAsync(fernanda, request.Id, valeriaOffer.Id, Ct)).Error);
        Assert.Equal(OfferErrors.NotPending, (await service.AcceptAsync(fernanda, request.Id, valeriaOffer.Id, Ct)).Error);
        Assert.Equal(OfferErrors.NotEditable, (await service.SaveAsync(valeria, Uttt, request.Id, Direct(), Ct)).Error);
        Assert.Equal("rejected", (await service.MineAsync(valeria, Uttt, request.Id, Ct)).Value!.Status);
    }

    [Fact]
    public async Task Accept_OpensTheConversation_BuyerIsWhoAsks_AndItIsIdempotent()
    {
        var (fernanda, request, _, _, diego, diegoOffer) = await OffersAsync();

        var first = (await service.AcceptAsync(fernanda, request.Id, diegoOffer.Id, Ct)).Value!;
        var again = (await service.AcceptAsync(fernanda, request.Id, diegoOffer.Id, Ct)).Value!;
        var chat = (await service.ChatAsync(fernanda, request.Id, diegoOffer.Id, Ct)).Value!;
        var conversation = (await conversations.FindByIdAsync(first.ConversationId, Ct))!;

        Assert.Equal(first.ConversationId, again.ConversationId);
        Assert.Equal(first.ConversationId, chat.ConversationId);
        Assert.Equal((fernanda, diego), (conversation.BuyerId, conversation.SellerId));
        Assert.Equal(("offer", diegoOffer.Id, "Bata de laboratorio blanca, talla M"), (conversation.Context.Type, conversation.Context.Id, conversation.Context.Title));
        Assert.Equal(diegoOffer.Photo, conversation.Context.Photo);
        Assert.Equal(("Fernanda Ortiz", "Diego Hernández"), (conversation.BuyerName, conversation.SellerName));
        Assert.Equal("accepted", (await service.ReceivedAsync(fernanda, request.Id, Ct)).Value!.Items.Single(o => o.Id == diegoOffer.Id).Status);
        Assert.Equal(OfferErrors.NotEditable, (await service.SaveAsync(diego, Uttt, request.Id, Direct(), Ct)).Error);
    }

    [Fact]
    public async Task Chat_DoesNotDecide_AndAnOfferOfAnotherRequest_IsNotFound()
    {
        var (fernanda, request, valeria, valeriaOffer, _, _) = await OffersAsync();
        var other = await RequestAsync(fernanda);

        var chat = (await service.ChatAsync(fernanda, request.Id, valeriaOffer.Id, Ct)).Value!;

        Assert.False(string.IsNullOrEmpty(chat.ConversationId));
        Assert.Equal("sent", (await service.MineAsync(valeria, Uttt, request.Id, Ct)).Value!.Status);
        Assert.Equal(OfferErrors.OfferNotFound, (await service.AcceptAsync(fernanda, other.Id, valeriaOffer.Id, Ct)).Error);
        Assert.Equal(WantedErrors.NotFound, (await service.AcceptAsync(valeria, request.Id, valeriaOffer.Id, Ct)).Error);
    }

    [Fact]
    public async Task Accept_OnAnExpiredRequest_IsRequestClosed_ButTheChatStillOpens()
    {
        var (fernanda, request, _, valeriaOffer, _, _) = await OffersAsync();
        time.Advance(TimeSpan.FromDays(6));

        Assert.Equal(OfferErrors.RequestClosed, (await service.AcceptAsync(fernanda, request.Id, valeriaOffer.Id, Ct)).Error);
        Assert.Null((await service.ChatAsync(fernanda, request.Id, valeriaOffer.Id, Ct)).Error);
    }

    [Fact]
    public async Task ALinkedListing_KeepsItsConditionDetail()
    {
        var (fernanda, request, _, _, _, diegoOffer) = await OffersAsync();

        var item = (await service.ReceivedAsync(fernanda, request.Id, Ct)).Value!.Items.Single(o => o.Id == diegoOffer.Id);

        Assert.Equal("good", item.ConditionDetail);
    }

    [Fact]
    public async Task Offers_NotifyWhoAsks_AndTheDecision_NotifiesWhoOffered()
    {
        var (fernanda, request, valeria, valeriaOffer, diego, diegoOffer) = await OffersAsync();

        var received = center.Sent.Where(n => n.Type == "offer_received").ToList();
        Assert.Equal(2, received.Count);
        Assert.All(received, n => Assert.Equal((fernanda, "requests", "wanted", request.Id), (n.UserId, n.Category, n.TargetType, n.TargetId)));
        Assert.Contains(received, n => n.Body.StartsWith("Valeria C. respondió \"Yo tengo este producto\"", StringComparison.Ordinal) && n.Body.EndsWith("· $280.", StringComparison.Ordinal));

        await service.SaveAsync(valeria, Uttt, request.Id, Direct(price: 27_000), Ct);
        Assert.Equal(2, center.Sent.Count(n => n.Type == "offer_received"));

        var chat = (await service.AcceptAsync(fernanda, request.Id, diegoOffer.Id, Ct)).Value!;
        await service.AcceptAsync(fernanda, request.Id, diegoOffer.Id, Ct);
        await service.RejectAsync(fernanda, request.Id, valeriaOffer.Id, Ct);

        var accepted = center.Sent.Single(n => n.Type == "offer_accepted");
        Assert.Equal((diego, "Fernanda aceptó tu oferta", "conversation", chat.ConversationId), (accepted.UserId, accepted.Title, accepted.TargetType, accepted.TargetId));
        var rejected = center.Sent.Single(n => n.Type == "offer_rejected");
        Assert.Equal((valeria, "Eligieron otra oferta", "wanted"), (rejected.UserId, rejected.Title, rejected.TargetType));
    }

    [Theory]
    [InlineData(50_000L, "$500")]
    [InlineData(165_000L, "$1,650")]
    [InlineData(12_350L, "$123.50")]
    public void Pesos_DoNotDependOnTheServerCulture(long cents, string expected) => Assert.Equal(expected, OfferService.Pesos(cents));
}
