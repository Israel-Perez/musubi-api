using MongoDB.Driver;
using Musubi.Api.Modules.GenAi.Data;
using Musubi.TestShared.GenAi;

namespace DatabaseTest;

/// <summary>HU-32/33/34: el repositorio de Mongo cumple el contrato contra MongoDB real con db/ aplicado.</summary>
public sealed class MongoAgreementRepositoryTests(MongoContainerFixture mongo) : AgreementRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IAgreementRepository Agreements => new MongoAgreementRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("agreements_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
