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

/// <summary>Publicaciones en MongoDB (indices de vitrina, categoria, vendedor y precio; HU-06, HU-09, HU-10).</summary>
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
        if (query.Page is null && query.After is { } after)
        {
            filter &= CursorFilter(query.Sort, after);
        }

        var find = products.Find(filter).Sort(SortFor(query.Sort));
        if (query.Page is { } page)
        {
            // El tope de pagina se valida en el servicio; aqui se calcula en long para no desbordar.
            find = find.Skip((int)Math.Min((long)(page - 1) * query.Limit, int.MaxValue));
        }

        return await find
            .Limit(query.Limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<long> CountAsync(ProductQuery query, CancellationToken cancellationToken) =>
        products.CountDocumentsAsync(Showcase(query), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<ProductDocument?> UpdateDetailsAsync(string id, string sellerId, ProductDetails details, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var update = Builders<ProductDocument>.Update
            .Set(p => p.Title, details.Title)
            .Set(p => p.Description, details.Description)
            .Set(p => p.CategorySlug, details.CategorySlug)
            .Set(p => p.PriceCents, details.PriceCents)
            .Set(p => p.Condition, details.Condition)
            .Set(p => p.UpdatedAt, now)
            // HU-09: las palabras de la busqueda salen del nombre y la descripcion; sin esto, la busqueda
            // seguiria encontrando el nombre viejo. El documento las calcula al insertar, no al actualizar.
            .Set(p => p.SearchWords, SearchWord.Words($"{details.Title} {details.Description}"));
        // El $jsonSchema no acepta null: en un producto nuevo el detalle se quita.
        update = details.ConditionDetail is { } detail
            ? update.Set(p => p.ConditionDetail, detail)
            : update.Unset(p => p.ConditionDetail);
        return await products.FindOneAndUpdateAsync(
            Editable(id, sellerId),
            update,
            new FindOneAndUpdateOptions<ProductDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProductDocument?> SetPausedAsync(string id, string sellerId, bool paused, DateTime now, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        // Reactivar decide active/soldOut con las piezas del mismo documento (update con pipeline): sin leer antes.
        BsonValue status = paused
            ? ProductStatuses.Hidden
            : new BsonDocument("$cond", new BsonArray { new BsonDocument("$gt", new BsonArray { "$stock", 0 }), ProductStatuses.Active, ProductStatuses.SoldOut });
        var stage = new BsonDocument("$set", new BsonDocument { { "status", status }, { "updatedAt", new BsonDateTime(now) } });
        return await products.FindOneAndUpdateAsync(
            Editable(id, sellerId),
            new PipelineUpdateDefinition<ProductDocument>(new BsonDocumentStagePipelineDefinition<ProductDocument, ProductDocument>([stage])),
            new FindOneAndUpdateOptions<ProductDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProductDocument?> DeleteAsync(string id, string sellerId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await products.FindOneAndDeleteAsync(OwnedBy(id, sellerId), cancellationToken: cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<long> DeleteBySellerAsync(string sellerId, CancellationToken cancellationToken)
    {
        var result = await products.DeleteManyAsync(p => p.SellerId == sellerId, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductDocument>> FindBySellerAsync(
        string sellerId,
        IReadOnlyList<string>? statuses,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(p => p.SellerId, sellerId)
            & (statuses is null ? Filter.Ne(p => p.Status, ProductStatuses.Removed) : Filter.In(p => p.Status, statuses));
        if (beforeId is not null)
        {
            filter &= Filter.Lt("_id", ObjectId.Parse(beforeId));
        }

        return await products.Find(filter)
            .SortByDescending(p => p.Id)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, long>> CountBySellerAsync(string sellerId, CancellationToken cancellationToken)
    {
        // Un solo viaje: $match con ix_seller_id y $group por estado.
        var groups = await products.Aggregate()
            .Match(Filter.Eq(p => p.SellerId, sellerId) & Filter.Ne(p => p.Status, ProductStatuses.Removed))
            .Group(p => p.Status, g => new { Status = g.Key, Count = g.LongCount() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return groups.ToDictionary(g => g.Status, g => g.Count, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<ProductDocument?> MarkSoldAsync(string id, string sellerId, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await products.FindOneAndUpdateAsync(
                OwnedBy(id, sellerId) & Filter.Ne(p => p.Status, ProductStatuses.Sold),
                Builders<ProductDocument>.Update
                    .Set(p => p.Status, ProductStatuses.Sold)
                    .Set(p => p.SoldAt, now)
                    .Set(p => p.UpdatedAt, now),
                new FindOneAndUpdateOptions<ProductDocument> { ReturnDocument = ReturnDocument.After },
                cancellationToken).ConfigureAwait(false)
            : null;

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

    // HU-08: una vendida no se edita ni se pausa (reactivarla la "des-venderia").
    private static FilterDefinition<ProductDocument> Editable(string id, string sellerId) =>
        OwnedBy(id, sellerId) & Filter.Ne(p => p.Status, ProductStatuses.Sold);

    // El dueno en el filtro de cada escritura (OWASP API1): nunca se lee y despues se escribe.
    private static FilterDefinition<ProductDocument> OwnedBy(string id, string sellerId) =>
        Filter.Eq(p => p.Id, id) & Filter.Eq(p => p.SellerId, sellerId) & Filter.Ne(p => p.Status, ProductStatuses.Removed);

    private static FilterDefinition<ProductDocument> Showcase(ProductQuery query)
    {
        // HU-10: los agotados dependen de IncludeSoldOut; el servicio decide el valor por defecto.
        // NOTE (HU-10): con status $in y orden por precio, Mongo puede usar SORT_MERGE; confirmar el
        // plan con explain en CI queda pendiente.
        var statuses = query.IncludeSoldOut
            ? new[] { ProductStatuses.Active, ProductStatuses.SoldOut }
            : [ProductStatuses.Active];
        var filter = Filter.Eq(p => p.CommunityId, query.CommunityId) & Filter.In(p => p.Status, statuses);
        if (query.CategorySlugs is { Count: > 0 } categories)
        {
            filter &= Filter.In(p => p.CategorySlug, categories);
        }

        if (query.Condition is { } condition)
        {
            filter &= Filter.Eq(p => p.Condition, condition);
        }

        if (query.MinPriceCents is { } min)
        {
            filter &= Filter.Gte(p => p.PriceCents, min);
        }

        if (query.MaxPriceCents is { } max)
        {
            filter &= Filter.Lte(p => p.PriceCents, max);
        }

        // HU-57: sin las publicaciones de quien bloqueaste o te bloqueo.
        if (query.ExcludeSellers is { Count: > 0 } excluded)
        {
            filter &= Filter.Nin(p => p.SellerId, excluded);
        }

        // HU-09: cada termino, ya normalizado, es un prefijo de una palabra de `searchWords` (indice
        // ix_community_status_search); un documento coincide si alguna palabra empieza igual (OR).
        return query.Search is { } search ? filter & SearchByPrefix(search) : filter;
    }

    private static FilterDefinition<ProductDocument> CursorFilter(string sort, ProductCursor cursor)
    {
        var id = ObjectId.Parse(cursor.Id);
        var before = Filter.Lt("_id", id);
        var price = cursor.PriceCents.GetValueOrDefault();
        return sort switch
        {
            ProductSorts.Oldest => Filter.Gt("_id", id),
            // En empate, el orden sigue con _id descendente: la pagina siguiente tiene _id menor.
            ProductSorts.PriceAsc => Filter.Or(
                Filter.Gt(p => p.PriceCents, price),
                Filter.And(Filter.Eq(p => p.PriceCents, price), before)),
            ProductSorts.PriceDesc => Filter.Or(
                Filter.Lt(p => p.PriceCents, price),
                Filter.And(Filter.Eq(p => p.PriceCents, price), before)),
            _ => before,
        };
    }

    // NOTE (HU-10): con q, el indice de prefijo resuelve la busqueda pero el orden por precio puede
    // resolverse en memoria sobre las coincidencias; limit <= 50 acota el costo.
    private static SortDefinition<ProductDocument> SortFor(string sort) => sort switch
    {
        ProductSorts.Oldest => Builders<ProductDocument>.Sort.Ascending(p => p.Id),
        ProductSorts.PriceAsc => Builders<ProductDocument>.Sort
            .Ascending(p => p.PriceCents)
            .Descending(p => p.Id),
        ProductSorts.PriceDesc => Builders<ProductDocument>.Sort
            .Descending(p => p.PriceCents)
            .Descending(p => p.Id),
        _ => Builders<ProductDocument>.Sort.Descending(p => p.Id),
    };

    private static FilterDefinition<ProductDocument> SearchByPrefix(string search)
    {
        var byTerm = search
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(SearchWord.Normalize)
            .Where(term => term.Length > 0)
            .Select(term => Filter.Regex(p => p.SearchWords, new BsonRegularExpression("^" + Regex.Escape(term))))
            .ToList();
        return byTerm.Count == 0 ? Filter.Empty : Filter.Or(byTerm);
    }
}
