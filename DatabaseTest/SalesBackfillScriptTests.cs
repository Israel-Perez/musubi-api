using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.Profiles.Data;

namespace DatabaseTest;

/// <summary>
/// HU-26: <c>db/021-ventas-compras.js</c> cuenta "Ventas" y "Compras" de los pedidos completados que ya existian, deja
/// el numero exacto aunque se corra otra vez y no recrea el perfil de una cuenta borrada (HU-46).
/// </summary>
public sealed class SalesBackfillScriptTests(MongoContainerFixture mongo)
{
    private static readonly DateTime Now = new(2026, 11, 13, 18, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static UserDocument User(string id, string community) => new()
    {
        Id = id,
        CommunityId = community,
        Email = $"u.{id}@uttt.edu.mx",
        EmailNormalized = $"u.{id}@uttt.edu.mx",
        PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
        FullName = "Persona de Prueba",
        Status = UserStatuses.Active,
        EmailVerifiedAt = Now,
        CreatedAt = Now,
    };

    private static OrderDocument Order(string community, string buyer, string seller, string status) => new()
    {
        Id = AuthIds.New(),
        CommunityId = community,
        Number = 1001,
        BuyerId = buyer,
        SellerId = seller,
        BuyerName = "Ana Sofía Ramírez",
        SellerName = "Diego Hernández",
        ConversationId = AuthIds.New(),
        Item = new OrderItem { SourceId = AuthIds.New(), Title = "Calculadora", PriceCents = 50_000, Quantity = 1, TotalCents = 50_000 },
        Delivery = new OrderDelivery { PointSlug = "biblioteca", PointName = "Biblioteca", At = Now },
        Status = status,
        History = [new OrderTransition { Status = status, At = Now }],
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public async Task CountsTheCompletedOrders_AndRunningItAgainKeepsTheSameNumbers()
    {
        const string name = "script_ventas";
        var db = await mongo.MigratedDatabaseAsync(name);
        var community = AuthIds.New();
        var ana = AuthIds.New();
        var diego = AuthIds.New();
        var deleted = AuthIds.New();
        var users = new MongoUserRepository(db);
        Assert.True(await users.TryInsertAsync(User(ana, community), Ct));
        Assert.True(await users.TryInsertAsync(User(diego, community), Ct));
        var orders = new MongoOrderRepository(db);
        await orders.TryInsertAsync(Order(community, ana, diego, OrderStatuses.Completed), Ct);
        await orders.TryInsertAsync(Order(community, ana, diego, OrderStatuses.Completed), Ct);
        await orders.TryInsertAsync(Order(community, ana, diego, OrderStatuses.Cancelled), Ct);
        await orders.TryInsertAsync(Order(community, deleted, diego, OrderStatuses.Completed), Ct);

        await mongo.ApplyScriptsAsync(name);
        await mongo.ApplyScriptsAsync(name);
        var profiles = new MongoProfileRepository(db);
        var seller = await profiles.FindAsync(diego, Ct);
        var buyer = await profiles.FindAsync(ana, Ct);

        Assert.Equal((3, 0), (seller!.Stats.Sales, seller.Stats.Purchases));
        Assert.Equal((0, 2), (buyer!.Stats.Sales, buyer.Stats.Purchases));
        Assert.Equal(("Diego Hernández", true), (seller.FullName, seller.Visibility.Sales));
        Assert.Null(await profiles.FindAsync(deleted, Ct));
    }
}
