using MongoDB.Driver;
using Musubi.Api.Modules.Listings.Data;
using Musubi.TestShared.Listings;

namespace DatabaseTest;

/// <summary>HU-51: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado (db/029).</summary>
public sealed class MongoStockAlertRepositoryTests(MongoContainerFixture mongo) : StockAlertRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IStockAlertRepository Alerts => new MongoStockAlertRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("stock_alerts_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
