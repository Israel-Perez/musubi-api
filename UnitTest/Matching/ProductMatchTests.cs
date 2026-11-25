using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.Matching.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Notifications.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared;
using UnitTest.Common;

namespace UnitTest.Matching;

/// <summary>
/// HU-29 (decision de la usuaria, 14-Nov-2026): al publicar algo compatible con una solicitud activa, a quien la publico
/// le llega "Alguien publicó lo que buscas", que abre la publicacion. Las mismas reglas de HU-19 (RN-28), uno por
/// solicitud y publicacion (RN-29), hasta 3 al dia (RN-30) y nunca a quien vende ni entre personas bloqueadas (RN-31,
/// HU-57). Cada coincidencia se registra (RF-25).
/// </summary>
public sealed class ProductMatchTests
{
    private const string Campus = "6716a0000000000000000c01";

    // Viernes 13 de noviembre de 2026, 12:00 en el campus (18:00 UTC).
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryWantedRepository requests = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly InMemoryMatchRepository matches = new();
    private readonly InMemorySavedSearchRepository savedSearches = new();
    private readonly RecordingNotificationCenter center = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly ProductMatchNotifier notifier;
    private readonly string diego = AuthIds.New();

    public ProductMatchTests() => notifier = Notifier(center, time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly SemanticMatcher semantic = Semantic.Off();

    private ProductMatchNotifier Notifier(INotificationCenter notifications, TimeProvider clock) => new(
        new WantedMatchDirectory(requests, clock),
        new SavedSearchService(savedSearches, new MemberDirectory(new InMemoryCommunityRepository(), new InMemoryUserRepository()), clock),
        new BlockDirectory(blocks),
        notifications,
        new MatchLedger(matches, clock, NullLogger<MatchLedger>.Instance),
        semantic,
        clock);

    private ProductPublished Product(
        string title = "Bata blanca de laboratorio, talla M",
        string category = "laboratorio",
        long price = 28_000,
        string? id = null) =>
        new(id ?? AuthIds.New(), Campus, diego, title, category, price, SearchWord.Words(title), ProductConditions.Used, "Diego Hernández");

    private async Task<string> WantedAsync(
        string requester,
        string title = "Bata de laboratorio talla M",
        string category = "laboratorio",
        long budget = 30_000,
        string status = WantedStatuses.Active,
        TimeSpan? expiresIn = null,
        string community = Campus)
    {
        var id = AuthIds.New();
        await requests.InsertAsync(
            new WantedRequestDocument
            {
                Id = id,
                CommunityId = community,
                RequesterId = requester,
                RequesterName = "Ana R.",
                Title = title,
                CategorySlug = category,
                CategoryName = category,
                BudgetCents = budget,
                ExpiresAt = Now.UtcDateTime + (expiresIn ?? TimeSpan.FromDays(7)),
                Status = status,
                CreatedAt = Now.UtcDateTime.AddDays(-1),
            },
            Ct).ConfigureAwait(false);
        return id;
    }

    [Fact]
    public async Task ACompatibleRequest_TellsWhoLooksForIt_AndOpensTheListing()
    {
        var ana = AuthIds.New();
        var request = await WantedAsync(ana);
        var product = Product();

        var notified = await notifier.NotifyAsync(product, Ct);

        Assert.Equal(1, notified);
        Assert.Equal(
            new Notice(
                ana,
                NotificationTypes.ProductMatch,
                "Alguien publicó lo que buscas",
                "«Bata blanca de laboratorio, talla M» · $280 coincide con tu solicitud de Bata de laboratorio talla M.",
                NotificationTargets.Product,
                product.Id,
                NotificationCategories.Matches,
                $"product_match:{request}:{product.Id}"),
            Assert.Single(center.Sent));
        var match = Assert.Single(await matches.FindByRequestAsync(request, Ct));
        Assert.Equal((product.Id, diego, true, false), (match.ProductId, match.SellerId, match.NotifiedRequester, match.NotifiedSeller));
    }

    [Theory]
    [InlineData("laboratorio", 30_000, 36_000, true)] // el precio cabe con el 20 % de mas
    [InlineData("laboratorio", 30_000, 36_001, false)] // pasa el 20 %
    [InlineData("ropa", 30_000, 28_000, false)] // otra categoria
    [InlineData("otro", 30_000, 28_000, true)] // "otro": cualquier categoria
    public async Task TheRules_AreTheSameAsHu19(string requestCategory, long budget, long price, bool expected)
    {
        await WantedAsync(AuthIds.New(), category: requestCategory, budget: budget);

        var notified = await notifier.NotifyAsync(Product(price: price), Ct);

        Assert.Equal(expected ? 1 : 0, notified);
    }

    [Fact]
    public async Task FewWordsInCommon_ClosedExpiredOtherCampusOrOwn_TellNobody()
    {
        await WantedAsync(AuthIds.New(), title: "Bata de algodón grande con bolsas");
        await WantedAsync(AuthIds.New(), status: WantedStatuses.Closed);
        await WantedAsync(AuthIds.New(), expiresIn: TimeSpan.FromHours(-1));
        await WantedAsync(AuthIds.New(), community: "6716a0000000000000000c02");
        await WantedAsync(diego);

        var notified = await notifier.NotifyAsync(Product(), Ct);

        Assert.Equal(0, notified);
        Assert.Empty(center.Sent);
    }

    [Fact]
    public async Task ABlockInEitherDirection_TellsNobody()
    {
        var ana = AuthIds.New();
        var luis = AuthIds.New();
        await WantedAsync(ana);
        await WantedAsync(luis);
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = ana, BlockedId = diego, CreatedAt = Now.UtcDateTime }, Ct);
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = diego, BlockedId = luis, CreatedAt = Now.UtcDateTime }, Ct);

        Assert.Equal(0, await notifier.NotifyAsync(Product(), Ct));
    }

    [Fact]
    public async Task TwoRequestsOfTheSamePerson_GiveOneNotice()
    {
        var ana = AuthIds.New();
        await WantedAsync(ana, title: "Bata de laboratorio");
        await WantedAsync(ana, title: "Bata de laboratorio talla M");

        Assert.Equal(1, await notifier.NotifyAsync(Product(), Ct));
    }

    [Fact]
    public async Task UpToThreeADay_TheRestAreStillRecorded()
    {
        var ana = AuthIds.New();
        var request = await WantedAsync(ana);
        for (var i = 0; i < MatchRules.DailyLimit; i++)
        {
            await notifier.NotifyAsync(Product(), Ct);
        }

        var fourth = Product();
        var notified = await notifier.NotifyAsync(fourth, Ct);

        Assert.Equal(0, notified);
        Assert.Equal(MatchRules.DailyLimit, center.Sent.Count);
        Assert.False((await matches.FindByRequestAsync(request, Ct)).Single(m => m.ProductId == fourth.Id).NotifiedRequester);
    }

    [Fact]
    public async Task TheSameListing_IsNotNotifiedTwice()
    {
        var notifications = new InMemoryNotificationRepository();
        var real = new NotificationCenter(notifications, new PushQueue(), time, NullLogger<NotificationCenter>.Instance);
        var once = Notifier(real, time);
        var ana = AuthIds.New();
        await WantedAsync(ana);
        var product = Product();

        await once.NotifyAsync(product, Ct);
        await once.NotifyAsync(product, Ct);

        Assert.Single(await notifications.FindPageAsync(ana, null, null, 10, Ct));
    }

    [Theory]
    [InlineData(36_000, 30_000)]
    [InlineData(36_001, 30_001)]
    [InlineData(1, 1)]
    public void MinBudget_IsTheSmallestWhoseMarginCoversThePrice(long price, long budget)
    {
        Assert.Equal(budget, ProductMatchNotifier.MinBudget(price));
        Assert.True(price <= MatchRules.MaxPrice(budget));
    }

    // HU-12: "Avísame cuando aparezca".
    private async Task<string> SavedAsync(string user, string query, string community = Campus)
    {
        var id = AuthIds.New();
        await savedSearches.SaveAsync(
            new SavedSearchDocument
            {
                Id = id,
                UserId = user,
                CommunityId = community,
                Query = query,
                Terms = SavedSearchService.Terms(query),
                Key = $"{user}:{query}",
                CreatedAt = Now.UtcDateTime,
                ExpiresAt = Now.UtcDateTime.AddDays(30),
            },
            Now.UtcDateTime,
            Ct).ConfigureAwait(false);
        return id;
    }

    [Fact]
    public async Task ASavedSearch_TellsWhoSavedIt_WithTheMockupText()
    {
        var luis = AuthIds.New();
        var search = await SavedAsync(luis, "bata talla M");
        var product = Product();

        var notified = await notifier.NotifyAsync(product, Ct);

        Assert.Equal(1, notified);
        Assert.Equal(
            new Notice(
                luis,
                NotificationTypes.SavedSearchMatch,
                "¡Ya apareció lo que buscabas!",
                "Diego publicó «Bata blanca de laboratorio, talla M» por $280. Activaste “Avísame cuando aparezca”.",
                NotificationTargets.Product,
                product.Id,
                NotificationCategories.Matches,
                $"saved_search:{search}:{product.Id}"),
            Assert.Single(center.Sent));
    }

    [Fact]
    public async Task ASavedSearch_NeedsAllItsWords_AndNeverTheSellerOrAnotherCampus()
    {
        await SavedAsync(AuthIds.New(), "bata talla chica");
        await SavedAsync(diego, "bata");
        await SavedAsync(AuthIds.New(), "bata", community: "6716a0000000000000000c02");

        Assert.Equal(0, await notifier.NotifyAsync(Product(), Ct));
    }

    [Fact]
    public async Task WhoAlreadyGotTheRequestNotice_DoesNotGetItTwice()
    {
        var ana = AuthIds.New();
        await WantedAsync(ana);
        await SavedAsync(ana, "bata");
        await SavedAsync(ana, "bata laboratorio");

        var notified = await notifier.NotifyAsync(Product(), Ct);

        Assert.Equal(1, notified);
        Assert.Equal(NotificationTypes.ProductMatch, Assert.Single(center.Sent).Type);
    }

    [Fact]
    public async Task TwoSavedSearchesOfTheSamePerson_GiveOneNotice()
    {
        var luis = AuthIds.New();
        await SavedAsync(luis, "bata");
        await SavedAsync(luis, "bata laboratorio");

        Assert.Equal(1, await notifier.NotifyAsync(Product(), Ct));
    }
}
