using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Listings.Data;

// Persistencia de HU-12. Mongo* e InMemory* pasan la misma bateria (TestShared/Listings/SavedSearchRepositoryContract).

/// <summary>
/// "Avísame cuando aparezca" (HU-12): una busqueda que no tuvo resultados, con sus filtros. Privada (RNF-07), una por
/// persona y busqueda (<see cref="Key"/>), y vence a los 30 dias (RN-34, TTL).
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class SavedSearchDocument
{
    /// <summary>Id.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Quien la guardo.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; init; } = string.Empty;

    /// <summary>Su campus.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Lo que escribio (para mostrarlo).</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Las palabras normalizadas que debe tener una publicacion (todas).</summary>
    public IReadOnlyList<string> Terms { get; init; } = [];

    /// <summary>Categorias (cualquiera de ellas), o vacio para todas.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Precio minimo, inclusivo.</summary>
    public long? MinPriceCents { get; init; }

    /// <summary>Precio maximo, inclusivo.</summary>
    public long? MaxPriceCents { get; init; }

    /// <summary><c>new</c>, <c>used</c> o <c>null</c> para las dos.</summary>
    public string? Condition { get; init; }

    /// <summary>Forma canonica de la busqueda: la misma busqueda guardada otra vez no se duplica.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Cuando se guardo (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Cuando vence (TTL).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Busquedas guardadas.</summary>
internal interface ISavedSearchRepository
{
    /// <summary>
    /// La guarda o, si ya tenia la misma (<see cref="SavedSearchDocument.Key"/>), solo le renueva el vencimiento.
    /// </summary>
    /// <param name="search">La busqueda nueva.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La busqueda guardada y si es nueva (o estaba vencida).</returns>
    Task<(SavedSearchDocument Search, bool Created)> SaveAsync(SavedSearchDocument search, DateTime now, CancellationToken cancellationToken);

    /// <summary>Las vigentes de una persona, de la mas nueva a la mas vieja.</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las busquedas.</returns>
    Task<IReadOnlyList<SavedSearchDocument>> FindByUserAsync(string userId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Las vigentes de un campus, sin las de <paramref name="excludedUsers"/> (HU-29: al publicar).</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="excludedUsers">Quien vende y las personas bloqueadas en cualquier sentido.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las busquedas.</returns>
    Task<IReadOnlyList<SavedSearchDocument>> FindActiveAsync(string communityId, DateTime now, IReadOnlyCollection<string> excludedUsers, int limit, CancellationToken cancellationToken);

    /// <summary>Quita una busqueda, solo de su dueno.</summary>
    /// <param name="userId">Quien la quita.</param>
    /// <param name="id">La busqueda.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si existia y era suya.</returns>
    Task<bool> DeleteAsync(string userId, string id, CancellationToken cancellationToken);

    /// <summary>Borra todas las de una persona (HU-46).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se borraron.</returns>
    Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Busquedas guardadas en MongoDB (coleccion <c>savedSearches</c>, db/025).</summary>
internal sealed class MongoSavedSearchRepository(IMongoDatabase database) : ISavedSearchRepository
{
    private static readonly FilterDefinitionBuilder<SavedSearchDocument> Filter = Builders<SavedSearchDocument>.Filter;

    private readonly IMongoCollection<SavedSearchDocument> searches = database.GetCollection<SavedSearchDocument>(MongoSchema.SavedSearches);

    /// <inheritdoc />
    public async Task<(SavedSearchDocument Search, bool Created)> SaveAsync(SavedSearchDocument search, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        var filter = Filter.Eq(s => s.UserId, search.UserId) & Filter.Eq(s => s.Key, search.Key);
        var update = Builders<SavedSearchDocument>.Update
            .Set(s => s.ExpiresAt, search.ExpiresAt)
            .SetOnInsert(s => s.Id, search.Id)
            .SetOnInsert(s => s.CommunityId, search.CommunityId)
            .SetOnInsert(s => s.Query, search.Query)
            .SetOnInsert(s => s.Terms, search.Terms)
            .SetOnInsert(s => s.Categories, search.Categories)
            .SetOnInsert(s => s.MinPriceCents, search.MinPriceCents)
            .SetOnInsert(s => s.MaxPriceCents, search.MaxPriceCents)
            .SetOnInsert(s => s.Condition, search.Condition)
            .SetOnInsert(s => s.CreatedAt, search.CreatedAt)
            .SetOnInsert(s => s.SchemaVersion, 1);
        var options = new FindOneAndUpdateOptions<SavedSearchDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.Before };
        SavedSearchDocument? before;
        try
        {
            before = await searches.FindOneAndUpdateAsync(filter, update, options, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoCommandException ex) when (ex.Code == 11000)
        {
            // ux_user_key: otra peticion la guardo al mismo tiempo; ahora ya existe y solo se renueva.
            before = await searches.FindOneAndUpdateAsync(filter, update, options, cancellationToken).ConfigureAwait(false);
        }

        var saved = await searches.Find(filter).FirstAsync(cancellationToken).ConfigureAwait(false);
        return (saved, before is null || before.ExpiresAt <= now);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedSearchDocument>> FindByUserAsync(string userId, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? await searches.Find(Filter.Eq(s => s.UserId, userId) & Filter.Gt(s => s.ExpiresAt, now))
                .SortByDescending(s => s.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedSearchDocument>> FindActiveAsync(
        string communityId,
        DateTime now,
        IReadOnlyCollection<string> excludedUsers,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(communityId, out _))
        {
            return [];
        }

        var filter = Filter.Eq(s => s.CommunityId, communityId) & Filter.Gt(s => s.ExpiresAt, now);
        var excluded = excludedUsers.Where(id => ObjectId.TryParse(id, out _)).ToList();
        if (excluded.Count > 0)
        {
            filter &= Filter.Nin(s => s.UserId, excluded);
        }

        return await searches.Find(filter).SortByDescending(s => s.Id).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string userId, string id, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _) && ObjectId.TryParse(id, out _)
        && (await searches.DeleteOneAsync(Filter.Eq(s => s.Id, id) & Filter.Eq(s => s.UserId, userId), cancellationToken).ConfigureAwait(false)).DeletedCount > 0;

    /// <inheritdoc />
    public async Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? (await searches.DeleteManyAsync(s => s.UserId == userId, cancellationToken).ConfigureAwait(false)).DeletedCount
            : 0;
}

/// <summary>Busquedas guardadas en memoria (Development y pruebas).</summary>
internal sealed class InMemorySavedSearchRepository : ISavedSearchRepository
{
    private readonly Lock gate = new();
    private readonly List<SavedSearchDocument> searches = [];

    /// <inheritdoc />
    public Task<(SavedSearchDocument Search, bool Created)> SaveAsync(SavedSearchDocument search, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);
        lock (gate)
        {
            var index = searches.FindIndex(s => s.UserId == search.UserId && s.Key == search.Key);
            if (index < 0)
            {
                searches.Add(search);
                return Task.FromResult((search, true));
            }

            var found = searches[index];
            var renewed = new SavedSearchDocument
            {
                Id = found.Id,
                UserId = found.UserId,
                CommunityId = found.CommunityId,
                Query = found.Query,
                Terms = found.Terms,
                Categories = found.Categories,
                MinPriceCents = found.MinPriceCents,
                MaxPriceCents = found.MaxPriceCents,
                Condition = found.Condition,
                Key = found.Key,
                CreatedAt = found.CreatedAt,
                ExpiresAt = search.ExpiresAt,
            };
            searches[index] = renewed;
            return Task.FromResult((renewed, found.ExpiresAt <= now));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SavedSearchDocument>> FindByUserAsync(string userId, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<SavedSearchDocument>>(
                [.. searches.Where(s => s.UserId == userId && s.ExpiresAt > now).OrderByDescending(s => s.Id, StringComparer.Ordinal)]);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SavedSearchDocument>> FindActiveAsync(
        string communityId,
        DateTime now,
        IReadOnlyCollection<string> excludedUsers,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<SavedSearchDocument>>(
                [.. searches
                    .Where(s => s.CommunityId == communityId && s.ExpiresAt > now && !excludedUsers.Contains(s.UserId))
                    .OrderByDescending(s => s.Id, StringComparer.Ordinal)
                    .Take(limit)]);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string userId, string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(searches.RemoveAll(s => s.Id == id && s.UserId == userId) > 0);
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)searches.RemoveAll(s => s.UserId == userId));
        }
    }
}
