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

// Persistencia de HU-51. Mongo* e InMemory* pasan la misma bateria (TestShared/Listings/StockAlertRepositoryContract).

/// <summary>
/// "Avísame" en una publicacion agotada (HU-51): uno por persona y publicacion (<c>ux_user_product</c>), privado
/// (RNF-07). Se borra al avisar (decision de la usuaria: se usa una vez) o a los 90 dias (TTL).
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class StockAlertDocument
{
    /// <summary>Id.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Quien lo pidio.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; init; } = string.Empty;

    /// <summary>Su campus.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>La publicacion agotada.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ProductId { get; init; } = string.Empty;

    /// <summary>Cuando se pidio (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Cuando vence (TTL).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Avisos de disponibilidad.</summary>
internal interface IStockAlertRepository
{
    /// <summary>Lo guarda o, si ya lo tenia, le renueva el vencimiento.</summary>
    /// <param name="alert">El aviso nuevo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task SaveAsync(StockAlertDocument alert, CancellationToken cancellationToken);

    /// <summary>Si la persona tiene un aviso vigente de esa publicacion.</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si lo tiene.</returns>
    Task<bool> ExistsAsync(string userId, string productId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Cuantos avisos vigentes tiene una persona (el limite de 20).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El numero.</returns>
    Task<long> CountByUserAsync(string userId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Quita el aviso de una persona (idempotente).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si existia.</returns>
    Task<bool> DeleteAsync(string userId, string productId, CancellationToken cancellationToken);

    /// <summary>
    /// Toma (lee y borra) los avisos vigentes de una publicacion, sin los de <paramref name="excludedUsers"/>. Cada uno
    /// se borra de forma atomica: con dos instancias, un aviso solo lo toma una.
    /// </summary>
    /// <param name="productId">La publicacion.</param>
    /// <param name="excludedUsers">Quien vende y las personas bloqueadas (sus avisos se quedan).</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los avisos tomados.</returns>
    Task<IReadOnlyList<StockAlertDocument>> TakeAsync(string productId, IReadOnlyCollection<string> excludedUsers, DateTime now, int limit, CancellationToken cancellationToken);

    /// <summary>Borra todos los de una persona (HU-46).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se borraron.</returns>
    Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Avisos de disponibilidad en MongoDB (coleccion <c>stockAlerts</c>, db/029).</summary>
internal sealed class MongoStockAlertRepository(IMongoDatabase database) : IStockAlertRepository
{
    private static readonly FilterDefinitionBuilder<StockAlertDocument> Filter = Builders<StockAlertDocument>.Filter;

    private readonly IMongoCollection<StockAlertDocument> alerts = database.GetCollection<StockAlertDocument>(MongoSchema.StockAlerts);

    /// <inheritdoc />
    public async Task SaveAsync(StockAlertDocument alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);
        var filter = Filter.Eq(a => a.UserId, alert.UserId) & Filter.Eq(a => a.ProductId, alert.ProductId);
        var update = Builders<StockAlertDocument>.Update
            .Set(a => a.ExpiresAt, alert.ExpiresAt)
            .SetOnInsert(a => a.Id, alert.Id)
            .SetOnInsert(a => a.CommunityId, alert.CommunityId)
            .SetOnInsert(a => a.CreatedAt, alert.CreatedAt)
            .SetOnInsert(a => a.SchemaVersion, 1);
        try
        {
            await alerts.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // ux_user_product: otra peticion lo guardo al mismo tiempo; ahora ya existe y solo se renueva.
            await alerts.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string userId, string productId, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _) && ObjectId.TryParse(productId, out _)
        && await alerts.Find(Filter.Eq(a => a.UserId, userId) & Filter.Eq(a => a.ProductId, productId) & Filter.Gt(a => a.ExpiresAt, now))
            .AnyAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<long> CountByUserAsync(string userId, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? await alerts.CountDocumentsAsync(Filter.Eq(a => a.UserId, userId) & Filter.Gt(a => a.ExpiresAt, now), cancellationToken: cancellationToken).ConfigureAwait(false)
            : 0;

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string userId, string productId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _) && ObjectId.TryParse(productId, out _)
        && (await alerts.DeleteOneAsync(Filter.Eq(a => a.UserId, userId) & Filter.Eq(a => a.ProductId, productId), cancellationToken).ConfigureAwait(false)).DeletedCount > 0;

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockAlertDocument>> TakeAsync(
        string productId,
        IReadOnlyCollection<string> excludedUsers,
        DateTime now,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(productId, out _))
        {
            return [];
        }

        var filter = Filter.Eq(a => a.ProductId, productId) & Filter.Gt(a => a.ExpiresAt, now);
        var excluded = excludedUsers.Where(id => ObjectId.TryParse(id, out _)).ToList();
        if (excluded.Count > 0)
        {
            filter &= Filter.Nin(a => a.UserId, excluded);
        }

        var candidates = await alerts.Find(filter).SortBy(a => a.Id).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
        var taken = new List<StockAlertDocument>(candidates.Count);
        foreach (var candidate in candidates)
        {
            // Solo cuenta si esta instancia fue la que lo borro.
            if (await alerts.FindOneAndDeleteAsync(Filter.Eq(a => a.Id, candidate.Id), cancellationToken: cancellationToken).ConfigureAwait(false) is { } deleted)
            {
                taken.Add(deleted);
            }
        }

        return taken;
    }

    /// <inheritdoc />
    public async Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? (await alerts.DeleteManyAsync(a => a.UserId == userId, cancellationToken).ConfigureAwait(false)).DeletedCount
            : 0;
}

/// <summary>Avisos de disponibilidad en memoria (Development y pruebas).</summary>
internal sealed class InMemoryStockAlertRepository : IStockAlertRepository
{
    private readonly Lock gate = new();
    private readonly List<StockAlertDocument> alerts = [];

    /// <inheritdoc />
    public Task SaveAsync(StockAlertDocument alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);
        lock (gate)
        {
            var index = alerts.FindIndex(a => a.UserId == alert.UserId && a.ProductId == alert.ProductId);
            if (index < 0)
            {
                alerts.Add(alert);
            }
            else
            {
                var found = alerts[index];
                alerts[index] = new StockAlertDocument
                {
                    Id = found.Id,
                    UserId = found.UserId,
                    CommunityId = found.CommunityId,
                    ProductId = found.ProductId,
                    CreatedAt = found.CreatedAt,
                    ExpiresAt = alert.ExpiresAt,
                };
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string userId, string productId, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(alerts.Exists(a => a.UserId == userId && a.ProductId == productId && a.ExpiresAt > now));
        }
    }

    /// <inheritdoc />
    public Task<long> CountByUserAsync(string userId, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)alerts.Count(a => a.UserId == userId && a.ExpiresAt > now));
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string userId, string productId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(alerts.RemoveAll(a => a.UserId == userId && a.ProductId == productId) > 0);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StockAlertDocument>> TakeAsync(
        string productId,
        IReadOnlyCollection<string> excludedUsers,
        DateTime now,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            List<StockAlertDocument> taken =
            [
                .. alerts
                    .Where(a => a.ProductId == productId && a.ExpiresAt > now && !excludedUsers.Contains(a.UserId))
                    .OrderBy(a => a.Id, StringComparer.Ordinal)
                    .Take(limit),
            ];
            alerts.RemoveAll(taken.Contains);
            return Task.FromResult<IReadOnlyList<StockAlertDocument>>(taken);
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)alerts.RemoveAll(a => a.UserId == userId));
        }
    }
}
