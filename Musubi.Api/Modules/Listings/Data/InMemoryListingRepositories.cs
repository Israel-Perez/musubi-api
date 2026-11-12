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
    public Task<IReadOnlyList<ProductDocument>> FindByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        lock (gate)
        {
            var wanted = ids.ToHashSet(StringComparer.Ordinal);
            return Task.FromResult<IReadOnlyList<ProductDocument>>(
                products.Where(p => wanted.Contains(p.Id)).ToList());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductDocument>> FindPageAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (gate)
        {
            // El tope de pagina se valida en el servicio; aqui se calcula en long para no desbordar.
            var skip = query.Page is { } page
                ? (int)Math.Min((long)(page - 1) * query.Limit, int.MaxValue)
                : 0;
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
    public Task<ProductDocument?> UpdateDetailsAsync(string id, string sellerId, ProductDetails details, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        return Task.FromResult(Replace(id, sellerId, p => Copy(p, details, p.Status, now, p.SoldAt)));
    }

    /// <inheritdoc />
    public Task<ProductDocument?> SetPausedAsync(string id, string sellerId, bool paused, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Replace(id, sellerId, p => Copy(
            p,
            new ProductDetails(p.Title, p.Description, p.CategorySlug, p.PriceCents, p.Condition, p.ConditionDetail),
            ResumedOrPaused(p, paused),
            now,
            p.SoldAt)));

    /// <inheritdoc />
    public Task<ProductDocument?> SetStockAsync(string id, string sellerId, int stock, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Replace(id, sellerId, p => WithStock(p, stock, StatusWithStock(p, stock), now, p.SoldAt)));

    /// <inheritdoc />
    public Task<ProductDocument?> DeleteAsync(string id, string sellerId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var index = products.FindIndex(p => Owned(p, id, sellerId));
            if (index < 0)
            {
                return Task.FromResult<ProductDocument?>(null);
            }

            var deleted = products[index];
            products.RemoveAt(index);
            return Task.FromResult<ProductDocument?>(deleted);
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
    public Task<IReadOnlyList<ProductDocument>> FindBySellerAsync(
        string sellerId,
        IReadOnlyList<string>? statuses,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var mine = products
                .Where(p => string.Equals(p.SellerId, sellerId, StringComparison.Ordinal)
                    && (statuses is null ? p.Status != ProductStatuses.Removed : statuses.Contains(p.Status))
                    && (beforeId is null || string.CompareOrdinal(p.Id, beforeId) < 0))
                .OrderByDescending(p => p.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<ProductDocument>>(mine);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, long>> CountBySellerAsync(string sellerId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyDictionary<string, long> counts = products
                .Where(p => string.Equals(p.SellerId, sellerId, StringComparison.Ordinal) && p.Status != ProductStatuses.Removed)
                .GroupBy(p => p.Status, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (long)g.Count(), StringComparer.Ordinal);
            return Task.FromResult(counts);
        }
    }

    /// <inheritdoc />
    public Task<ProductDocument?> MarkSoldAsync(string id, string sellerId, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Replace(
            id,
            sellerId,
            p => Copy(p, new ProductDetails(p.Title, p.Description, p.CategorySlug, p.PriceCents, p.Condition, p.ConditionDetail), ProductStatuses.Sold, now, now)));

    /// <inheritdoc />
    public Task<bool> TryReserveStockAsync(string id, int quantity, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(quantity > 0 && Change(
            id,
            p => p.Status == ProductStatuses.Active && p.Stock >= quantity,
            p => WithStock(p, p.Stock - quantity, p.Stock - quantity > 0 ? ProductStatuses.Active : ProductStatuses.SoldOut, now, p.SoldAt)));

    /// <inheritdoc />
    public Task<bool> ReleaseStockAsync(string id, int quantity, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(quantity > 0 && Change(
            id,
            _ => true,
            p => WithStock(p, p.Stock + quantity, p.Status == ProductStatuses.SoldOut ? ProductStatuses.Active : p.Status, now, p.SoldAt)));

    /// <inheritdoc />
    public Task<bool> MarkSoldIfEmptyAsync(string id, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Change(
            id,
            p => p.Status == ProductStatuses.SoldOut && p.Stock == 0,
            p => WithStock(p, 0, ProductStatuses.Sold, now, now)));

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

    private static bool Owned(ProductDocument p, string id, string sellerId) =>
        string.Equals(p.Id, id, StringComparison.Ordinal)
        && string.Equals(p.SellerId, sellerId, StringComparison.Ordinal)
        && p.Status != ProductStatuses.Removed;

    // HU-50: una pausada sigue pausada; si no, el estado sale de las piezas (igual que en Mongo).
    private static string StatusWithStock(ProductDocument p, int stock)
    {
        if (p.Status == ProductStatuses.Hidden)
        {
            return ProductStatuses.Hidden;
        }

        return stock > 0 ? ProductStatuses.Active : ProductStatuses.SoldOut;
    }

    private static string ResumedOrPaused(ProductDocument p, bool paused)
    {
        if (paused)
        {
            return ProductStatuses.Hidden;
        }

        return p.Stock > 0 ? ProductStatuses.Active : ProductStatuses.SoldOut;
    }

    // Los documentos son inmutables (init): un cambio reemplaza el documento, como lo devuelve Mongo.
    private static ProductDocument Copy(ProductDocument p, ProductDetails details, string status, DateTime now, DateTime? soldAt) => new()
    {
        Id = p.Id,
        CommunityId = p.CommunityId,
        SellerId = p.SellerId,
        Seller = p.Seller,
        Title = details.Title,
        Description = details.Description,
        CategorySlug = details.CategorySlug,
        PriceCents = details.PriceCents,
        Condition = details.Condition,
        ConditionDetail = details.ConditionDetail,
        Stock = p.Stock,
        Status = status,
        Photos = p.Photos,
        SchemaVersion = p.SchemaVersion,
        CreatedAt = p.CreatedAt,
        UpdatedAt = now,
        SoldAt = soldAt,
    };

    private static ProductDocument WithStock(ProductDocument p, int stock, string status, DateTime now, DateTime? soldAt) => new()
    {
        Id = p.Id,
        CommunityId = p.CommunityId,
        SellerId = p.SellerId,
        Seller = p.Seller,
        Title = p.Title,
        Description = p.Description,
        CategorySlug = p.CategorySlug,
        PriceCents = p.PriceCents,
        Condition = p.Condition,
        ConditionDetail = p.ConditionDetail,
        Stock = stock,
        Status = status,
        Photos = p.Photos,
        SchemaVersion = p.SchemaVersion,
        CreatedAt = p.CreatedAt,
        UpdatedAt = now,
        SoldAt = soldAt,
    };

    // HU-24: las piezas las cambia un pedido (no el vendedor), por eso no filtra por dueno.
    private bool Change(string id, Func<ProductDocument, bool> when, Func<ProductDocument, ProductDocument> change)
    {
        lock (gate)
        {
            var index = products.FindIndex(p => string.Equals(p.Id, id, StringComparison.Ordinal) && when(p));
            if (index < 0)
            {
                return false;
            }

            products[index] = change(products[index]);
            return true;
        }
    }

    private ProductDocument? Replace(string id, string sellerId, Func<ProductDocument, ProductDocument> change)
    {
        lock (gate)
        {
            // HU-08: una vendida no se edita, no se pausa y no se vende otra vez (igual que el filtro de Mongo).
            var index = products.FindIndex(p => Owned(p, id, sellerId) && p.Status != ProductStatuses.Sold);
            if (index < 0)
            {
                return null;
            }

            products[index] = change(products[index]);
            return products[index];
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

/// <summary>Favoritos en memoria (HU-53).</summary>
internal sealed class InMemoryFavoriteRepository : IFavoriteRepository
{
    private readonly Lock gate = new();
    private readonly List<FavoriteDocument> favorites = [];

    /// <inheritdoc />
    public Task<bool> InsertIfAbsentAsync(FavoriteDocument favorite, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(favorite);
        lock (gate)
        {
            // Misma regla que el indice unico ux_user_product.
            if (favorites.Exists(f => Is(f, favorite.UserId, favorite.ProductId)))
            {
                return Task.FromResult(false);
            }

            favorites.Add(favorite);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FavoriteDocument>> FindByUserAsync(string userId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<FavoriteDocument>>(favorites
                .Where(f => string.Equals(f.UserId, userId, StringComparison.Ordinal))
                .OrderByDescending(f => f.CreatedAt)
                .ThenByDescending(f => f.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string userId, string productId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(favorites.RemoveAll(f => Is(f, userId, productId)) > 0);
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)favorites.RemoveAll(f => string.Equals(f.UserId, userId, StringComparison.Ordinal)));
        }
    }

    private static bool Is(FavoriteDocument favorite, string userId, string productId) =>
        string.Equals(favorite.UserId, userId, StringComparison.Ordinal) && string.Equals(favorite.ProductId, productId, StringComparison.Ordinal);
}
