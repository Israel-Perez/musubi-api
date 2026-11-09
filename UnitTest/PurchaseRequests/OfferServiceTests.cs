using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;
using Musubi.TestShared;

namespace UnitTest.PurchaseRequests;

/// <summary>
/// HU-16: vincular una publicacion propia disponible u ofrecer sin publicar; una por persona y solicitud, editable
/// (RN-14); nunca a una propia (RN-06); presupuesto rebasado se permite y se marca; moderacion (02-07); quien no
/// puede ver la solicitud recibe <c>wanted.not_found</c>.
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
    private readonly ManualTimeProvider time = new(Now);
    private readonly OfferService service;

    public OfferServiceTests()
    {
        service = new OfferService(
            offers,
            requests,
            new MemberDirectory(new InMemoryCommunityRepository(), users),
            new ProductDirectory(products),
            new ListingPhotos(media),
            new BlockDirectory(blocks),
            new InMemoryContentModerator(),
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
}
