using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.TestShared.Listings;

/// <summary>
/// HU-06 / HU-07: comportamiento esperado de los repositorios de publicaciones. Corre contra los fakes en memoria
/// (UnitTest) y contra MongoDB real con db/ aplicado (DatabaseTest). Cada prueba usa una comunidad o un
/// vendedor propio: la base de Mongo se comparte en la clase.
/// </summary>
public abstract class ListingRepositoryContract
{
    private static readonly DateTime Now = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    internal abstract ICategoryRepository Categories { get; }

    internal abstract IProductRepository Products { get; }

    /// <summary>Id de la UTTT en esa base (lo genera el script en Mongo).</summary>
    internal abstract Task<string> UtttIdAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ProductDocument NewProduct(
        string communityId,
        string sellerId,
        string category = "calculadoras",
        string status = ProductStatuses.Active,
        long price = 45_000,
        string? title = null,
        string? description = null,
        string condition = ProductConditions.Used,
        int? stock = null) => new()
    {
        Id = AuthIds.New(),
        CommunityId = communityId,
        SellerId = sellerId,
        Seller = new ProductSeller { Name = "Ana R." },
        Title = title ?? "Calculadora Casio fx-991ES Plus",
        Description = description ?? "La usé un semestre.",
        CategorySlug = category,
        PriceCents = price,
        Condition = condition,
        ConditionDetail = condition == ProductConditions.Used ? "likeNew" : null,
        Stock = stock ?? (status == ProductStatuses.SoldOut ? 0 : 1),
        Status = status,
        Photos = [$"media/products/{sellerId}/{Guid.NewGuid():N}.webp"],
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public async Task Categories_OfUttt_AreTheSixOfTheMockupInOrder()
    {
        var categories = await Categories.ListActiveAsync(await UtttIdAsync(), Ct);

        Assert.Equal(
            ["calculadoras", "libros", "laptops", "laboratorio", "dibujo", "electronica"],
            categories.Select(c => c.Slug));
        Assert.Equal("Electrónica", categories[^1].Name);
    }

    [Fact]
    public async Task Categories_OfAnotherCommunity_AreEmpty()
    {
        Assert.Empty(await Categories.ListActiveAsync(AuthIds.New(), Ct));
    }

    [Fact]
    public async Task Page_IsNewestFirst_AndTheCursorContinues()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var inserted = new List<ProductDocument>();
        for (var i = 0; i < 3; i++)
        {
            var product = NewProduct(community, seller);
            inserted.Add(product);
            await Products.InsertAsync(product, Ct);
        }

        var first = await Products.FindPageAsync(new ProductQuery(community, null, true, null, 2), Ct);
        var second = await Products.FindPageAsync(
            new ProductQuery(community, null, true, new ProductCursor(ProductSorts.Newest, null, first[^1].Id), 2), Ct);

        Assert.Equal([inserted[2].Id, inserted[1].Id], first.Select(p => p.Id));
        Assert.Equal([inserted[0].Id], second.Select(p => p.Id));
        Assert.Equal(3, await Products.CountAsync(new ProductQuery(community, null, true, null, 2), Ct));
    }

    [Fact]
    public async Task Page_RoundTripsAllFields()
    {
        var community = AuthIds.New();
        var product = NewProduct(community, AuthIds.New());
        await Products.InsertAsync(product, Ct);

        var read = Assert.Single(await Products.FindPageAsync(new ProductQuery(community, null, true, null, 10), Ct));

        Assert.Equal(product.SellerId, read.SellerId);
        Assert.Equal("Ana R.", read.Seller.Name);
        Assert.Equal(product.Title, read.Title);
        Assert.Equal(45_000, read.PriceCents);
        Assert.Equal("likeNew", read.ConditionDetail);
        Assert.Equal(product.Photos, read.Photos);
        Assert.Equal(Now, read.CreatedAt);
    }

    [Fact]
    public async Task Filters_ByCategory_AndHideSoldOut()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, seller, "libros"), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "calculadoras"), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "calculadoras", ProductStatuses.SoldOut), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "calculadoras", ProductStatuses.Hidden), Ct);

        var calculators = await Products.FindPageAsync(new ProductQuery(community, ["calculadoras"], true, null, 10), Ct);
        var available = await Products.FindPageAsync(new ProductQuery(community, ["calculadoras"], false, null, 10), Ct);

        Assert.Equal(2, calculators.Count);
        Assert.Single(available);
        // Sin agotados ni pausados: el libro y una calculadora.
        Assert.Equal(2, await Products.CountAsync(new ProductQuery(community, null, false, null, 10), Ct));
    }

    [Fact]
    public async Task SellerFilter_OnlyTheActiveListingsOfThatSeller_AndCountsThem()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var other = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, seller), Ct);
        await Products.InsertAsync(NewProduct(community, seller, title: "Calculadora graficadora"), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.SoldOut), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Hidden), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Sold), Ct);
        await Products.InsertAsync(NewProduct(community, other), Ct);

        var active = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, SellerId: seller), Ct);
        var withSoldOut = await Products.FindPageAsync(new ProductQuery(community, null, true, null, 10, SellerId: seller), Ct);

        Assert.Equal(2, active.Count);
        Assert.All(active, p => Assert.Equal(seller, p.SellerId));
        Assert.Equal(3, withSoldOut.Count);
        Assert.DoesNotContain(withSoldOut, p => p.Status is ProductStatuses.Hidden or ProductStatuses.Sold or ProductStatuses.Removed);
        Assert.Equal(2, await Products.CountAsync(new ProductQuery(community, null, false, null, 10, SellerId: seller), Ct));
        Assert.Empty(await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, SellerId: AuthIds.New()), Ct));
    }

    [Fact]
    public async Task ExcludedSellers_DoNotShow_InThePageOrTheCount()
    {
        var community = AuthIds.New();
        var blocked = AuthIds.New();
        var visible = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, blocked), Ct);
        await Products.InsertAsync(NewProduct(community, visible), Ct);
        await Products.InsertAsync(NewProduct(community, visible, title: "Calculadora graficadora"), Ct);

        var page = await Products.FindPageAsync(new ProductQuery(community, null, true, null, 10, ExcludeSellers: [blocked]), Ct);
        var searched = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "calculadora", ExcludeSellers: [blocked]), Ct);
        var nobody = await Products.FindPageAsync(new ProductQuery(community, null, true, null, 10, ExcludeSellers: []), Ct);

        Assert.All(page, p => Assert.Equal(visible, p.SellerId));
        Assert.Equal(2, page.Count);
        Assert.Equal(2, searched.Count);
        Assert.Equal(3, nobody.Count);
        Assert.Equal(2, await Products.CountAsync(new ProductQuery(community, null, true, null, 10, ExcludeSellers: [blocked]), Ct));
    }

    [Fact]
    public async Task Pages_ByNumber()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        for (var i = 0; i < 5; i++)
        {
            await Products.InsertAsync(NewProduct(community, seller, price: 10_000 + (i * 1_000)), Ct);
        }

        // HU-10: la pagina salta bloques del mismo tamano, sin repetir ids.
        var first = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 2, Page: 1), Ct);
        var second = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 2, Page: 2), Ct);
        var third = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 2, Page: 3), Ct);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Single(third);
        Assert.Empty(first.Select(p => p.Id).Intersect(second.Select(p => p.Id)));
    }

    [Fact]
    public async Task BySeller_ExcludesRemoved()
    {
        var seller = AuthIds.New();
        var community = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, seller), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Hidden), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Removed), Ct);

        var mine = await Products.FindBySellerAsync(seller, null, null, 10, Ct);

        Assert.Equal(2, mine.Count);
        Assert.DoesNotContain(mine, p => p.Status == ProductStatuses.Removed);
    }

    [Fact]
    public async Task ActivePrices_OnlyThatCategoryAndAvailable()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, seller, "libros", price: 30_000), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "libros", price: 42_000), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "libros", ProductStatuses.SoldOut, 99_000), Ct);
        await Products.InsertAsync(NewProduct(community, seller, "dibujo", price: 18_000), Ct);

        var prices = await Products.FindActivePricesAsync(community, "libros", 10, Ct);

        Assert.Equal([30_000L, 42_000L], prices.Order());
    }

    [Fact]
    public async Task DeleteBySeller_RemovesEveryStatusOnlyForThatSeller()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var other = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, seller), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.SoldOut), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Removed), Ct);
        await Products.InsertAsync(NewProduct(community, other), Ct);

        Assert.Equal(3, await Products.DeleteBySellerAsync(seller, Ct));
        Assert.Empty(await Products.FindBySellerAsync(seller, null, null, 10, Ct));
        Assert.Single(await Products.FindBySellerAsync(other, null, null, 10, Ct));
        Assert.Equal(0, await Products.DeleteBySellerAsync(seller, Ct));
    }

    [Fact]
    public async Task UpdateDetails_OnlyForTheOwner_AndNeverARemovedOne()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var product = NewProduct(community, seller);
        var removed = NewProduct(community, seller, status: ProductStatuses.Removed);
        await Products.InsertAsync(product, Ct);
        await Products.InsertAsync(removed, Ct);
        var later = Now.AddHours(1);
        // Pasa de usado a nuevo: el detalle se quita (el $jsonSchema no acepta null).
        var details = new ProductDetails("Calculadora Casio fx-570", "Como nueva.", "libros", 39_900, ProductConditions.New, null);

        Assert.Null(await Products.UpdateDetailsAsync(product.Id, AuthIds.New(), details, later, Ct));
        Assert.Null(await Products.UpdateDetailsAsync(removed.Id, seller, details, later, Ct));
        Assert.Null(await Products.UpdateDetailsAsync("no-es-un-id", seller, details, later, Ct));
        var updated = await Products.UpdateDetailsAsync(product.Id, seller, details, later, Ct);

        Assert.NotNull(updated);
        var read = await Products.FindByIdAsync(product.Id, Ct);
        Assert.Equal("Calculadora Casio fx-570", read!.Title);
        Assert.Equal("Como nueva.", read.Description);
        Assert.Equal("libros", read.CategorySlug);
        Assert.Equal(39_900, read.PriceCents);
        Assert.Equal(ProductConditions.New, read.Condition);
        Assert.Null(read.ConditionDetail);
        Assert.Equal(later, read.UpdatedAt);
        // Lo que no se edita se queda igual.
        Assert.Equal(product.Photos, read.Photos);
        Assert.Equal(product.Stock, read.Stock);
        Assert.Equal(ProductStatuses.Active, read.Status);
        Assert.Equal(Now, read.CreatedAt);
        Assert.Equal(removed.Title, (await Products.FindByIdAsync(removed.Id, Ct))!.Title);
    }

    [Fact]
    public async Task UpdateDetails_TheSearchFindsTheNewNameAndNotTheOldOne()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var product = NewProduct(community, seller, title: "Calculadora Casio", description: "La usé un semestre.");
        await Products.InsertAsync(product, Ct);
        var details = new ProductDetails("Libro de Biología", "Incluye ejercicios resueltos.", "libros", 30_000, ProductConditions.New, null);

        await Products.UpdateDetailsAsync(product.Id, seller, details, Now, Ct);

        // HU-09: editar recalcula las palabras de la busqueda (searchWords) en la misma operacion.
        Assert.Empty(await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "calculadora"), Ct));
        Assert.Equal(product.Id, Assert.Single(await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "biologia"), Ct)).Id);
        Assert.Equal(product.Id, Assert.Single(await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "ejercicios"), Ct)).Id);
    }

    [Fact]
    public async Task Pause_HidesIt_AndResumeDependsOnTheStock()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var withStock = NewProduct(community, seller);
        var soldOut = NewProduct(community, seller, status: ProductStatuses.SoldOut);
        await Products.InsertAsync(withStock, Ct);
        await Products.InsertAsync(soldOut, Ct);

        Assert.Equal(ProductStatuses.Hidden, (await Products.SetPausedAsync(withStock.Id, seller, true, Now, Ct))!.Status);
        Assert.Equal(ProductStatuses.Hidden, (await Products.SetPausedAsync(withStock.Id, seller, true, Now, Ct))!.Status);
        Assert.Equal(ProductStatuses.Hidden, (await Products.SetPausedAsync(soldOut.Id, seller, true, Now, Ct))!.Status);
        Assert.Empty(await Products.FindPageAsync(new ProductQuery(community, null, true, null, 10), Ct));

        Assert.Equal(ProductStatuses.Active, (await Products.SetPausedAsync(withStock.Id, seller, false, Now, Ct))!.Status);
        Assert.Equal(ProductStatuses.SoldOut, (await Products.SetPausedAsync(soldOut.Id, seller, false, Now, Ct))!.Status);
        Assert.Equal(2, await Products.CountAsync(new ProductQuery(community, null, true, null, 10), Ct));
    }

    [Fact]
    public async Task Pause_OfSomeoneElsesOrARemovedProduct_ChangesNothing()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var product = NewProduct(community, seller);
        var removed = NewProduct(community, seller, status: ProductStatuses.Removed);
        await Products.InsertAsync(product, Ct);
        await Products.InsertAsync(removed, Ct);

        Assert.Null(await Products.SetPausedAsync(product.Id, AuthIds.New(), true, Now, Ct));
        Assert.Null(await Products.SetPausedAsync(removed.Id, seller, false, Now, Ct));

        Assert.Equal(ProductStatuses.Active, (await Products.FindByIdAsync(product.Id, Ct))!.Status);
        Assert.Equal(ProductStatuses.Removed, (await Products.FindByIdAsync(removed.Id, Ct))!.Status);
    }

    [Fact]
    public async Task Delete_OnlyTheOwner_AndReturnsThePhotos()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var product = NewProduct(community, seller);
        var removed = NewProduct(community, seller, status: ProductStatuses.Removed);
        await Products.InsertAsync(product, Ct);
        await Products.InsertAsync(removed, Ct);

        Assert.Null(await Products.DeleteAsync(product.Id, AuthIds.New(), Ct));
        Assert.Null(await Products.DeleteAsync(removed.Id, seller, Ct));
        Assert.Null(await Products.DeleteAsync("no-es-un-id", seller, Ct));
        var deleted = await Products.DeleteAsync(product.Id, seller, Ct);

        Assert.Equal(product.Photos, deleted!.Photos);
        Assert.Null(await Products.FindByIdAsync(product.Id, Ct));
        Assert.NotNull(await Products.FindByIdAsync(removed.Id, Ct));
        Assert.Null(await Products.DeleteAsync(product.Id, seller, Ct));
    }

    // HU-09: busqueda por palabra clave (indice de texto en espanol, db/007). Las palabras de estos
    // casos coinciden igual en MongoDB y en el fake en memoria, que solo aproxima el stemming.

    [Fact]
    public async Task Search_MatchesTitleAndDescription_AndOnlyAvailable()
    {
        var community = AuthIds.New();
        var otherCampus = AuthIds.New();
        var seller = AuthIds.New();
        var calculator = NewProduct(community, seller);
        var book = NewProduct(community, seller, "libros", title: "Libro de Biología", description: "Incluye ejercicios resueltos.");
        var elsewhere = NewProduct(otherCampus, seller);
        await Products.InsertAsync(calculator, Ct);
        await Products.InsertAsync(book, Ct);
        await Products.InsertAsync(elsewhere, Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.SoldOut), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Hidden), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Removed), Ct);

        // HU-09: con busqueda solo salen las disponibles; HU-10: con IncludeSoldOut tambien las agotadas.
        var byTitle = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "calculadora"), Ct);
        var byDescription = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "ejercicios"), Ct);
        var ofAnotherCommunity = await Products.FindPageAsync(new ProductQuery(otherCampus, null, false, null, 10, "calculadora"), Ct);
        var withSoldOut = await Products.FindPageAsync(new ProductQuery(community, null, true, null, 10, "calculadora"), Ct);

        Assert.Equal(calculator.Id, Assert.Single(byTitle).Id);
        Assert.Equal(book.Id, Assert.Single(byDescription).Id);
        Assert.Equal(elsewhere.Id, Assert.Single(ofAnotherCommunity).Id);
        Assert.Equal(2, withSoldOut.Count);
    }

    [Fact]
    public async Task Search_IgnoresCaseAccentsAndPlurals()
    {
        var community = AuthIds.New();
        var product = NewProduct(community, AuthIds.New(), title: "Calculadora Casio", description: "La usé un semestre para Cálculo y Física.");
        await Products.InsertAsync(product, Ct);

        foreach (var query in new[] { "CALCULADORA", "calculadoras", "cálculo", "FISICA", "  Casio  " })
        {
            var found = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, query), Ct);

            Assert.Equal(product.Id, Assert.Single(found).Id);
        }
    }

    [Fact]
    public async Task Search_MatchesAnyWord_AndCombinesWithTheCategory()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var calculator = NewProduct(community, seller);
        var book = NewProduct(community, seller, "libros", title: "Libro de Biología", description: "Apuntes del semestre.");
        await Products.InsertAsync(calculator, Ct);
        await Products.InsertAsync(book, Ct);

        var either = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "casio apuntes"), Ct);
        var booksOnly = await Products.FindPageAsync(new ProductQuery(community, ["libros"], false, null, 10, "casio apuntes"), Ct);

        Assert.Equal(2, either.Count);
        Assert.Contains(either, p => p.Id == calculator.Id);
        Assert.Contains(either, p => p.Id == book.Id);
        Assert.Equal(book.Id, Assert.Single(booksOnly).Id);
        Assert.Equal(2, await Products.CountAsync(new ProductQuery(community, null, false, null, 10, "casio apuntes"), Ct));
    }

    [Fact]
    public async Task Search_PagesWithTheCursor_AndCountsEveryMatch()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var inserted = new List<ProductDocument>();
        for (var i = 0; i < 3; i++)
        {
            var product = NewProduct(community, seller, title: $"Calculadora Casio {i}");
            inserted.Add(product);
            await Products.InsertAsync(product, Ct);
        }

        await Products.InsertAsync(NewProduct(community, seller, "libros", title: "Libro de Biología"), Ct);

        var first = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 2, "calculadora"), Ct);
        var second = await Products.FindPageAsync(
            new ProductQuery(community, null, false, new ProductCursor(ProductSorts.Newest, null, first[^1].Id), 2, "calculadora"), Ct);

        Assert.Equal([inserted[2].Id, inserted[1].Id], first.Select(p => p.Id));
        Assert.Equal([inserted[0].Id], second.Select(p => p.Id));
        Assert.Equal(3, await Products.CountAsync(new ProductQuery(community, null, false, null, 2, "calculadora"), Ct));
    }

    [Fact]
    public async Task Search_WithoutMatches_ReturnsNothing()
    {
        var community = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, AuthIds.New()), Ct);

        Assert.Empty(await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "zzzzqqqq"), Ct));
        Assert.Equal(0, await Products.CountAsync(new ProductQuery(community, null, false, null, 10, "zzzzqqqq"), Ct));
    }

    [Fact]
    public async Task Search_MatchesWordPrefixes()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var calculator = NewProduct(community, seller, title: "Calculadora Casio fx-991ES");
        var laptop = NewProduct(community, seller, "laptops", title: "Laptop Lenovo ThinkPad");
        await Products.InsertAsync(calculator, Ct);
        await Products.InsertAsync(laptop, Ct);

        // HU-09: basta el comienzo de la palabra, con o sin mayusculas.
        foreach (var query in new[] { "calc", "calcula", "calculadora", "CASI", "lap", "leno" })
        {
            Assert.NotEmpty(await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, query), Ct));
        }

        var calculators = await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "calc"), Ct);
        Assert.Equal(calculator.Id, Assert.Single(calculators).Id);

        // El prefijo se ancla al inicio de la palabra.
        Assert.Empty(await Products.FindPageAsync(new ProductQuery(community, null, false, null, 10, "culadora"), Ct));
    }

    // HU-10: filtros de la hoja "Busqueda-filtros" (categorias en OR, rango de precio inclusivo,
    // condicion y orden) y cursor compuesto. Mongo e InMemory tienen que comportarse igual.

    [Fact]
    public async Task Filters_ByPriceRange_IsInclusive_AndAcceptsOnlyOneBound()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var cheap = NewProduct(community, seller, price: 10_000);
        var middle = NewProduct(community, seller, price: 20_000);
        var expensive = NewProduct(community, seller, price: 30_000);
        foreach (var product in new[] { cheap, middle, expensive })
        {
            await Products.InsertAsync(product, Ct);
        }

        // HU-10 R2 y R3: los dos extremos son inclusivos y cada uno se puede omitir.
        var bounded = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10) { MinPriceCents = 20_000, MaxPriceCents = 30_000 }, Ct);
        var fromMiddle = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10) { MinPriceCents = 20_000 }, Ct);
        var upToMiddle = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10) { MaxPriceCents = 20_000 }, Ct);

        Assert.Equal([expensive.Id, middle.Id], bounded.Select(p => p.Id));
        Assert.Equal([expensive.Id, middle.Id], fromMiddle.Select(p => p.Id));
        Assert.Equal([middle.Id, cheap.Id], upToMiddle.Select(p => p.Id));
        Assert.Equal(2, await Products.CountAsync(new ProductQuery(community, null, true, null, 10) { MinPriceCents = 20_000 }, Ct));
    }

    [Fact]
    public async Task Filters_BySeveralCategories_AndByCondition()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var usedBook = NewProduct(community, seller, "libros", price: 12_000);
        var newBook = NewProduct(community, seller, "libros", price: 15_000, condition: ProductConditions.New);
        var usedLaptop = NewProduct(community, seller, "laptops", price: 80_000);
        var usedCalculator = NewProduct(community, seller, "calculadoras", price: 18_000);
        foreach (var product in new[] { usedBook, newBook, usedLaptop, usedCalculator })
        {
            await Products.InsertAsync(product, Ct);
        }

        // HU-10 R1: las categorias se comparan como OR; R4: la condicion acota.
        var byCategories = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10) { CategorySlugs = ["libros", "laptops"] }, Ct);
        var usedOfThose = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10) { CategorySlugs = ["libros", "laptops"], Condition = ProductConditions.Used }, Ct);
        var onlyNew = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10) { Condition = ProductConditions.New }, Ct);

        Assert.Equal([usedLaptop.Id, newBook.Id, usedBook.Id], byCategories.Select(p => p.Id));
        Assert.Equal([usedLaptop.Id, usedBook.Id], usedOfThose.Select(p => p.Id));
        Assert.Equal(newBook.Id, Assert.Single(onlyNew).Id);
    }

    [Fact]
    public async Task Filters_CombinePriceCategoryConditionAndSearch()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var match = NewProduct(community, seller, "libros", price: 25_000, title: "Libro de Cálculo diferencial");
        var tooCheap = NewProduct(community, seller, "libros", price: 5_000, title: "Libro de Cálculo integral");
        var otherCategory = NewProduct(community, seller, "laptops", price: 25_000, title: "Libro de Cálculo en PDF");
        var otherCondition = NewProduct(community, seller, "libros", price: 25_000, title: "Libro de Cálculo nuevo", condition: ProductConditions.New);
        var soldOut = NewProduct(community, seller, "libros", ProductStatuses.SoldOut, 25_000, "Libro de Cálculo vendido");
        foreach (var product in new[] { match, tooCheap, otherCategory, otherCondition, soldOut })
        {
            await Products.InsertAsync(product, Ct);
        }

        var query = new ProductQuery(community, null, false, null, 10, "cálculo")
        {
            CategorySlugs = ["libros"],
            MinPriceCents = 20_000,
            MaxPriceCents = 30_000,
            Condition = ProductConditions.Used,
        };

        // HU-10 R6: los filtros se combinan con AND y, con q y sin agotados, solo salen las disponibles.
        Assert.Equal(match.Id, Assert.Single(await Products.FindPageAsync(query, Ct)).Id);
        Assert.Equal(1, await Products.CountAsync(query, Ct));
    }

    [Fact]
    public async Task Sorts_ByNewestAndOldest_WithCursor()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var oldest = NewProduct(community, seller);
        var middle = NewProduct(community, seller);
        var newest = NewProduct(community, seller);
        foreach (var product in new[] { oldest, middle, newest })
        {
            await Products.InsertAsync(product, Ct);
        }

        // HU-10 R7 y R8: newest es _id descendente y oldest ascendente; el cursor sigue con ese orden.
        var newestFirst = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 2) { Sort = ProductSorts.Newest }, Ct);
        var afterNewest = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 2)
            {
                Sort = ProductSorts.Newest,
                After = new ProductCursor(ProductSorts.Newest, null, middle.Id),
            }, Ct);
        var oldestFirst = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 2) { Sort = ProductSorts.Oldest }, Ct);
        var afterOldest = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 2)
            {
                Sort = ProductSorts.Oldest,
                After = new ProductCursor(ProductSorts.Oldest, null, middle.Id),
            }, Ct);

        Assert.Equal([newest.Id, middle.Id], newestFirst.Select(p => p.Id));
        Assert.Equal(oldest.Id, Assert.Single(afterNewest).Id);
        Assert.Equal([oldest.Id, middle.Id], oldestFirst.Select(p => p.Id));
        Assert.Equal(newest.Id, Assert.Single(afterOldest).Id);
    }

    [Fact]
    public async Task FindByIds_ReturnsOnlyTheRequestedProducts()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var first = NewProduct(community, seller);
        var second = NewProduct(community, seller);
        var other = NewProduct(community, seller);
        foreach (var product in new[] { first, second, other })
        {
            await Products.InsertAsync(product, Ct);
        }

        var found = await Products.FindByIdsAsync([first.Id, other.Id], Ct);

        Assert.Equal(
            new[] { first.Id, other.Id }.OrderBy(id => id, StringComparer.Ordinal),
            found.Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Empty(await Products.FindByIdsAsync([], Ct));
        Assert.Empty(await Products.FindByIdsAsync([AuthIds.New()], Ct));
    }

    [Fact]
    public async Task Sorts_ByPrice_WithTieBreakerAndCursor()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var firstTie = NewProduct(community, seller, price: 30_000);
        var middle = NewProduct(community, seller, price: 20_000);
        var lastTie = NewProduct(community, seller, price: 30_000);
        foreach (var product in new[] { firstTie, middle, lastTie })
        {
            await Products.InsertAsync(product, Ct);
        }

        // HU-10 R7: en empate desempata _id descendente; R9: el total ignora el cursor.
        var ascending = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10) { Sort = ProductSorts.PriceAsc }, Ct);
        var descending = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10) { Sort = ProductSorts.PriceDesc }, Ct);
        var nextAscending = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10)
            {
                Sort = ProductSorts.PriceAsc,
                After = new ProductCursor(ProductSorts.PriceAsc, lastTie.PriceCents, lastTie.Id),
            }, Ct);
        // El cursor descendente cruza el empate de 30 000: no debe saltar ni repetir firstTie.
        var nextDescending = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10)
            {
                Sort = ProductSorts.PriceDesc,
                After = new ProductCursor(ProductSorts.PriceDesc, firstTie.PriceCents, firstTie.Id),
            }, Ct);
        var afterLastDescending = await Products.FindPageAsync(
            new ProductQuery(community, null, true, null, 10)
            {
                Sort = ProductSorts.PriceDesc,
                After = new ProductCursor(ProductSorts.PriceDesc, lastTie.PriceCents, lastTie.Id),
            }, Ct);

        Assert.Equal([middle.Id, lastTie.Id, firstTie.Id], ascending.Select(p => p.Id));
        Assert.Equal([lastTie.Id, firstTie.Id, middle.Id], descending.Select(p => p.Id));
        Assert.Equal(firstTie.Id, Assert.Single(nextAscending).Id);
        Assert.Equal(middle.Id, Assert.Single(nextDescending).Id);
        Assert.Equal([firstTie.Id, middle.Id], afterLastDescending.Select(p => p.Id));
        Assert.Equal(3, await Products.CountAsync(
            new ProductQuery(community, null, true, null, 1)
            {
                Sort = ProductSorts.PriceAsc,
                After = new ProductCursor(ProductSorts.PriceAsc, lastTie.PriceCents, lastTie.Id),
            }, Ct));
    }

    // HU-08: "Mis productos" por pestana, conteos y "Marcar como vendido".

    [Fact]
    public async Task BySeller_ByStatus_PagesWithACursor()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var first = NewProduct(community, seller);
        var paused = NewProduct(community, seller, status: ProductStatuses.Hidden);
        var third = NewProduct(community, seller);
        await Products.InsertAsync(first, Ct);
        await Products.InsertAsync(paused, Ct);
        await Products.InsertAsync(third, Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.SoldOut), Ct);
        await Products.InsertAsync(NewProduct(community, AuthIds.New()), Ct);
        string[] activeTab = [ProductStatuses.Active, ProductStatuses.Hidden];

        var page1 = await Products.FindBySellerAsync(seller, activeTab, null, 2, Ct);
        var page2 = await Products.FindBySellerAsync(seller, activeTab, page1[^1].Id, 2, Ct);

        Assert.Equal([third.Id, paused.Id], page1.Select(p => p.Id));
        Assert.Equal([first.Id], page2.Select(p => p.Id));
        Assert.Single(await Products.FindBySellerAsync(seller, [ProductStatuses.SoldOut], null, 10, Ct));
    }

    [Fact]
    public async Task CountBySeller_GroupsByStatus_WithoutRemoved()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        await Products.InsertAsync(NewProduct(community, seller), Ct);
        await Products.InsertAsync(NewProduct(community, seller), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Hidden), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.SoldOut), Ct);
        await Products.InsertAsync(NewProduct(community, seller, status: ProductStatuses.Removed), Ct);
        await Products.InsertAsync(NewProduct(community, AuthIds.New()), Ct);

        var counts = await Products.CountBySellerAsync(seller, Ct);

        Assert.Equal(2, counts[ProductStatuses.Active]);
        Assert.Equal(1, counts[ProductStatuses.Hidden]);
        Assert.Equal(1, counts[ProductStatuses.SoldOut]);
        Assert.False(counts.ContainsKey(ProductStatuses.Removed));
        Assert.Empty(await Products.CountBySellerAsync(AuthIds.New(), Ct));
    }

    [Fact]
    public async Task MarkSold_OnlyTheOwner_KeepsTheFirstDate_AndThenItCannotBeEditedOrPaused()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var product = NewProduct(community, seller);
        await Products.InsertAsync(product, Ct);
        var details = new ProductDetails("Calculadora Casio fx-570", "Como nueva.", "libros", 39_900, ProductConditions.New, null);

        Assert.Null(await Products.MarkSoldAsync(product.Id, AuthIds.New(), Now, Ct));
        Assert.Null(await Products.MarkSoldAsync("no-es-un-id", seller, Now, Ct));
        var sold = await Products.MarkSoldAsync(product.Id, seller, Now, Ct);

        Assert.Equal((ProductStatuses.Sold, Now), (sold!.Status, sold.SoldAt));
        // Ya vendida: no cambia (la fecha de la primera venta se conserva), no se edita y no se pausa ni se reactiva.
        Assert.Null(await Products.MarkSoldAsync(product.Id, seller, Now.AddDays(1), Ct));
        Assert.Null(await Products.UpdateDetailsAsync(product.Id, seller, details, Now, Ct));
        Assert.Null(await Products.SetPausedAsync(product.Id, seller, true, Now, Ct));
        Assert.Null(await Products.SetPausedAsync(product.Id, seller, false, Now, Ct));
        var read = await Products.FindByIdAsync(product.Id, Ct);
        Assert.Equal((ProductStatuses.Sold, Now, product.Title), (read!.Status, read.SoldAt, read.Title));
        // Sale de la vitrina, sigue en "Mis productos" y se puede eliminar.
        Assert.Empty(await Products.FindPageAsync(new ProductQuery(community, null, true, null, 10), Ct));
        Assert.Single(await Products.FindBySellerAsync(seller, [ProductStatuses.Sold], null, 10, Ct));
        Assert.NotNull(await Products.DeleteAsync(product.Id, seller, Ct));
    }

    [Fact]
    public async Task Stock_ReserveRelease_AndSoldWhenEmpty()
    {
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var product = NewProduct(community, seller, stock: 3);
        var paused = NewProduct(community, seller, status: ProductStatuses.Hidden, stock: 3);
        await Products.InsertAsync(product, Ct);
        await Products.InsertAsync(paused, Ct);

        // HU-24 (RF-34): aparta solo si alcanzan y la publicacion esta disponible.
        Assert.True(await Products.TryReserveStockAsync(product.Id, 2, Now, Ct));
        Assert.False(await Products.TryReserveStockAsync(product.Id, 2, Now, Ct));
        Assert.False(await Products.TryReserveStockAsync(paused.Id, 1, Now, Ct));
        Assert.False(await Products.TryReserveStockAsync("no-es-un-id", 1, Now, Ct));
        Assert.False(await Products.MarkSoldIfEmptyAsync(product.Id, Now, Ct));
        var left = await Products.FindByIdAsync(product.Id, Ct);
        Assert.Equal((1, ProductStatuses.Active), (left!.Stock, left.Status));

        // La ultima pieza la deja agotada; al liberar vuelve a estar disponible (RN-20).
        Assert.True(await Products.TryReserveStockAsync(product.Id, 1, Now, Ct));
        var soldOut = await Products.FindByIdAsync(product.Id, Ct);
        Assert.Equal((0, ProductStatuses.SoldOut), (soldOut!.Stock, soldOut.Status));
        Assert.True(await Products.ReleaseStockAsync(product.Id, 1, Now, Ct));
        var back = await Products.FindByIdAsync(product.Id, Ct);
        Assert.Equal((1, ProductStatuses.Active), (back!.Stock, back.Status));

        // Agotada sin piezas: pasa a vendida con su fecha (RN-05); una pausada conserva su estado al liberar.
        Assert.True(await Products.TryReserveStockAsync(product.Id, 1, Now, Ct));
        Assert.True(await Products.MarkSoldIfEmptyAsync(product.Id, Now.AddHours(1), Ct));
        var sold = await Products.FindByIdAsync(product.Id, Ct);
        Assert.Equal((0, ProductStatuses.Sold, (DateTime?)Now.AddHours(1)), (sold!.Stock, sold.Status, sold.SoldAt));
        Assert.True(await Products.ReleaseStockAsync(paused.Id, 2, Now, Ct));
        var stillPaused = await Products.FindByIdAsync(paused.Id, Ct);
        Assert.Equal((5, ProductStatuses.Hidden), (stillPaused!.Stock, stillPaused.Status));
        Assert.False(await Products.ReleaseStockAsync(AuthIds.New(), 1, Now, Ct));
    }

    [Fact]
    public async Task SetStock_OnlyTheOwner_KeepsPaused_AndDecidesSoldOut()
    {
        // HU-50: "Cambiar piezas" y "Reponer"; el estado se decide en la misma escritura.
        var community = AuthIds.New();
        var seller = AuthIds.New();
        var product = NewProduct(community, seller, stock: 2);
        var paused = NewProduct(community, seller, status: ProductStatuses.Hidden, stock: 2);
        var sold = NewProduct(community, seller, status: ProductStatuses.Sold);
        var removed = NewProduct(community, seller, status: ProductStatuses.Removed);
        foreach (var p in new[] { product, paused, sold, removed })
        {
            await Products.InsertAsync(p, Ct);
        }

        Assert.Null(await Products.SetStockAsync(product.Id, AuthIds.New(), 5, Now, Ct));
        Assert.Null(await Products.SetStockAsync(sold.Id, seller, 5, Now, Ct));
        Assert.Null(await Products.SetStockAsync(removed.Id, seller, 5, Now, Ct));
        Assert.Null(await Products.SetStockAsync("no-es-un-id", seller, 5, Now, Ct));

        var empty = await Products.SetStockAsync(product.Id, seller, 0, Now, Ct);
        Assert.Equal((0, ProductStatuses.SoldOut), (empty!.Stock, empty.Status));
        var restocked = await Products.SetStockAsync(product.Id, seller, 4, Now.AddHours(1), Ct);
        Assert.Equal((4, ProductStatuses.Active, Now.AddHours(1)), (restocked!.Stock, restocked.Status, restocked.UpdatedAt));
        // Una pausada sigue pausada, con o sin piezas.
        Assert.Equal((0, ProductStatuses.Hidden), Pair(await Products.SetStockAsync(paused.Id, seller, 0, Now, Ct)));
        Assert.Equal((3, ProductStatuses.Hidden), Pair(await Products.SetStockAsync(paused.Id, seller, 3, Now, Ct)));
        Assert.Equal(1, (await Products.FindByIdAsync(sold.Id, Ct))!.Stock);
    }

    [Fact]
    public async Task ReleaseStock_NeverGoesAboveTheTenPieceLimit()
    {
        // SCRUM-97: con 10 piezas puestas a mano (HU-50), cancelar un pedido confirmado no las sube a 11.
        var product = NewProduct(AuthIds.New(), AuthIds.New(), stock: 10);
        await Products.InsertAsync(product, Ct);

        Assert.True(await Products.ReleaseStockAsync(product.Id, 1, Now, Ct));
        Assert.Equal(10, (await Products.FindByIdAsync(product.Id, Ct))!.Stock);

        Assert.True(await Products.TryReserveStockAsync(product.Id, 3, Now, Ct));
        Assert.True(await Products.ReleaseStockAsync(product.Id, 5, Now, Ct));
        Assert.Equal(10, (await Products.FindByIdAsync(product.Id, Ct))!.Stock);
    }

    private static (int Stock, string Status) Pair(ProductDocument? p) => (p!.Stock, p.Status);
}
