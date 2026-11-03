using MongoDB.Driver;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.TestShared.Profiles;

namespace DatabaseTest;

/// <summary>HU-04: los repositorios de Mongo cumplen el contrato contra MongoDB real con db/ aplicado (incluida su validacion).</summary>
public sealed class MongoProfileRepositoryTests(MongoContainerFixture mongo) : ProfileRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override ICareerRepository Careers => new MongoCareerRepository(database!);

    internal override IProfileRepository Profiles => new MongoProfileRepository(database!);

    internal override async Task<string> UtttIdAsync() =>
        (await new MongoCommunityRepository(database!).FindBySlugAsync("uttt", TestContext.Current.CancellationToken).ConfigureAwait(false))!.Id;

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("profiles_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
