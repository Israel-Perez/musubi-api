using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth.Data;

namespace Musubi.Api.Modules.Listings.Data;

// Implementaciones en memoria para UseInMemoryDatabase=true (dotnet run sin MongoDB y suites WAF).
// TestShared/Listings verifica que se comporten igual que las de Mongo.

/// <summary>Categorias en memoria, con la misma semilla que db/002-publicaciones.js.</summary>
internal sealed class InMemoryCategoryRepository : ICategoryRepository
{
    /// <summary>Categorias de la maqueta (<c>Vender-3</c>), en su orden.</summary>
    public static readonly IReadOnlyList<(string Slug, string Name)> Seed =
    [
        ("calculadoras", "Calculadoras"),
        ("libros", "Libros"),
        ("laptops", "Laptops"),
        ("laboratorio", "Laboratorio"),
        ("dibujo", "Dibujo"),
        ("electronica", "Electrónica"),
    ];

    private readonly CategoryDocument[] categories = Seed
        .Select((c, i) => new CategoryDocument
        {
            Id = (i + 1).ToString("x24", System.Globalization.CultureInfo.InvariantCulture),
            CommunityId = InMemoryCommunityRepository.UtttId,
            Slug = c.Slug,
            Name = c.Name,
            Order = i + 1,
        })
        .ToArray();

    /// <inheritdoc />
    public Task<IReadOnlyList<CategoryDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CategoryDocument>>(
            categories.Where(c => c.Active && string.Equals(c.CommunityId, communityId, StringComparison.Ordinal)).OrderBy(c => c.Order).ToList());
}

/// <summary>Publicaciones en memoria (el id es un ObjectId nuevo: ordena por tiempo como en Mongo).</summary>
internal sealed class InMemoryProductRepository : IProductRepository
{
    private readonly Lock gate = new();
    private readonly List<ProductDocument> products = [];

    /// <inheritdoc />
    public Task InsertAsync(ProductDocument product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        lock (gate)
        {
            products.Add(product);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ProductDocument?> FindByIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(products.Find(p => string.Equals(p.Id, id, StringComparison.Ordinal)));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductDocument>> FindPageAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (gate)
        {
            var skip = query.Page is { } page ? (page - 1) * query.Limit : 0;
            var items = Sorted(Showcase(query), query.Sort)
                .Where(p => query.Page is not null || IsAfter(p, query))
                .Skip(skip)
                .Take(query.Limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<ProductDocument>>(items);
        }
    }

    /// <inheritdoc />
    public Task<long> CountAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (gate)
        {
            return Task.FromResult((long)Showcase(query).Count());
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteBySellerAsync(string sellerId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<long>(products.RemoveAll(p => string.Equals(p.SellerId, sellerId, StringComparison.Ordinal)));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductDocument>> FindBySellerAsync(string sellerId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var mine = products
                .Where(p => string.Equals(p.SellerId, sellerId, StringComparison.Ordinal) && p.Status != ProductStatuses.Removed)
                .OrderByDescending(p => p.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<ProductDocument>>(mine);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> FindActivePricesAsync(string communityId, string categorySlug, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var prices = products
                .Where(p => string.Equals(p.CommunityId, communityId, StringComparison.Ordinal)
                    && string.Equals(p.CategorySlug, categorySlug, StringComparison.Ordinal)
                    && p.Status == ProductStatuses.Active)
                .OrderByDescending(p => p.Id, StringComparer.Ordinal)
                .Take(limit)
                .Select(p => p.PriceCents)
                .ToList();
            return Task.FromResult<IReadOnlyList<long>>(prices);
        }
    }

    // Los ObjectId en hexadecimal (24 caracteres, minusculas) ordenan igual que en Mongo.
    private IEnumerable<ProductDocument> Showcase(ProductQuery query) => products.Where(p =>
        string.Equals(p.CommunityId, query.CommunityId, StringComparison.Ordinal)
        // HU-10: los agotados dependen de IncludeSoldOut; el servicio decide el valor por defecto.
        && (p.Status == ProductStatuses.Active || (query.IncludeSoldOut && p.Status == ProductStatuses.SoldOut))
        && ((query.CategorySlugs is null or { Count: 0 }) || query.CategorySlugs.Contains(p.CategorySlug, StringComparer.Ordinal))
        && (query.Condition is null || string.Equals(p.Condition, query.Condition, StringComparison.Ordinal))
        && (query.MinPriceCents is null || p.PriceCents >= query.MinPriceCents)
        && (query.MaxPriceCents is null || p.PriceCents <= query.MaxPriceCents)
        // HU-57: sin las publicaciones de quien bloqueaste o te bloqueo.
        && (query.ExcludeSellers is null || !query.ExcludeSellers.Contains(p.SellerId, StringComparer.Ordinal))
        && (query.Search is null || Matches(p, query.Search)));

    // Mismo orden que MongoDB (HU-10): en empate por precio, _id descendente.
    private static IEnumerable<ProductDocument> Sorted(IEnumerable<ProductDocument> products, string sort) => sort switch
    {
        ProductSorts.Oldest => products.OrderBy(p => p.Id, StringComparer.Ordinal),
        ProductSorts.PriceAsc => products
            .OrderBy(p => p.PriceCents)
            .ThenByDescending(p => p.Id, StringComparer.Ordinal),
        ProductSorts.PriceDesc => products
            .OrderByDescending(p => p.PriceCents)
            .ThenByDescending(p => p.Id, StringComparer.Ordinal),
        _ => products.OrderByDescending(p => p.Id, StringComparer.Ordinal),
    };

    private static bool IsAfter(ProductDocument product, ProductQuery query)
    {
        if (query.After is not { } cursor)
        {
            return true;
        }

        var price = cursor.PriceCents.GetValueOrDefault();
        return query.Sort switch
        {
            ProductSorts.Oldest => string.CompareOrdinal(product.Id, cursor.Id) > 0,
            ProductSorts.PriceAsc => product.PriceCents > price
                || (product.PriceCents == price && string.CompareOrdinal(product.Id, cursor.Id) < 0),
            ProductSorts.PriceDesc => product.PriceCents < price
                || (product.PriceCents == price && string.CompareOrdinal(product.Id, cursor.Id) < 0),
            _ => string.CompareOrdinal(product.Id, cursor.Id) < 0,
        };
    }

    // Prefijo de palabra como en MongoDB (HU-09): compara contra las palabras ya normalizadas del documento.
    private static bool Matches(ProductDocument product, string search)
    {
        var wanted = SearchWord.Words(search);
        return product.SearchWords.Any(word => wanted.Any(term => word.StartsWith(term, StringComparison.Ordinal)));
    }
}
