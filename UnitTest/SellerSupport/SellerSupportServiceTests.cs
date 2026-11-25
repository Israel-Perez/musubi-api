using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Modules.SellerSupport;
using Musubi.TestShared;
using UnitTest.Common;

namespace UnitTest.SellerSupport;

/// <summary>
/// HU-36/37/38 (maqueta <c>Mis-productos</c> → "Llevan tiempo sin venderse"; decisiones de la usuaria del 15-Nov-2026):
/// las disponibles con mas de 3 semanas y sin pedidos (RN-35, RN-36), con sus vistas, sus mensajes, quien las busca
/// (RN-37: las busquedas guardadas solo se cuentan) e ideas para venderlas (RN-38); y el aviso, una vez a las 3 semanas.
/// </summary>
public sealed class SellerSupportServiceTests
{
    private const string Campus = "6716a0000000000000000c01";

    private static readonly int[] PeopleCounts = [0, 1, 12];

    // Domingo 15 de noviembre de 2026, 12:00 en el campus (18:00 UTC).
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 18, 0, 0, TimeSpan.Zero);

    private readonly InMemoryProductRepository products = new();
    private readonly InMemoryProductStatsRepository stats;
    private readonly InMemoryWantedRepository requests = new();
    private readonly InMemorySavedSearchRepository savedSearches = new();
    private readonly InMemoryBlockRepository blocks = new();
    private readonly InMemoryConversationRepository conversations = new();
    private readonly InMemoryOrderRepository orders = new();
    private readonly RecordingNotificationCenter center = new();
    private readonly ManualTimeProvider time = new(Now);
    private readonly RecordingCopywriter copywriter = new();
    private readonly SellerSupportService service;
    private readonly SlowListingJob job;
    private readonly string diego = AuthIds.New();

    public SellerSupportServiceTests()
    {
        stats = new InMemoryProductStatsRepository(products);
        var listings = new SellerListings(products, stats);
        var demand = new ProductDemandService(
            new WantedMatchDirectory(requests, time),
            new SavedSearchService(savedSearches, new MemberDirectory(new InMemoryCommunityRepository(), new InMemoryUserRepository()), time),
            new BlockDirectory(blocks),
            UnitTest.Matching.Semantic.Off());
        var openOrders = new OpenOrders(orders);
        service = new SellerSupportService(listings, demand, new ConversationDirectory(conversations), openOrders, new Categories(), copywriter, time);
        job = new SlowListingJob(listings, openOrders, demand, center);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ProductDocument> ProductAsync(
        int daysAgo,
        string title = "Bata de laboratorio blanca, talla M",
        long price = 25_000,
        string status = ProductStatuses.Active,
        string? seller = null,
        string description = "Bata de manga larga, talla M. La usé dos semestres en el laboratorio de química y está limpia.",
        int photos = 3)
    {
        var product = new ProductDocument
        {
            Id = AuthIds.New(),
            CommunityId = Campus,
            SellerId = seller ?? diego,
            Seller = new ProductSeller { Name = "Diego H." },
            Title = title,
            Description = description,
            CategorySlug = "laboratorio",
            PriceCents = price,
            Condition = ProductConditions.Used,
            Stock = status == ProductStatuses.SoldOut ? 0 : 1,
            Status = status,
            Photos = [.. Enumerable.Range(0, photos).Select(i => $"media/products/{diego}/{i}.webp")],
            CreatedAt = Now.UtcDateTime.AddDays(-daysAgo),
            UpdatedAt = Now.UtcDateTime.AddDays(-daysAgo),
        };
        await products.InsertAsync(product, Ct).ConfigureAwait(false);
        return product;
    }

    private async Task<string> WantedAsync(string requester, string title = "Bata de laboratorio talla M", long budget = 30_000, string name = "Fernanda G.")
    {
        var id = AuthIds.New();
        await requests.InsertAsync(
            new WantedRequestDocument
            {
                Id = id,
                CommunityId = Campus,
                RequesterId = requester,
                RequesterName = name,
                Title = title,
                CategorySlug = "laboratorio",
                CategoryName = "Laboratorio",
                BudgetCents = budget,
                ExpiresAt = Now.UtcDateTime.AddDays(7),
                Status = WantedStatuses.Active,
                CreatedAt = Now.UtcDateTime.AddDays(-1),
            },
            Ct).ConfigureAwait(false);
        return id;
    }

    private async Task SavedAsync(string user, string query) =>
        await savedSearches.SaveAsync(
            new SavedSearchDocument
            {
                Id = AuthIds.New(),
                UserId = user,
                CommunityId = Campus,
                Query = query,
                Terms = SavedSearchService.Terms(query),
                Key = $"{user}:{query}",
                CreatedAt = Now.UtcDateTime,
                ExpiresAt = Now.UtcDateTime.AddDays(30),
            },
            Now.UtcDateTime,
            Ct).ConfigureAwait(false);

    private async Task OrderAsync(ProductDocument product, string status) =>
        Assert.True(await orders.TryInsertAsync(
            new OrderDocument
            {
                Id = AuthIds.New(),
                CommunityId = Campus,
                Number = 1,
                BuyerId = AuthIds.New(),
                SellerId = product.SellerId,
                ConversationId = AuthIds.New(),
                Item = new OrderItem { ProductId = product.Id, SourceId = product.Id, Title = product.Title, PriceCents = product.PriceCents, Quantity = 1, TotalCents = product.PriceCents },
                Status = status,
                CreatedAt = Now.UtcDateTime.AddDays(-2),
                UpdatedAt = Now.UtcDateTime.AddDays(-2),
            },
            Ct).ConfigureAwait(false));

    private async Task ConversationAsync(ProductDocument product) =>
        await conversations.InsertOrGetAsync(
            new ConversationDocument
            {
                Id = AuthIds.New(),
                CommunityId = Campus,
                BuyerId = AuthIds.New(),
                SellerId = product.SellerId,
                Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = product.Id, Title = product.Title },
                CreatedAt = Now.UtcDateTime.AddDays(-3),
                LastMessageAt = Now.UtcDateTime.AddDays(-3),
            },
            Ct).ConfigureAwait(false);

    [Fact]
    public async Task Slow_AreTheAvailableOnesWithMoreThanThreeWeeks_TheOldestFirst()
    {
        var bata = await ProductAsync(34);
        var calculadora = await ProductAsync(22, title: "Calculadora científica Casio");
        await ProductAsync(20, title: "Juego de escuadras");
        await ProductAsync(40, title: "Cálculo de una variable", status: ProductStatuses.Hidden);
        await ProductAsync(40, title: "Física universitaria", status: ProductStatuses.SoldOut);
        await ProductAsync(40, title: "Bata ajena", seller: AuthIds.New());

        var slow = await service.SlowAsync(diego, Ct);

        Assert.Equal(21, slow.ThresholdDays);
        Assert.Equal([(bata.Id, 34), (calculadora.Id, 22)], slow.Items.Select(i => (i.Product.Id, i.DaysListed)));
    }

    [Fact]
    public async Task Slow_LeavesOutWhatSoldByAnOrder_OrHasAnOpenOne()
    {
        var sold = await ProductAsync(30);
        var pending = await ProductAsync(30);
        var cancelled = await ProductAsync(30);
        await OrderAsync(sold, OrderStatuses.Completed);
        await OrderAsync(pending, OrderStatuses.Pending);
        await OrderAsync(cancelled, OrderStatuses.Cancelled);

        var slow = await service.SlowAsync(diego, Ct);

        // RN-36: un pedido completado ya es una venta; uno abierto ya se esta vendiendo; uno cancelado no cuenta.
        Assert.Equal(cancelled.Id, Assert.Single(slow.Items).Product.Id);
    }

    [Fact]
    public async Task Slow_CountsTheViewsOfThirtyDays_AndTheChatsFromTheListing()
    {
        var bata = await ProductAsync(34);
        await stats.IncrementViewAsync(Campus, bata.Id, Now.UtcDateTime.AddDays(-29), Ct);
        await stats.IncrementViewAsync(Campus, bata.Id, Now.UtcDateTime.AddDays(-1), Ct);
        await stats.IncrementViewAsync(Campus, bata.Id, Now.UtcDateTime, Ct);
        await stats.IncrementViewAsync(Campus, bata.Id, Now.UtcDateTime.AddDays(-30), Ct); // ya fuera de los 30 dias
        await ConversationAsync(bata);
        await ConversationAsync(bata);

        var item = Assert.Single((await service.SlowAsync(diego, Ct)).Items);

        Assert.Equal((3L, 2), (item.Views, item.Messages));
    }

    [Fact]
    public async Task Slow_SaysWhoLooksForIt_CountingTheSavedSearchesWithoutNamingAnyone()
    {
        await ProductAsync(34);
        var fernanda = AuthIds.New();
        await WantedAsync(fernanda);
        await WantedAsync(AuthIds.New(), title: "Bata talla M para química");
        await SavedAsync(fernanda, "bata"); // ya cuenta por su solicitud
        await SavedAsync(AuthIds.New(), "bata laboratorio");
        await SavedAsync(AuthIds.New(), "bata talla chica"); // no coincide: falta "chica"

        var item = Assert.Single((await service.SlowAsync(diego, Ct)).Items);

        Assert.Equal(new DemandSummaryResponse(People: 3, Requests: 2), item.Demand);
    }

    [Fact]
    public async Task Slow_WithNothing_IsEmpty()
    {
        await ProductAsync(5);

        Assert.Empty((await service.SlowAsync(diego, Ct)).Items);
        Assert.Empty((await service.SlowAsync(AuthIds.New(), Ct)).Items);
    }

    [Fact]
    public async Task Demand_ListsThePublicRequests_AndOnlyCountsTheAlerts()
    {
        var bata = await ProductAsync(34);
        var cheap = await WantedAsync(AuthIds.New(), budget: 21_000, name: "Luis M.");
        var best = await WantedAsync(AuthIds.New(), budget: 30_000);
        await SavedAsync(AuthIds.New(), "bata laboratorio");
        var blocked = AuthIds.New();
        await WantedAsync(blocked);
        await blocks.InsertIfAbsentAsync(new BlockDocument { Id = AuthIds.New(), BlockerId = blocked, BlockedId = diego, CreatedAt = Now.UtcDateTime }, Ct);

        var demand = await service.DemandAsync(diego, bata.Id, Ct);

        Assert.Null(demand.Error);
        Assert.Equal((3, 1), (demand.Value!.People, demand.Value.Alerts));
        // Igual de parecidas: primero el presupuesto mayor. Nunca la de quien bloqueo (RN-31, HU-57).
        Assert.Equal([(best, "Fernanda G.", 30_000L), (cheap, "Luis M.", 21_000L)], demand.Value.Requests.Select(r => (r.Id, r.RequesterName, r.BudgetCents)));
    }

    [Fact]
    public async Task Demand_OfSomeoneElsesListing_IsNotFound()
    {
        var other = await ProductAsync(34, seller: AuthIds.New());

        Assert.Equal(ListingsErrors.ProductNotFound, (await service.DemandAsync(diego, other.Id, Ct)).Error);
        Assert.Equal(ListingsErrors.ProductNotFound, (await service.DemandAsync(diego, "no-es-un-id", Ct)).Error);
    }

    [Fact]
    public async Task Ideas_SendTheListingAndItsDemand_WithoutNamesOrContactData()
    {
        var bata = await ProductAsync(
            34,
            title: "Bata de Diego talla M",
            description: "Escríbeme al 771 123 4567 o a diego.h@uttt.edu.mx. La usé dos semestres.",
            photos: 2);
        await stats.IncrementViewAsync(Campus, bata.Id, Now.UtcDateTime.AddDays(-2), Ct);
        await ConversationAsync(bata);
        await WantedAsync(AuthIds.New(), title: "Bata talla M, Fernanda", budget: 22_000);
        await SavedAsync(AuthIds.New(), "bata");

        var ideas = await service.IdeasAsync(diego, bata.Id, Ct);

        Assert.Equal(new ListingIdeasResponse("Bata de laboratorio talla M", "Bata blanca de manga larga."), ideas.Value);
        var prompt = copywriter.Prompts.Single();
        Assert.Equal(
            ("Bata de Vendedor talla M", "Escríbeme al [numero] o a [correo]. La usé dos semestres.", "Laboratorio", 2, 34, 1L, 1, 2),
            (prompt.Title, prompt.Description, prompt.Category, prompt.Photos, prompt.DaysListed, prompt.Views, prompt.Messages, prompt.People));
        // Lo que busca y cuanto ofrece, nunca quien (RNF-06).
        Assert.Equal(new CopyRequest("Bata talla M, Comprador", 22_000), Assert.Single(prompt.Requests));
        var text = ListingCopyText.User(prompt);
        Assert.DoesNotContain("Diego", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Fernanda", text, StringComparison.Ordinal);
        Assert.DoesNotContain(bata.Id, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ideas_WithoutAi_AreUnavailable_AndSendNothing()
    {
        var bata = await ProductAsync(34);
        copywriter.Enabled = false;

        Assert.Equal(SellerSupportErrors.IdeasUnavailable, (await service.IdeasAsync(diego, bata.Id, Ct)).Error);
        Assert.Empty(copywriter.Prompts);

        copywriter.Enabled = true;
        copywriter.Answer = null; // cuota excedida, sin red o con otra forma

        Assert.Equal(SellerSupportErrors.IdeasUnavailable, (await service.IdeasAsync(diego, bata.Id, Ct)).Error);
    }

    [Fact]
    public async Task Ideas_OfSomeoneElsesListing_AreNotFound_AndSendNothing()
    {
        var other = await ProductAsync(34, seller: AuthIds.New());

        Assert.Equal(ListingsErrors.ProductNotFound, (await service.IdeasAsync(diego, other.Id, Ct)).Error);
        Assert.Empty(copywriter.Prompts);
    }

    [Fact]
    public async Task TheNotice_GoesOnceAtThreeWeeks_WithWhoLooksForIt_AndOpensMyProducts()
    {
        var bata = await ProductAsync(21);
        await ProductAsync(20, title: "Juego de escuadras"); // aun no
        await ProductAsync(24, title: "Calculadora científica"); // ya paso su aviso
        await WantedAsync(AuthIds.New());
        await WantedAsync(AuthIds.New());
        await SavedAsync(AuthIds.New(), "bata");

        Assert.Equal(1, await job.RunAsync(Now.UtcDateTime, Ct));

        Assert.Equal(
            new Notice(
                diego,
                NotificationTypes.SlowListing,
                "Tu bata lleva 21 días sin venderse",
                "3 estudiantes buscan algo similar. Te damos ideas para vender más rápido.",
                NotificationTargets.MyProducts,
                bata.Id,
                NotificationCategories.Matches,
                $"slow_listing:{bata.Id}"),
            Assert.Single(center.Sent));
    }

    [Fact]
    public async Task TheNotice_LooksTwoDaysBack_AndSkipsWhatAlreadySold()
    {
        await ProductAsync(23, title: "Calculadora científica");
        var sold = await ProductAsync(22);
        await OrderAsync(sold, OrderStatuses.Completed);

        Assert.Equal(1, await job.RunAsync(Now.UtcDateTime, Ct));

        var notice = Assert.Single(center.Sent);
        Assert.Equal(("Tu calculadora lleva 23 días sin venderse", "Te damos ideas para vender más rápido: el precio, la descripción y las fotos."), (notice.Title, notice.Body));
    }

    [Theory]
    [InlineData("Bata de laboratorio blanca, talla M", "Tu bata lleva 34 días sin venderse")]
    [InlineData("Calculadora, casi nueva", "Tu calculadora lleva 34 días sin venderse")]
    [InlineData("iPad de 9a generación", "Tu iPad lleva 34 días sin venderse")]
    [InlineData("USB de 64 GB", "Tu USB lleva 34 días sin venderse")]
    [InlineData("TI-84 Plus", "«TI-84 Plus» lleva 34 días sin venderse")]
    [InlineData("Set de 12 plumones", "Tu set lleva 34 días sin venderse")]
    [InlineData("2 libros de cálculo", "«2 libros de cálculo» lleva 34 días sin venderse")]
    public void TheNoticeTitle_UsesTheFirstWordWhenItReadsWell(string title, string expected) =>
        Assert.Equal(expected, SlowListingJob.NoticeTitle(title, 34));

    [Fact]
    public void TheNoticeBody_FitsAPush()
    {
        Assert.Equal("1 estudiante busca algo similar. Te damos ideas para vender más rápido.", SlowListingJob.NoticeBody(1));
        Assert.All(PeopleCounts.Select(SlowListingJob.NoticeBody), body => Assert.True(body.Length <= NotificationCenter.MaxBody));
        Assert.True(SlowListingJob.NoticeTitle(new string('a', 80) + " " + new string('b', 40), 365).Length <= 120);
    }

    private sealed class Categories : ICategoryDirectory
    {
        public Task<CategorySummary?> FindAsync(string communityId, string slug, CancellationToken cancellationToken) =>
            Task.FromResult<CategorySummary?>(slug == "laboratorio" ? new CategorySummary(slug, "Laboratorio") : null);
    }

    private sealed class RecordingCopywriter : IListingCopywriter
    {
        public bool Enabled { get; set; } = true;

        public ListingCopy? Answer { get; set; } = new("Bata de laboratorio talla M", "Bata blanca de manga larga.");

        public List<CopyPrompt> Prompts { get; } = [];

        public Task<ListingCopy?> SuggestAsync(CopyPrompt prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(Answer);
        }
    }
}
