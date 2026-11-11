using MongoDB.Driver;
using Musubi.Api.Modules.Notifications.Data;
using Musubi.TestShared.Notifications;

namespace DatabaseTest;

/// <summary>HU-31: los repositorios de Mongo cumplen el contrato contra MongoDB real con db/ aplicado.</summary>
public sealed class MongoNotificationRepositoryTests(MongoContainerFixture mongo) : NotificationRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override INotificationRepository Notifications => new MongoNotificationRepository(database!);

    internal override IDeviceTokenRepository Tokens => new MongoDeviceTokenRepository(database!);

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("notifications_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
