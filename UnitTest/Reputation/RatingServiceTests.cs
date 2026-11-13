using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Moderation;
using Musubi.TestShared;

namespace UnitTest.Reputation;

/// <summary>
/// HU-27 y HU-45: calificar un pedido completado (una vez por parte, RN-24; de 1 a 5, RN-25; hasta 7 dias despues),
/// con los chips de cada parte, y la reputacion como un solo promedio con "A tiempo" (decisiones del 12-Nov-2026).
/// </summary>
public sealed class RatingServiceTests
{
    private const string Uttt = InMemoryCommunityRepository.UtttId;

    private static readonly DateTimeOffset Now = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryOrderRepository orders = new();
    private readonly InMemoryRatingRepository ratings = new();
    private readonly InMemoryProfileRepository profiles = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly RatingService service;

    public RatingServiceTests() => service = new RatingService(
        new OrderRatingGate(orders),
        ratings,
        new ProfileRatings(profiles),
        new MemberDirectory(new InMemoryCommunityRepository(), users),
        new BlockDirectory(blocks),
        new InMemoryContentModerator(),
        time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RateOrderRequest Rate(int? stars = 5, string[]? tags = null, string? comment = null) =>
        new(stars, tags ?? [], comment);

    private async Task<string> MemberAsync(string name, string status = UserStatuses.Active, string community = Uttt)
    {
        var id = AuthIds.New();
        Assert.True(await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = community,
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

    // Un pedido de Ana a Diego, completado hace una hora (o en el estado pedido).
    private async Task<OrderDocument> OrderAsync(string buyer, string seller, string status = OrderStatuses.Completed)
    {
        var at = Now.UtcDateTime.AddHours(-1);
        var order = new OrderDocument
        {
            Id = AuthIds.New(),
            CommunityId = Uttt,
            Number = 1001,
            BuyerId = buyer,
            SellerId = seller,
            BuyerName = "Ana Sofía Ramírez",
            SellerName = "Diego Hernández",
            ConversationId = AuthIds.New(),
            Item = new OrderItem { SourceId = AuthIds.New(), Title = "Calculadora", PriceCents = 50_000, Quantity = 1, TotalCents = 50_000 },
            Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", At = at },
            Status = status,
            History = [new OrderTransition { Status = OrderStatuses.Pending, At = at.AddDays(-1) }, new OrderTransition { Status = status, At = at }],
            CreatedAt = at.AddDays(-1),
            UpdatedAt = at,
        };
        Assert.True(await orders.TryInsertAsync(order, Ct).ConfigureAwait(false));
        return order;
    }

    private async Task<(string Ana, string Diego, OrderDocument Order)> CompletedAsync()
    {
        var ana = await MemberAsync("Ana Sofía Ramírez").ConfigureAwait(false);
        var diego = await MemberAsync("Diego Hernández").ConfigureAwait(false);
        return (ana, diego, await OrderAsync(ana, diego).ConfigureAwait(false));
    }

    [Fact]
    public async Task TheBuyer_RatesTheSeller_AndItCountsInTheReputation()
    {
        var (ana, diego, order) = await CompletedAsync();

        var result = await service.RateAsync(
            ana,
            order.Id,
            Rate(5, [RatingTags.AsDescribed, RatingTags.OnTime], "  Muy puntual en la biblioteca.  "),
            Ct);
        var stats = (await profiles.FindAsync(diego, Ct))!.Stats;
        var saved = (await ratings.FindByRatedAsync(diego, null, 10, Ct)).Single();
        var rated = await orders.FindByIdAsync(order.Id, Ct);

        Assert.Null(result.Error);
        Assert.Equal((5, "Muy puntual en la biblioteca."), (result.Value!.Stars, result.Value.Comment));
        // En el orden de la lista de la maqueta, no en el que llegaron.
        Assert.Equal([RatingTags.OnTime, RatingTags.AsDescribed], result.Value.Tags);
        Assert.Equal((1, 5.0, 1), (stats.RatingCount, stats.RatingAvg, stats.OnTimeCount));
        Assert.Equal(100, ProfileService.OnTimePercent(stats));
        Assert.Equal(("Ana R.", RatedRoles.Seller, ana), (saved.RaterName, saved.RatedRole, saved.RaterId));
        Assert.True(OrderRatingWindow.Rated(rated!, OrderRoles.Buyer));
        Assert.False(OrderRatingWindow.Rated(rated!, OrderRoles.Seller));
    }

    [Fact]
    public async Task TheSeller_RatesTheBuyer_OnlyWithTheBuyerTags()
    {
        var (ana, diego, order) = await CompletedAsync();

        var wrong = await service.RateAsync(diego, order.Id, Rate(4, [RatingTags.AsDescribed]), Ct);
        var ok = await service.RateAsync(diego, order.Id, Rate(4, [RatingTags.PaidAsAgreed, RatingTags.Friendly]), Ct);
        var stats = (await profiles.FindAsync(ana, Ct))!.Stats;

        Assert.Equal(ReputationErrors.InvalidTags, wrong.Error);
        Assert.Equal([RatingTags.Friendly, RatingTags.PaidAsAgreed], ok.Value!.Tags);
        Assert.Equal((1, 4.0, 0), (stats.RatingCount, stats.RatingAvg, stats.OnTimeCount));
        Assert.Equal(0, ProfileService.OnTimePercent(stats));
    }

    [Fact]
    public async Task OncePerOrder_PerSide()
    {
        var (ana, diego, order) = await CompletedAsync();

        var first = await service.RateAsync(ana, order.Id, Rate(5), Ct);
        var twice = await service.RateAsync(ana, order.Id, Rate(1), Ct);
        var otherSide = await service.RateAsync(diego, order.Id, Rate(5), Ct);

        Assert.Null(first.Error);
        Assert.Equal(ReputationErrors.AlreadyRated, twice.Error);
        Assert.Null(otherSide.Error);
        Assert.Equal(5.0, (await profiles.FindAsync(diego, Ct))!.Stats.RatingAvg);
    }

    [Theory]
    [InlineData(OrderStatuses.Pending)]
    [InlineData(OrderStatuses.Confirmed)]
    [InlineData(OrderStatuses.Cancelled)]
    public async Task OnlyACompletedOrder_CanBeRated(string status)
    {
        var ana = await MemberAsync("Ana Sofía Ramírez");
        var order = await OrderAsync(ana, await MemberAsync("Diego Hernández"), status);

        Assert.Equal(ReputationErrors.NotCompleted, (await service.RateAsync(ana, order.Id, Rate(), Ct)).Error);
    }

    [Fact]
    public async Task AfterSevenDays_ItCanNoLongerBeRated()
    {
        var (ana, diego, order) = await CompletedAsync();

        time.Advance(TimeSpan.FromDays(7) - TimeSpan.FromHours(1) - TimeSpan.FromMinutes(1));
        var inTime = await service.RateAsync(diego, order.Id, Rate(), Ct);
        time.Advance(TimeSpan.FromMinutes(2));
        var late = await service.RateAsync(ana, order.Id, Rate(), Ct);

        Assert.Null(inTime.Error);
        Assert.Equal(ReputationErrors.Expired, late.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(null)]
    public async Task StarsOutOfOneToFive_AreRejected_AndNothingChanges(int? stars)
    {
        var (ana, diego, order) = await CompletedAsync();

        var result = await service.RateAsync(ana, order.Id, Rate(stars), Ct);

        Assert.Equal(ReputationErrors.InvalidStars, result.Error);
        Assert.Null(await profiles.FindAsync(diego, Ct));
        Assert.False(OrderRatingWindow.Rated((await orders.FindByIdAsync(order.Id, Ct))!, OrderRoles.Buyer));
    }

    [Fact]
    public async Task TheComment_HasALimit_AndIsModerated()
    {
        var (ana, _, order) = await CompletedAsync();

        var tooLong = await service.RateAsync(ana, order.Id, Rate(comment: new string('a', RatingService.MaxComment + 1)), Ct);
        var offensive = await service.RateAsync(ana, order.Id, Rate(comment: "Todo bien [[inapropiado]]"), Ct);
        var blank = await service.RateAsync(ana, order.Id, Rate(comment: "   "), Ct);

        Assert.Equal(ReputationErrors.InvalidComment, tooLong.Error);
        Assert.Equal("moderation.text_rejected", offensive.Error?.Code);
        Assert.Null(blank.Value!.Comment);
    }

    [Fact]
    public async Task SomeoneElse_GetsNotFound_AndASuspendedAccountIsForbidden()
    {
        var (_, _, order) = await CompletedAsync();
        var outsider = await MemberAsync("Luis Martínez");
        var suspended = await MemberAsync("Sofía López", UserStatuses.Suspended);
        var hers = await OrderAsync(suspended, await MemberAsync("Diego Hernández"));

        Assert.Equal(OrdersErrors.NotFound, (await service.RateAsync(outsider, order.Id, Rate(), Ct)).Error);
        Assert.Equal(OrdersErrors.NotFound, (await service.RateAsync(outsider, "no-es-un-id", Rate(), Ct)).Error);
        Assert.Equal(ApiErrors.Forbidden, (await service.RateAsync(suspended, hers.Id, Rate(), Ct)).Error);
    }

    [Fact]
    public async Task TheReputation_IsASingleAverage_AsSellerAndAsBuyer()
    {
        // Decision de la usuaria: un solo promedio con lo recibido como quien vende y como quien compra.
        var diego = await MemberAsync("Diego Hernández");
        var ana = await MemberAsync("Ana Sofía Ramírez");
        var luis = await MemberAsync("Luis Martínez");
        var sold = await OrderAsync(ana, diego);
        var bought = await OrderAsync(diego, luis);

        await service.RateAsync(ana, sold.Id, Rate(5, [RatingTags.OnTime]), Ct);
        await service.RateAsync(luis, bought.Id, Rate(4, [RatingTags.Friendly]), Ct);
        var stats = (await profiles.FindAsync(diego, Ct))!.Stats;

        Assert.Equal((2, 4.5), (stats.RatingCount, stats.RatingAvg));
        Assert.Equal(50, ProfileService.OnTimePercent(stats));
    }

    [Fact]
    public async Task Opinions_NewestFirst_WithACursor_AndTheRaterShortName()
    {
        var diego = await MemberAsync("Diego Hernández");
        var viewer = await MemberAsync("Valeria Cruz");
        var names = new[] { "Ana Sofía Ramírez", "Luis Martínez", "Fernanda Ortiz" };
        foreach (var name in names)
        {
            var buyer = await MemberAsync(name);
            await service.RateAsync(buyer, (await OrderAsync(buyer, diego)).Id, Rate(5, [], $"Comentario de {name}"), Ct);
        }

        var first = (await service.ListAsync(viewer, Uttt, diego, null, 2, Ct)).Value!;
        var second = (await service.ListAsync(viewer, Uttt, diego, first.NextCursor, 2, Ct)).Value!;

        Assert.Equal(["Fernanda O.", "Luis M."], first.Items.Select(r => r.RaterName));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(["Ana R."], second.Items.Select(r => r.RaterName));
        Assert.Null(second.NextCursor);
        Assert.All(first.Items, r => Assert.Equal(RatedRoles.Seller, r.Role));
    }

    [Fact]
    public async Task Opinions_FollowTheProfileRules_CampusBlocksAndCursor()
    {
        var diego = await MemberAsync("Diego Hernández");
        var viewer = await MemberAsync("Valeria Cruz");
        var other = await MemberAsync("Otro Campus", community: AuthIds.New());
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = diego, BlockedId = viewer, CreatedAt = Now.UtcDateTime }, Ct);

        Assert.Equal(ProfilesErrors.NotFound, (await service.ListAsync(viewer, Uttt, diego, null, null, Ct)).Error);
        Assert.Equal(ProfilesErrors.NotFound, (await service.ListAsync(viewer, Uttt, other, null, null, Ct)).Error);
        Assert.Equal(ProfilesErrors.NotFound, (await service.ListAsync(viewer, Uttt, "no-es-un-id", null, null, Ct)).Error);
        Assert.Equal(ReputationErrors.InvalidCursor, (await service.ListAsync(viewer, Uttt, diego, "{\"$gt\":1}", null, Ct)).Error);
    }
}
