using MongoDB.Driver;
using Musubi.Api.Modules.Admin.Data;
using Musubi.TestShared.Admin;

namespace DatabaseTest;

/// <summary>HU-39/40: la bitacora de Mongo cumple el contrato contra MongoDB real con db/ aplicado (db/030).</summary>
public sealed class MongoModerationLogRepositoryTests(MongoContainerFixture mongo) : ModerationLogRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IModerationLogRepository Log => new MongoModerationLogRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("moderation_log_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
