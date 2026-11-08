using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Listings.Data;

/// <summary>Categorias en MongoDB (indice <c>ux_community_slug</c>).</summary>
internal sealed class MongoCategoryRepository(IMongoDatabase database) : ICategoryRepository
{
    private readonly IMongoCollection<CategoryDocument> categories = database.GetCollection<CategoryDocument>(MongoSchema.Categories);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken) =>
        await categories.Find(c => c.CommunityId == communityId && c.Active)
            .SortBy(c => c.Order)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
}

/// <summary>Publicaciones en MongoDB (indices <c>ix_community_status_id</c>, <c>ix_community_category_status_id</c> e <c>ix_seller_id</c>).</summary>
internal sealed class MongoProductRepository(IMongoDatabase database) : IProductRepository
{
    private static readonly FilterDefinitionBuilder<ProductDocument> Filter = Builders<ProductDocument>.Filter;

    private readonly IMongoCollection<ProductDocument> products = database.GetCollection<ProductDocument>(MongoSchema.Products);

    /// <inheritdoc />
    public Task InsertAsync(ProductDocument product, CancellationToken cancellationToken) =>
        products.InsertOneAsync(product, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<ProductDocument?> FindByIdAsync(string id, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await products.Find(p => p.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductDocument>> FindPageAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        var filter = Showcase(query);
        if (query.BeforeId is { } before)
        {
            filter &= Filter.Lt("_id", ObjectId.Parse(before));
        }

        return await products.Find(filter)
            .SortByDescending(p => p.Id)
            .Limit(query.Limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(ProductQuery query, CancellationToken cancellationToken) =>
        products.CountDocumentsAsync(Showcase(query), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<long> DeleteBySellerAsync(string sellerId, CancellationToken cancellationToken)
    {
        var result = await products.DeleteManyAsync(p => p.SellerId == sellerId, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductDocument>> FindBySellerAsync(string sellerId, int limit, CancellationToken cancellationToken) =>
        await products.Find(p => p.SellerId == sellerId && p.Status != ProductStatuses.Removed)
            .SortByDescending(p => p.Id)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> FindActivePricesAsync(string communityId, string categorySlug, int limit, CancellationToken cancellationToken)
    {
        var prices = await products
            .Find(p => p.CommunityId == communityId && p.CategorySlug == categorySlug && p.Status == ProductStatuses.Active)
            .SortByDescending(p => p.Id)
            .Limit(limit)
            .Project(p => p.PriceCents)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return prices.ToList();
    }

    private static FilterDefinition<ProductDocument> Showcase(ProductQuery query)
    {
        // HU-09: con busqueda solo salen las disponibles, aunque se pidan los agotados.
        var statuses = query.Search is null && query.IncludeSoldOut
            ? new[] { ProductStatuses.Active, ProductStatuses.SoldOut }
            : [ProductStatuses.Active];
        var filter = Filter.Eq(p => p.CommunityId, query.CommunityId) & Filter.In(p => p.Status, statuses);
        if (query.CategorySlug is { } category)
        {
            filter &= Filter.Eq(p => p.CategorySlug, category);
        }

        // HU-09: cada termino, ya normalizado, es un prefijo de una palabra de `searchWords` (indice
        // ix_community_status_search); un documento coincide si alguna palabra empieza igual (OR).
        return query.Search is { } search ? filter & SearchByPrefix(search) : filter;
    }

    private static FilterDefinition<ProductDocument> SearchByPrefix(string search)
    {
        var byTerm = search
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(SearchWord.Normalize)
            .Where(term => term.Length > 0)
            .Select(term => Filter.Regex(p => p.SearchWords, new BsonRegularExpression("^" + Regex.Escape(term), "i")))
            .ToList();
        return byTerm.Count == 0 ? Filter.Empty : Filter.Or(byTerm);
    }
}
