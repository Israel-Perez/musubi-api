using MongoDB.Driver;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared.Reputation;

namespace DatabaseTest;

/// <summary>HU-27/HU-45: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado.</summary>
public sealed class MongoRatingRepositoryTests(MongoContainerFixture mongo) : RatingRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IRatingRepository Ratings => new MongoRatingRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("ratings_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
