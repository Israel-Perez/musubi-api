using MongoDB.Driver;
using Musubi.Api.Modules.Chat.Data;
using Musubi.TestShared.Chat;

namespace DatabaseTest;

/// <summary>HU-20: los repositorios de Mongo cumplen el contrato contra MongoDB real con db/ aplicado (incluida su validacion).</summary>
public sealed class MongoChatRepositoryTests(MongoContainerFixture mongo) : ChatRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override IConversationRepository Conversations => new MongoConversationRepository(database!);

    internal override IMessageRepository Messages => new MongoMessageRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("chat_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
