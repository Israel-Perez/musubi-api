using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Notifications.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared;
using UnitTest.Common;

namespace UnitTest.Matching;

/// <summary>
/// HU-19 (decisiones de la usuaria, 13-Nov-2026): quien vende algo compatible con una solicitud nueva recibe "Hay una
/// solicitud compatible con tu producto". Compatible = misma categoria (o cualquiera con "otro"), la mitad o mas de
/// las palabras del nombre buscado (RN-28) y hasta 20 % arriba del presupuesto. Un aviso por solicitud y persona
/// (RN-29), hasta 3 al dia (RN-30) y nunca sobre lo propio ni entre personas bloqueadas (RN-31, HU-57).
/// </summary>
public sealed class WantedMatchTests
{
    private const string Campus = "6716a0000000000000000c01";

    // Viernes 13 de noviembre de 2026, 12:00 en el campus (18:00 UTC).
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly RecordingNotificationCenter center = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly WantedMatchNotifier notifier;
    private readonly string ana = AuthIds.New();

    public WantedMatchTests()
    {
        notifier = new WantedMatchNotifier(new ProductMatches(products), new BlockDirectory(blocks), center, time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WantedPublished Request(
        string title = "Bata de laboratorio talla M",
        string? category = "laboratorio",
        long budget = 30_000,
        string? id = null) =>
        new(id ?? AuthIds.New(), Campus, ana, "Ana R.", title, category, budget);

    private async Task<string> ProductAsync(
        string seller,
        string title = "Bata blanca de laboratorio, talla M",
        string category = "laboratorio",
        long price = 28_000,
        string status = ProductStatuses.Active,
        string description = "",
        string community = Campus)
    {
        var id = AuthIds.New();
        await products.InsertAsync(
            new ProductDocument
            {
                Id = id,
                CommunityId = community,
                SellerId = seller,
                Seller = new ProductSeller { Name = "Diego H." },
                Title = title,
                Description = description,
                CategorySlug = category,
                PriceCents = price,
                Stock = status == ProductStatuses.SoldOut ? 0 : 1,
                Status = status,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            },
            Ct).ConfigureAwait(false);
        return id;
    }

    [Fact]
    public async Task ACompatibleListing_TellsItsSeller_WithTheMockupText()
    {
        var diego = AuthIds.New();
        await ProductAsync(diego);
        var request = Request();

        var notified = await notifier.NotifyAsync(request, Ct);

        Assert.Equal(1, notified);
        Assert.Equal(
            new Notice(
                diego,
                NotificationTypes.WantedMatch,
                "Hay una solicitud compatible con tu producto",
                "Ana busca Bata de laboratorio talla M. Tu publicación «Bata blanca de laboratorio, talla M» coincide.",
                NotificationTargets.WantedBoard,
                request.RequestId,
                NotificationCategories.Matches,
                $"wanted_match:{request.RequestId}"),
            Assert.Single(center.Sent));
    }

    [Theory]
    [InlineData("Bata de laboratorio talla M", "laboratorio", 36_000, true)] // justo el 20 % de mas
    [InlineData("Bata de laboratorio talla M", "laboratorio", 36_001, false)] // mas del 20 %
    [InlineData("Bata de laboratorio talla M", "ropa", 28_000, false)] // otra categoria
    [InlineData("Bata de laboratorio talla M", null, 28_000, true)] // "otro": cualquier categoria
    [InlineData("Bata de seda talla chica", "laboratorio", 28_000, true)] // "bata" y "talla": la mitad
    [InlineData("Bata de algodón grande con bolsas", "laboratorio", 28_000, false)] // solo "bata": menos de la mitad
    public async Task Compatible_IsTheCategory_HalfTheWords_AndUpTo20PercentOverBudget(
        string title,
        string? category,
        long price,
        bool expected)
    {
        await ProductAsync(AuthIds.New(), price: price);

        await notifier.NotifyAsync(Request(title: title, category: category), Ct);

        Assert.Equal(expected, center.Sent.Count == 1);
    }

    [Fact]
    public async Task TheWordsAlsoComeFromTheDescription_AndPluralsMatch()
    {
        await ProductAsync(AuthIds.New(), title: "Bata blanca", description: "Para laboratorios, talla M.");

        await notifier.NotifyAsync(Request(title: "Batas de laboratorio"), Ct);

        Assert.Single(center.Sent);
    }

    [Fact]
    public async Task OnlyAvailableListings_OfThisCampus_AndNeverMine()
    {
        await ProductAsync(AuthIds.New(), status: ProductStatuses.Hidden);
        await ProductAsync(AuthIds.New(), status: ProductStatuses.SoldOut);
        await ProductAsync(AuthIds.New(), status: ProductStatuses.Sold);
        await ProductAsync(AuthIds.New(), community: "6716a0000000000000000c02");
        // RN-31: la publicacion de quien busca.
        await ProductAsync(ana);

        Assert.Equal(0, await notifier.NotifyAsync(Request(), Ct));
        Assert.Empty(center.Sent);
    }

    [Fact]
    public async Task ABlock_InEitherDirection_LeavesThatPersonOut()
    {
        var diego = AuthIds.New();
        var luis = AuthIds.New();
        await ProductAsync(diego);
        await ProductAsync(luis);
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = ana, BlockedId = diego, CreatedAt = Now.UtcDateTime }, Ct);
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = luis, BlockedId = ana, CreatedAt = Now.UtcDateTime }, Ct);

        Assert.Equal(0, await notifier.NotifyAsync(Request(), Ct));
    }

    [Fact]
    public async Task OneNoticePerPerson_NamingTheirClosestListing()
    {
        var diego = AuthIds.New();
        await ProductAsync(diego, title: "Bata de laboratorio", price: 20_000);
        await ProductAsync(diego, title: "Bata de laboratorio talla M", price: 34_000);
        await ProductAsync(diego, title: "Bata de laboratorio talla M usada", price: 29_000);

        await notifier.NotifyAsync(Request(), Ct);

        // Las dos tienen todas las palabras; la de $290 queda mas cerca de los $300.
        Assert.Contains("«Bata de laboratorio talla M usada»", Assert.Single(center.Sent).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AtMostThreeADay_ThenTheNextDayAgain()
    {
        var diego = AuthIds.New();
        await ProductAsync(diego);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(1, await notifier.NotifyAsync(Request(), Ct));
        }

        Assert.Equal(0, await notifier.NotifyAsync(Request(), Ct));
        Assert.Equal(3, center.Sent.Count);
    }

    [Fact]
    public async Task TheDailyLimit_CountsFromMidnightOnCampus()
    {
        // Jueves 12 a las 23:30 en el campus (05:30 UTC del viernes 13): cuentan para el jueves.
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 13, 5, 30, 0, TimeSpan.Zero));
        var real = new NotificationCenter(new InMemoryNotificationRepository(), new PushQueue(), clock, NullLogger<NotificationCenter>.Instance);
        var daily = new WantedMatchNotifier(new ProductMatches(products), new BlockDirectory(blocks), real, clock);
        await ProductAsync(AuthIds.New());
        for (var i = 0; i < 3; i++)
        {
            await daily.NotifyAsync(Request(), Ct);
        }

        Assert.Equal(0, await daily.NotifyAsync(Request(), Ct));

        // Viernes 13 a las 00:00 en el campus: un dia nuevo.
        clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(1, await daily.NotifyAsync(Request(), Ct));
    }

    [Fact]
    public async Task TheSameRequest_IsNotNotifiedTwice()
    {
        var notifications = new InMemoryNotificationRepository();
        var real = new NotificationCenter(notifications, new PushQueue(), time, NullLogger<NotificationCenter>.Instance);
        var once = new WantedMatchNotifier(new ProductMatches(products), new BlockDirectory(blocks), real, time);
        var diego = AuthIds.New();
        await ProductAsync(diego);
        var request = Request();

        await once.NotifyAsync(request, Ct);
        await once.NotifyAsync(request, Ct);

        Assert.Equal(1, await notifications.CountSinceAsync(diego, NotificationTypes.WantedMatch, Now.UtcDateTime.AddDays(-1), Ct));
    }

    [Theory]
    [InlineData("Calculadora Casio fx-991ES Plus", new[] { "calculadora", "casio", "fx", "991", "plu" })] // "Plus" queda "plu", como al buscar
    [InlineData("Las batas de la talla M", new[] { "bata", "talla" })]
    [InlineData("de la y o", new string[0])]
    public void Terms_SkipTheWordsThatSayNothing(string title, string[] expected) =>
        Assert.Equal(expected, MatchRules.Terms(title));

    [Fact]
    public async Task WithoutUsefulWords_NothingIsSearched()
    {
        await ProductAsync(AuthIds.New());

        Assert.Equal(0, await notifier.NotifyAsync(Request(title: "de la"), Ct));
    }
}
