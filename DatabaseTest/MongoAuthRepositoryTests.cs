using MongoDB.Driver;
using Musubi.Api.Modules.Auth.Data;
using Musubi.TestShared.Auth;

namespace DatabaseTest;

/// <summary>02-01: los repositorios de Mongo cumplen el contrato contra MongoDB real con db/ aplicado.</summary>
public sealed class MongoAuthRepositoryTests(MongoContainerFixture mongo) : AuthRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override ICommunityRepository Communities => new MongoCommunityRepository(database!);

    internal override IUserRepository Users => new MongoUserRepository(database!);

    internal override IVerificationCodeRepository Codes => new MongoVerificationCodeRepository(database!);

    internal override IRefreshTokenRepository Tokens => new MongoRefreshTokenRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("auth_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
