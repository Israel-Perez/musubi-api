using Musubi.Api.Modules.Admin;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.GenAi.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.Matching.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Notifications.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Auth;
using Musubi.TestShared;

namespace UnitTest.Admin;

/// <summary>
/// HU-42 (RF-57): los indicadores del apartado 19 de la propuesta, por area y en su orden. Cada modulo da sus conteos;
/// aqui se juntan, se cruzan (chat con pedidos, avisos con ventas) y se calculan los porcentajes. Solo el administrador
/// activo y solo su campus; sin base, un porcentaje es <c>null</c> (no 0).
/// </summary>
public sealed class UsageIndicatorsServiceTests
{
    private const string Campus = InMemoryCommunityRepository.UtttId;

    private static readonly DateTimeOffset Now = new(2026, 11, 25, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryUserRepository users = new();
    private readonly Fakes fakes = new();
    private readonly ManualTimeProvider time = new(Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheNineAreasOfTheProposal_InOrder_WithTheirValues()
    {
        var admin = await UserAsync(UserRoles.Admin);
        var luis = await UserAsync();
        fakes.Saved = [new NoticeSent(luis, "saved_search:s1:p1", Now.UtcDateTime), new NoticeSent(luis, "saved_search:s1:p2", Now.UtcDateTime), new NoticeSent(AuthIds.New(), "saved_search:s9:p1", Now.UtcDateTime)];
        fakes.Slow = [new NoticeSent(luis, "slow_listing:p1", Now.UtcDateTime.AddDays(-3)), new NoticeSent(luis, "slow_listing:p2", Now.UtcDateTime.AddDays(-2))];
        fakes.SoldAfter = 1;

        var result = await Service().IndicatorsAsync(admin, null, Ct);

        var page = result.Value!;
        Assert.Equal(("week", Now.UtcDateTime.AddDays(-7), Now.UtcDateTime), (page.Period, page.From, page.To));
        Assert.Equal(["adoption", "supply", "demand", "connection", "operations", "chat", "ai", "sellers", "trust"], page.Areas.Select(a => a.Key));
        var all = page.Areas.SelectMany(a => a.Items).ToDictionary(i => i.Key);
        Assert.Equal((12d, "6 validados · 2 nuevos en el periodo"), (all["users_registered"].Value!.Value, all["users_registered"].Detail));
        Assert.Equal(5d, all["users_active"].Value);
        Assert.Equal([new IndicatorBreakdown("Calculadoras", 3), new IndicatorBreakdown("Libros", 1)], all["products_created"].Breakdown);
        Assert.Equal([new IndicatorBreakdown("Calculadoras", 2), new IndicatorBreakdown("Otro", 2)], all["requests_created"].Breakdown);
        Assert.Equal((1.5, IndicatorUnits.Average), (all["offers_per_request"].Value!.Value, all["offers_per_request"].Unit));
        Assert.Equal((25d, IndicatorUnits.Percent), (all["direct_offers"].Value!.Value, all["direct_offers"].Unit));
        Assert.Equal(25d, all["request_conversion"].Value);
        Assert.Equal(1d, all["saved_searches_served"].Value);
        Assert.Equal((3d, "5 productos vendidos"), (all["orders_completed"].Value!.Value, all["orders_completed"].Detail));
        Assert.Equal((5.3, "Hasta la primera oferta; hasta aceptar una, 30 h"), (all["time_to_offer"].Value!.Value, all["time_to_offer"].Detail));
        Assert.Equal((4d, 50d), (all["conversations"].Value!.Value, all["chat_conversion"].Value!.Value));
        Assert.Equal((3d, 50d), (all["ai_summaries"].Value!.Value, all["ai_precision"].Value!.Value));
        Assert.Equal((2d, 1d), (all["slow_listings"].Value!.Value, all["sold_after_notice"].Value!.Value));
        Assert.Equal(["p1", "p2"], fakes.SoldAfterAsked!.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((4.7, IndicatorUnits.Stars), (all["rating_average"].Value!.Value, all["rating_average"].Unit));
        Assert.Equal((1d, "4 reportes en el periodo"), (all["reports_upheld"].Value!.Value, all["reports_upheld"].Detail));
        Assert.All(fakes.Campuses, c => Assert.Equal(Campus, c));
    }

    [Fact]
    public async Task WithoutData_APercentageIsEmpty_NotZero()
    {
        var admin = await UserAsync(UserRoles.Admin);
        fakes.Empty();

        var page = (await Service().IndicatorsAsync(admin, IndicatorPeriods.Month, Ct)).Value!;

        var all = page.Areas.SelectMany(a => a.Items).ToDictionary(i => i.Key);
        Assert.Equal(Now.UtcDateTime.AddDays(-30), page.From);
        foreach (var key in new[] { "offers_per_request", "direct_offers", "request_conversion", "chat_conversion", "ai_precision", "time_to_offer", "rating_average" })
        {
            Assert.Null(all[key].Value);
        }

        Assert.Equal("Hasta la primera oferta", all["time_to_offer"].Detail);
        Assert.Equal(0d, all["products_created"].Value);
    }

    [Fact]
    public async Task OnlyAnActiveAdmin_AndOnlyAKnownPeriod()
    {
        var student = await UserAsync();
        var suspended = await UserAsync(UserRoles.Admin, UserStatuses.Suspended);
        var admin = await UserAsync(UserRoles.Admin);

        Assert.Equal(403, (await Service().IndicatorsAsync(student, null, Ct)).Error?.Status);
        Assert.Equal(403, (await Service().IndicatorsAsync(suspended, null, Ct)).Error?.Status);
        Assert.Equal("admin.invalid_period", (await Service().IndicatorsAsync(admin, "year", Ct)).Error?.Code);
        Assert.Empty(fakes.Campuses);
    }

    [Fact]
    public void Percentages_AndRatios_RoundToOneDecimal()
    {
        Assert.Equal(33.3, UsageIndicatorsService.Percent(1, 3));
        Assert.Null(UsageIndicatorsService.Percent(1, 0));
        Assert.Equal(1.3, UsageIndicatorsService.Ratio(4, 3));
        Assert.Null(UsageIndicatorsService.Ratio(0, 0));
        Assert.Equal((7, 30, null), (IndicatorPeriods.Days(null), IndicatorPeriods.Days("month"), IndicatorPeriods.Days("Week")));
    }

    private UsageIndicatorsService Service() => new(
        new AdminGate(new AccountModeration(users, new InMemoryRefreshTokenRepository(), new NoRevocations(), time)),
        new UserIndicatorsWithCommunity(fakes, users),
        fakes,
        fakes,
        fakes,
        fakes,
        fakes,
        fakes,
        fakes,
        fakes,
        time);

    private async Task<string> UserAsync(string? role = null, string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        Assert.True(await users.TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = Campus,
                Email = $"u.{id}@uttt.edu.mx",
                EmailNormalized = $"u.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Persona de Prueba",
                Status = status,
                Roles = role is null ? [UserRoles.Student] : [UserRoles.Student, role],
                EmailVerifiedAt = Now.UtcDateTime,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    /// <summary>Nadie revoca nada en estas pruebas.</summary>
    private sealed class NoRevocations : IAccessTokenRevocations
    {
        public Task RevokeTokenAsync(string tokenId, DateTime expiresAt, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RevokeUserAsync(string userId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> IsRevokedAsync(string? tokenId, string? userId, long issuedAtMs, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    /// <summary>La adopcion es fija; quienes son del campus se resuelve con los usuarios de verdad.</summary>
    private sealed class UserIndicatorsWithCommunity(Fakes fakes, IUserRepository users) : IUserIndicators
    {
        public Task<Adoption> AdoptionAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) =>
            fakes.Adoption(communityId);

        public Task<IReadOnlyCollection<string>> OfCommunityAsync(string communityId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
            new UserIndicators(users, new InMemoryRefreshTokenRepository()).OfCommunityAsync(communityId, userIds, cancellationToken);
    }

    /// <summary>Lo que daria cada modulo.</summary>
    private sealed class Fakes : IListingIndicators, IWantedIndicators, IMatchIndicators, IOrderIndicators, IChatIndicators, IAiIndicators, INotificationIndicators, ITrustIndicators
    {
        private Adoption adoption = new(12, 6, 2, 5);
        private Supply supply = new(4, 9, new Dictionary<string, long> { ["calculadoras"] = 3, ["libros"] = 1 }, new Dictionary<string, string> { ["calculadoras"] = "Calculadoras", ["libros"] = "Libros" });
        private Demand demand = new(4, new Dictionary<string, long> { ["calculadoras"] = 2, ["otro"] = 2 }, 6, 8, 2, 5.25, 30);
        private MatchUsage matches = new(7, 2);
        private OrderUsage orders = new(3, 5, 1);
        private IReadOnlyList<string> conversations = ["c1", "c2", "c3", "c4"];
        private long withOrders = 2;
        private AiUsage ai = new(3, 2, 1);
        private Trust trust = new(new RatingUsage(6, 4.6666), new ReportUsage(4, 1));

        public List<string> Campuses { get; } = [];

        public IReadOnlyList<NoticeSent> Saved { get; set; } = [];

        public IReadOnlyList<NoticeSent> Slow { get; set; } = [];

        public long SoldAfter { get; set; }

        public IReadOnlyDictionary<string, DateTime>? SoldAfterAsked { get; private set; }

        public void Empty()
        {
            adoption = new(1, 1, 0, 0);
            supply = new(0, 0, new Dictionary<string, long>(), new Dictionary<string, string>());
            demand = new(0, new Dictionary<string, long>(), 0, 0, 0, null, null);
            matches = new(0, 0);
            orders = new(0, 0, 0);
            conversations = [];
            withOrders = 0;
            ai = new(0, 0, 0);
            trust = new(new RatingUsage(0, null), new ReportUsage(0, 0));
        }

        public Task<Adoption> Adoption(string communityId) => Seen(communityId, adoption);

        public Task<Supply> SupplyAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) => Seen(communityId, supply);

        public Task<Demand> DemandAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) => Seen(communityId, demand);

        public Task<MatchUsage> ConnectionAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) => Seen(communityId, matches);

        public Task<OrderUsage> OperationsAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) => Seen(communityId, orders);

        public Task<long> ConversationsWithOrdersAsync(IReadOnlyCollection<string> conversationIds, CancellationToken cancellationToken) => Task.FromResult(withOrders);

        public Task<long> SoldAfterAsync(IReadOnlyDictionary<string, DateTime> since, CancellationToken cancellationToken)
        {
            SoldAfterAsked = since;
            return Task.FromResult(SoldAfter);
        }

        public Task<IReadOnlyList<string>> StartedAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) => Seen(communityId, conversations);

        public Task<AiUsage> UsageAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) => Seen(communityId, ai);

        public Task<IReadOnlyList<NoticeSent>> SentAsync(string type, DateTime from, DateTime to, CancellationToken cancellationToken) =>
            Task.FromResult(type == NotificationTypes.SavedSearchMatch ? Saved : Slow);

        public Task<Trust> TrustAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) => Seen(communityId, trust);

        private Task<T> Seen<T>(string communityId, T value)
        {
            Campuses.Add(communityId);
            return Task.FromResult(value);
        }
    }
}
