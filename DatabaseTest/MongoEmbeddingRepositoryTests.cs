using MongoDB.Driver;
using Musubi.Api.Modules.Matching.Data;
using Musubi.TestShared.Matching;

namespace DatabaseTest;

/// <summary>HU-60: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado (db/035).</summary>
public sealed class MongoEmbeddingRepositoryTests(MongoContainerFixture mongo) : EmbeddingRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IEmbeddingRepository Embeddings => new MongoEmbeddingRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("embeddings_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
