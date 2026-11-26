using MongoDB.Driver;
using Musubi.Api.Modules.GenAi.Data;
using Musubi.TestShared.GenAi;

namespace DatabaseTest;

/// <summary>HU-42: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado (db/036).</summary>
public sealed class MongoAiUsageRepositoryTests(MongoContainerFixture mongo) : AiUsageRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IAiUsageRepository Usage => new MongoAiUsageRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("ai_usage_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
