using MongoDB.Driver;
using Musubi.Api.Modules.Orders.Data;
using Musubi.TestShared.Orders;

namespace DatabaseTest;

/// <summary>HU-55: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado (validador de db/027).</summary>
public sealed class MongoPayoutAccountRepositoryTests(MongoContainerFixture mongo) : PayoutAccountRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IPayoutAccountRepository Accounts => new MongoPayoutAccountRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("payout_accounts_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
