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

namespace Musubi.Api.Modules.Matching.Data;

// Persistencia de M-06 (RF-25). Mongo* e InMemory* pasan la misma bateria (TestShared/Matching).

/// <summary>
/// Una coincidencia entre una solicitud y una publicacion (RF-25): si se aviso a quien busca (HU-29) o a quien vende
/// (HU-19) y cuando termino en pedido. Una por solicitud y publicacion; vence a los 90 dias (TTL).
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class MatchDocument
{
    /// <summary><c>{requestId}:{productId}</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Campus.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Solicitud.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string RequestId { get; init; } = string.Empty;

    /// <summary>Publicacion.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ProductId { get; init; } = string.Empty;

    /// <summary>Quien busca.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string RequesterId { get; init; } = string.Empty;

    /// <summary>Quien vende.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string SellerId { get; init; } = string.Empty;

    /// <summary>Si se aviso a quien busca (HU-29).</summary>
    public bool NotifiedRequester { get; init; }

    /// <summary>Si se aviso a quien vende (HU-19).</summary>
    public bool NotifiedSeller { get; init; }

    /// <summary>Cuando quien busca hizo un pedido de esa publicacion, o <c>null</c>.</summary>
    public DateTime? OrderedAt { get; init; }

    /// <summary>Cuando se encontro (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Cuando se borra sola (TTL).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Una coincidencia por registrar.</summary>
/// <param name="CommunityId">Campus.</param>
/// <param name="RequestId">Solicitud.</param>
/// <param name="ProductId">Publicacion.</param>
/// <param name="RequesterId">Quien busca.</param>
/// <param name="SellerId">Quien vende.</param>
/// <param name="NotifiedRequester">Si se le aviso a quien busca.</param>
/// <param name="NotifiedSeller">Si se le aviso a quien vende.</param>
internal sealed record MatchRecord(
    string CommunityId,
    string RequestId,
    string ProductId,
    string RequesterId,
    string SellerId,
    bool NotifiedRequester,
    bool NotifiedSeller)
{
    /// <summary>El id del documento.</summary>
    public string Key => $"{RequestId}:{ProductId}";
}

/// <summary>HU-42 (conexion): las coincidencias de un periodo.</summary>
/// <param name="Found">Las que se encontraron.</param>
/// <param name="Ordered">Las que terminaron en pedido.</param>
internal sealed record MatchUsage(long Found, long Ordered);

/// <summary>Coincidencias registradas.</summary>
internal interface IMatchRepository
{
    /// <summary>
    /// Registra una coincidencia: la crea o, si ya existia, solo enciende los avisos que ahora si se dieron (un aviso
    /// dado nunca se apaga).
    /// </summary>
    /// <param name="match">La coincidencia.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="expiresAt">Vencimiento.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task RecordAsync(MatchRecord match, DateTime now, DateTime expiresAt, CancellationToken cancellationToken);

    /// <summary>Marca las coincidencias de una persona con una publicacion como terminadas en pedido (la primera vez).</summary>
    /// <param name="requesterId">Quien compra.</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="at">Cuando (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se marcaron.</returns>
    Task<long> MarkOrderedAsync(string requesterId, string productId, DateTime at, CancellationToken cancellationToken);

    /// <summary>Las coincidencias de una solicitud.</summary>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las coincidencias.</returns>
    Task<IReadOnlyList<MatchDocument>> FindByRequestAsync(string requestId, CancellationToken cancellationToken);

    /// <summary>Borra las coincidencias donde la persona busca o vende (HU-46).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se borraron.</returns>
    Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>HU-42 (conexion): coincidencias encontradas en el periodo y cuantas terminaron en pedido.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="from">Inicio del periodo (UTC, inclusivo).</param>
    /// <param name="to">Fin del periodo (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los conteos.</returns>
    Task<MatchUsage> UsageAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken);
}

/// <summary>Coincidencias en MongoDB (coleccion <c>matches</c>, db/024).</summary>
internal sealed class MongoMatchRepository(IMongoDatabase database) : IMatchRepository
{
    private static readonly FilterDefinitionBuilder<MatchDocument> Filter = Builders<MatchDocument>.Filter;

    private readonly IMongoCollection<MatchDocument> matches = database.GetCollection<MatchDocument>(MongoSchema.Matches);

    /// <inheritdoc />
    public async Task RecordAsync(MatchRecord match, DateTime now, DateTime expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        var update = Builders<MatchDocument>.Update
            .SetOnInsert(m => m.CommunityId, match.CommunityId)
            .SetOnInsert(m => m.RequestId, match.RequestId)
            .SetOnInsert(m => m.ProductId, match.ProductId)
            .SetOnInsert(m => m.RequesterId, match.RequesterId)
            .SetOnInsert(m => m.SellerId, match.SellerId)
            .SetOnInsert(m => m.CreatedAt, now)
            .SetOnInsert(m => m.ExpiresAt, expiresAt)
            .SetOnInsert(m => m.SchemaVersion, 1);
        update = match.NotifiedRequester ? update.Set(m => m.NotifiedRequester, true) : update.SetOnInsert(m => m.NotifiedRequester, false);
        update = match.NotifiedSeller ? update.Set(m => m.NotifiedSeller, true) : update.SetOnInsert(m => m.NotifiedSeller, false);
        var filter = Filter.Eq(m => m.Id, match.Key);
        try
        {
            await matches.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Los dos avisos (HU-19 y HU-29) la crearon al mismo tiempo: ahora ya existe y solo se actualiza.
            await matches.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<long> MarkOrderedAsync(string requesterId, string productId, DateTime at, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(requesterId, out _) || !ObjectId.TryParse(productId, out _))
        {
            return 0;
        }

        var result = await matches.UpdateManyAsync(
            Filter.Eq(m => m.RequesterId, requesterId) & Filter.Eq(m => m.ProductId, productId) & Filter.Eq(m => m.OrderedAt, null),
            Builders<MatchDocument>.Update.Set(m => m.OrderedAt, at),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MatchDocument>> FindByRequestAsync(string requestId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(requestId, out _)
            ? await matches.Find(m => m.RequestId == requestId).ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

    /// <inheritdoc />
    public async Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? (await matches.DeleteManyAsync(Filter.Eq(m => m.RequesterId, userId) | Filter.Eq(m => m.SellerId, userId), cancellationToken).ConfigureAwait(false)).DeletedCount
            : 0;

    /// <inheritdoc />
    public async Task<MatchUsage> UsageAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(communityId, out _))
        {
            return new MatchUsage(0, 0);
        }

        var inPeriod = Filter.Eq(m => m.CommunityId, communityId) & Filter.Gte(m => m.CreatedAt, from) & Filter.Lt(m => m.CreatedAt, to);
        var found = await matches.CountDocumentsAsync(inPeriod, cancellationToken: cancellationToken).ConfigureAwait(false);
        var ordered = await matches.CountDocumentsAsync(inPeriod & Filter.Ne(m => m.OrderedAt, null), cancellationToken: cancellationToken).ConfigureAwait(false);
        return new MatchUsage(found, ordered);
    }
}

/// <summary>Coincidencias en memoria (Development y pruebas).</summary>
internal sealed class InMemoryMatchRepository : IMatchRepository
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, MatchDocument> matches = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task RecordAsync(MatchRecord match, DateTime now, DateTime expiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        lock (gate)
        {
            matches[match.Key] = matches.TryGetValue(match.Key, out var found)
                ? Copy(found, found.NotifiedRequester || match.NotifiedRequester, found.NotifiedSeller || match.NotifiedSeller, found.OrderedAt)
                : new MatchDocument
                {
                    Id = match.Key,
                    CommunityId = match.CommunityId,
                    RequestId = match.RequestId,
                    ProductId = match.ProductId,
                    RequesterId = match.RequesterId,
                    SellerId = match.SellerId,
                    NotifiedRequester = match.NotifiedRequester,
                    NotifiedSeller = match.NotifiedSeller,
                    CreatedAt = now,
                    ExpiresAt = expiresAt,
                };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> MarkOrderedAsync(string requesterId, string productId, DateTime at, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var pending = matches.Values
                .Where(m => m.RequesterId == requesterId && m.ProductId == productId && m.OrderedAt is null)
                .ToList();
            foreach (var match in pending)
            {
                matches[match.Id] = Copy(match, match.NotifiedRequester, match.NotifiedSeller, at);
            }

            return Task.FromResult((long)pending.Count);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MatchDocument>> FindByRequestAsync(string requestId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<MatchDocument>>([.. matches.Values.Where(m => m.RequestId == requestId)]);
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var gone = matches.Values.Where(m => m.RequesterId == userId || m.SellerId == userId).Select(m => m.Id).ToList();
            foreach (var key in gone)
            {
                matches.Remove(key);
            }

            return Task.FromResult((long)gone.Count);
        }
    }

    private static MatchDocument Copy(MatchDocument match, bool notifiedRequester, bool notifiedSeller, DateTime? orderedAt) => new()
    {
        Id = match.Id,
        CommunityId = match.CommunityId,
        RequestId = match.RequestId,
        ProductId = match.ProductId,
        RequesterId = match.RequesterId,
        SellerId = match.SellerId,
        NotifiedRequester = notifiedRequester,
        NotifiedSeller = notifiedSeller,
        OrderedAt = orderedAt,
        CreatedAt = match.CreatedAt,
        ExpiresAt = match.ExpiresAt,
        SchemaVersion = match.SchemaVersion,
    };

    /// <inheritdoc />
    public Task<MatchUsage> UsageAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var found = matches.Values.Where(m => m.CommunityId == communityId && m.CreatedAt >= from && m.CreatedAt < to).ToList();
            return Task.FromResult(new MatchUsage(found.Count, found.Count(m => m.OrderedAt is not null)));
        }
    }
}
