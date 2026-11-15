using MongoDB.Driver;
using Musubi.Api.Modules.Matching.Data;
using Musubi.TestShared.Matching;

namespace DatabaseTest;

/// <summary>RF-25: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado (db/024).</summary>
public sealed class MongoMatchRepositoryTests(MongoContainerFixture mongo) : MatchRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IMatchRepository Matches => new MongoMatchRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("matches_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
