using MongoDB.Driver;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.TestShared.Reputation;

namespace DatabaseTest;

/// <summary>HU-28 y HU-57: los repositorios de Mongo cumplen el contrato contra MongoDB real con db/ aplicado (incluida su validacion).</summary>
public sealed class MongoReputationRepositoryTests(MongoContainerFixture mongo) : ReputationRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IReportRepository Reports => new MongoReportRepository(database!);

    internal override IBlockRepository Blocks => new MongoBlockRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("reputation_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
