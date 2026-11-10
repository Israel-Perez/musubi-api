using MongoDB.Driver;
using Musubi.Api.Modules.Listings.Data;
using Musubi.TestShared.Listings;

namespace DatabaseTest;

/// <summary>HU-53: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado.</summary>
public sealed class MongoFavoriteRepositoryTests(MongoContainerFixture mongo) : FavoriteRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IFavoriteRepository Favorites => new MongoFavoriteRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("favorites_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
