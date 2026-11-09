using MongoDB.Driver;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.TestShared.PurchaseRequests;

namespace DatabaseTest;

/// <summary>HU-16: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado (indice unico y validacion).</summary>
public sealed class MongoOfferRepositoryTests(MongoContainerFixture mongo) : OfferRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IOfferRepository Offers => new MongoOfferRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("offer_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
