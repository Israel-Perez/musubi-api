using MongoDB.Driver;
using Musubi.Api.Modules.Recommendations.Data;
using Musubi.TestShared.Recommendations;

namespace DatabaseTest;

/// <summary>HU-35: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado.</summary>
public sealed class MongoInteractionRepositoryTests(MongoContainerFixture mongo) : InteractionRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IInteractionRepository Interactions => new MongoInteractionRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("interactions_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
