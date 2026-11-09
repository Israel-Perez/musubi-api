using MongoDB.Driver;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.TestShared.PurchaseRequests;

namespace DatabaseTest;

/// <summary>HU-13: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado (incluida su validacion).</summary>
public sealed class MongoWantedRepositoryTests(MongoContainerFixture mongo) : WantedRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IWantedRepository Requests => new MongoWantedRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("wanted_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
