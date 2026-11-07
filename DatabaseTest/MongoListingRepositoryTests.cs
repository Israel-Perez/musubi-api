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

    // HU-09: con solo palabras vacias del espanol el indice devuelve una pagina vacia, no un error.
    [Fact]
    public async Task Search_WithOnlyStopWords_ReturnsAnEmptyPage()
    {
        var ct = TestContext.Current.CancellationToken;
        var community = AuthIds.New();
        var seller = AuthIds.New();
        await Products.InsertAsync(new ProductDocument
        {
            Id = AuthIds.New(),
            CommunityId = community,
            SellerId = seller,
            Seller = new ProductSeller { Name = "Ana R." },
            Title = "Libro de Biología",
            Description = "La guía de la clase.",
            CategorySlug = "libros",
            PriceCents = 30_000,
            Condition = ProductConditions.Used,
            ConditionDetail = "good",
            Stock = 1,
            Status = ProductStatuses.Active,
            Photos = [$"media/products/{seller}/{Guid.NewGuid():N}.webp"],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }, ct);

        Assert.Empty(await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "de la"), ct));
    }
}
