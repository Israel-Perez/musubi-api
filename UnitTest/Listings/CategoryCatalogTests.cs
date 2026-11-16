using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Caching;
using UnitTest.PurchaseRequests;

namespace UnitTest.Listings;

/// <summary>HU-41: reglas de las categorias (nombre, slug, repetidas, la ultima activa y el maximo) y las caches.</summary>
public sealed class CategoryCatalogTests
{
    private const string Uttt = InMemoryCommunityRepository.UtttId;

    private readonly InMemoryCategoryRepository repository = new();
    private readonly RecordingOutputCacheStore outputCache = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CategoryCatalog Catalog() => new(repository, outputCache);

    [Theory]
    [InlineData("Ropa y batas", "ropa-y-batas")]
    [InlineData("  Electrónica   y  más ", "electronica-y-mas")]
    [InlineData("Dibujo/Arte", "dibujo-arte")]
    [InlineData("Ñandú 2", "nandu-2")]
    public void Slug_ComesFromTheName_WithoutAccentsOrSpaces(string name, string slug)
    {
        Assert.Equal(slug, CategoryCatalog.SlugOf(CategoryCatalog.CleanName(name)!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("¡¿!")]
    [InlineData("Una categoría con un nombre larguísimo XX")]
    public void Name_TooShortLongOrWithoutLetters_IsRejected(string? name)
    {
        Assert.Null(CategoryCatalog.CleanName(name));
    }

    [Fact]
    public void Slug_OfALongName_IsCutWithoutATrailingDash()
    {
        var slug = CategoryCatalog.SlugOf("Material de laboratorio y equipo de seguridad");

        Assert.True(slug.Length <= CategoryCatalog.MaxNameLength);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public async Task Create_AddsItActiveAtTheEnd_AndClearsTheCaches()
    {
        var change = await Catalog().CreateAsync(Uttt, "  Ropa   y batas ", Ct);

        Assert.Equal(CategoryChangeError.None, change.Error);
        Assert.Null(change.Before);
        Assert.Equal(("ropa-y-batas", "Ropa y batas", 7, true), (change.After!.Slug, change.After.Name, change.After.Order, change.After.Active));
        Assert.Equal("ropa-y-batas", (await repository.ListActiveAsync(Uttt, Ct))[^1].Slug);
        Assert.Equal([CachingServiceCollectionExtensions.CatalogTag, CachingServiceCollectionExtensions.ShowcaseTag], outputCache.EvictedTags);
    }

    [Theory]
    [InlineData("Libros")]
    [InlineData("ELECTRONICA")]
    [InlineData("electrónica")]
    public async Task Create_WithTheNameOfAnother_IsRejected(string name)
    {
        var change = await Catalog().CreateAsync(Uttt, name, Ct);

        Assert.Equal(CategoryChangeError.Exists, change.Error);
        Assert.Empty(outputCache.EvictedTags);
    }

    [Fact]
    public async Task Create_WithTheNameOfADeactivatedOne_IsRejected()
    {
        await Catalog().UpdateAsync(Uttt, "dibujo", null, active: false, Ct);

        Assert.Equal(CategoryChangeError.Exists, (await Catalog().CreateAsync(Uttt, "Dibujo", Ct)).Error);
    }

    [Fact]
    public async Task Create_WithAnInvalidName_IsRejected()
    {
        Assert.Equal(CategoryChangeError.InvalidName, (await Catalog().CreateAsync(Uttt, " x ", Ct)).Error);
    }

    [Fact]
    public async Task Create_AfterTheMaximum_IsRejected()
    {
        var community = AuthIds.New();
        for (var i = 0; i < CategoryCatalog.MaxCategories; i++)
        {
            Assert.Equal(CategoryChangeError.None, (await Catalog().CreateAsync(community, $"Categoría {i}", Ct)).Error);
        }

        Assert.Equal(CategoryChangeError.Limit, (await Catalog().CreateAsync(community, "Una más", Ct)).Error);
    }

    [Fact]
    public async Task Create_InAnEmptyCampus_StartsTheOrderAtOne()
    {
        var change = await Catalog().CreateAsync(AuthIds.New(), "Batas", Ct);

        Assert.Equal(1, change.After!.Order);
    }

    [Fact]
    public async Task Rename_KeepsTheSlug_AndReturnsBeforeAndAfter()
    {
        var change = await Catalog().UpdateAsync(Uttt, "dibujo", "Dibujo técnico", null, Ct);

        Assert.Equal(CategoryChangeError.None, change.Error);
        Assert.Equal("Dibujo", change.Before!.Name);
        Assert.Equal(("dibujo", "Dibujo técnico", true), (change.After!.Slug, change.After.Name, change.After.Active));
    }

    [Fact]
    public async Task Rename_ToTheNameOfAnother_IsRejected_ButTheSameNameWithAccentsIsAllowed()
    {
        Assert.Equal(CategoryChangeError.Exists, (await Catalog().UpdateAsync(Uttt, "dibujo", "Libros", null, Ct)).Error);
        Assert.Equal(CategoryChangeError.None, (await Catalog().UpdateAsync(Uttt, "electronica", "ELECTRÓNICA", null, Ct)).Error);
    }

    [Fact]
    public async Task Update_WithoutChanges_TouchesNothing()
    {
        var change = await Catalog().UpdateAsync(Uttt, "libros", "Libros", active: true, Ct);

        Assert.Equal(CategoryChangeError.None, change.Error);
        Assert.Equal(change.Before, change.After);
        Assert.Empty(outputCache.EvictedTags);
    }

    [Fact]
    public async Task Update_OfAnUnknownCategory_IsNotFound()
    {
        Assert.Equal(CategoryChangeError.NotFound, (await Catalog().UpdateAsync(Uttt, "mochilas", "Mochilas", null, Ct)).Error);
        Assert.Equal(CategoryChangeError.NotFound, (await Catalog().UpdateAsync(AuthIds.New(), "libros", null, false, Ct)).Error);
    }

    [Fact]
    public async Task Deactivate_TheLastActive_IsRejected()
    {
        var catalog = Catalog();
        foreach (var slug in new[] { "calculadoras", "libros", "laptops", "laboratorio", "dibujo" })
        {
            Assert.Equal(CategoryChangeError.None, (await catalog.UpdateAsync(Uttt, slug, null, false, Ct)).Error);
        }

        Assert.Equal(CategoryChangeError.LastActive, (await catalog.UpdateAsync(Uttt, "electronica", null, false, Ct)).Error);
        Assert.Equal(CategoryChangeError.None, (await catalog.UpdateAsync(Uttt, "libros", null, true, Ct)).Error);
        Assert.Equal(["libros", "electronica"], (await repository.ListActiveAsync(Uttt, Ct)).Select(c => c.Slug));
    }

    [Fact]
    public async Task List_IncludesTheDeactivated()
    {
        await Catalog().UpdateAsync(Uttt, "laptops", null, false, Ct);

        var all = await Catalog().ListAsync(Uttt, Ct);

        Assert.Equal(6, all.Count);
        Assert.False(Assert.Single(all, c => c.Slug == "laptops").Active);
    }
}
