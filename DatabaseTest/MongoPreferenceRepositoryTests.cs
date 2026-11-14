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

    [Fact]
    public async Task Interests_UpsertWithTheSystemTheme_AndKeepTheChosenTheme()
    {
        var repository = new MongoPreferenceRepository(database!);
        var ct = TestContext.Current.CancellationToken;
        const string fresh = "6716a0000000000000000002";
        const string themed = "6716a0000000000000000003";

        // HU-66: el validador de db/018 exige theme; el upsert de los intereses lo pone en system.
        var created = await repository.SetInterestsAsync(fresh, ["libros", "laptops"], ct);
        await repository.SetThemeAsync(themed, "dark", ct);
        await repository.SetInterestsAsync(themed, ["dibujo"], ct);
        var kept = await repository.SetThemeAsync(themed, "light", ct);

        Assert.Equal("system", created.Theme);
        Assert.Equal(["libros", "laptops"], (await repository.FindAsync(fresh, ct))?.Interests);
        Assert.Equal(["dibujo"], kept.Interests);
        Assert.Equal("light", kept.Theme);
    }
}
