using MongoDB.Driver;
using Musubi.Api.Modules.Profiles.Data;

namespace DatabaseTest;

/// <summary>HU-54: preferencias en Mongo, con db/015 y su validador reales.</summary>
public sealed class MongoPreferenceRepositoryTests(MongoContainerFixture mongo) : IAsyncLifetime
{
    private IMongoDatabase? database;

    public async ValueTask InitializeAsync() =>
        database = await mongo.MigratedDatabaseAsync("preferences_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Theme_UpsertsByUserAndCanBeDeleted()
    {
        var repository = new MongoPreferenceRepository(database!);
        const string userId = "6716a0000000000000000001";

        Assert.Null(await repository.FindAsync(userId, TestContext.Current.CancellationToken));
        var saved = await repository.SetThemeAsync(userId, "dark", TestContext.Current.CancellationToken);
        Assert.Equal("dark", saved.Theme);
        Assert.Equal("dark", (await repository.FindAsync(userId, TestContext.Current.CancellationToken))?.Theme);

        await repository.DeleteAsync(userId, TestContext.Current.CancellationToken);

        Assert.Null(await repository.FindAsync(userId, TestContext.Current.CancellationToken));
    }
}
