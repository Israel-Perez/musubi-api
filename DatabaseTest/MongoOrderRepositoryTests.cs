using MongoDB.Driver;
using Musubi.Api.Modules.Orders.Data;
using Musubi.TestShared.Orders;

namespace DatabaseTest;

/// <summary>HU-23: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado.</summary>
public sealed class MongoOrderRepositoryTests(MongoContainerFixture mongo) : OrderRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IOrderRepository Orders => new MongoOrderRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("orders_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
