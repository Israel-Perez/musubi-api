using MongoDB.Driver;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.TestShared.Listings;

namespace DatabaseTest;

/// <summary>HU-06: los repositorios de Mongo cumplen el contrato contra MongoDB real con db/ aplicado (incluida su validacion).</summary>
public sealed class MongoListingRepositoryTests(MongoContainerFixture mongo) : ListingRepositoryContract, IAsyncLifetime
{
    private IMongoDatabase? database;

    internal override ICategoryRepository Categories => new MongoCategoryRepository(database!);

    internal override IProductRepository Products => new MongoProductRepository(database!);

    internal override async Task<string> UtttIdAsync() =>
        (await new MongoCommunityRepository(database!).FindBySlugAsync("uttt", TestContext.Current.CancellationToken).ConfigureAwait(false))!.Id;

    public async ValueTask InitializeAsync() => database = await mongo.MigratedDatabaseAsync("listings_contract").ConfigureAwait(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
