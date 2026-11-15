using MongoDB.Driver;
using Musubi.Api.Modules.Listings.Data;
using Musubi.TestShared.Listings;

namespace DatabaseTest;

/// <summary>HU-12: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado (db/025).</summary>
public sealed class MongoSavedSearchRepositoryTests(MongoContainerFixture mongo) : SavedSearchRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override ISavedSearchRepository Searches => new MongoSavedSearchRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("saved_searches_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
