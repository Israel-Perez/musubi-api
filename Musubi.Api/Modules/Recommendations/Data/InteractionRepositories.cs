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

namespace Musubi.Api.Modules.Recommendations.Data;

// Contratos de persistencia de M-11 (HU-35). Mongo* (nube y local) e InMemory* (UseInMemoryDatabase=true) pasan la
// misma bateria de pruebas (TestShared/Recommendations).

/// <summary>
/// Una interaccion de una persona con una publicacion (RF-48): una por persona, publicacion y tipo; repetirla actualiza
/// la fecha y suma al conteo. Privada (RNF-07) y vence a los 90 dias (TTL).
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class InteractionDocument
{
    /// <summary>Id.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Quien.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; init; } = string.Empty;

    /// <summary>Campus.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Publicacion.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ProductId { get; init; } = string.Empty;

    /// <summary>Ver <c>InteractionKinds</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Cuantas veces.</summary>
    public int Count { get; init; } = 1;

    /// <summary>La ultima vez (UTC).</summary>
    public DateTime At { get; init; }

    /// <summary>Cuando se borra sola (TTL).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Interacciones de las personas con las publicaciones.</summary>
internal interface IInteractionRepository
{
    /// <summary>Registra una interaccion: la crea o actualiza su fecha, su vencimiento y su conteo.</summary>
    /// <param name="userId">Quien.</param>
    /// <param name="communityId">Campus.</param>
    /// <param name="productId">Publicacion.</param>
    /// <param name="kind">Tipo.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="expiresAt">Vencimiento (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task RecordAsync(string userId, string communityId, string productId, string kind, DateTime now, DateTime expiresAt, CancellationToken cancellationToken);

    /// <summary>Las de una persona, de la mas reciente a la mas vieja.</summary>
    /// <param name="userId">Quien.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las interacciones.</returns>
    Task<IReadOnlyList<InteractionDocument>> FindByUserAsync(string userId, int limit, CancellationToken cancellationToken);

    /// <summary>Las de un campus desde un momento, de la mas reciente a la mas vieja (entrenamiento del modelo).</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="since">Desde (UTC, incluido).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las interacciones.</returns>
    Task<IReadOnlyList<InteractionDocument>> FindByCommunityAsync(string communityId, DateTime since, int limit, CancellationToken cancellationToken);

    /// <summary>Borra las de una persona (HU-46).</summary>
    /// <param name="userId">Quien.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se borraron.</returns>
    Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Interacciones en MongoDB (coleccion <c>interactions</c>, db/022).</summary>
internal sealed class MongoInteractionRepository(IMongoDatabase database) : IInteractionRepository
{
    private static readonly FilterDefinitionBuilder<InteractionDocument> Filter = Builders<InteractionDocument>.Filter;

    private readonly IMongoCollection<InteractionDocument> interactions = database.GetCollection<InteractionDocument>(MongoSchema.Interactions);

    /// <inheritdoc />
    public async Task RecordAsync(string userId, string communityId, string productId, string kind, DateTime now, DateTime expiresAt, CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(i => i.UserId, userId) & Filter.Eq(i => i.ProductId, productId) & Filter.Eq(i => i.Kind, kind);
        var update = Builders<InteractionDocument>.Update
            .Set(i => i.CommunityId, communityId)
            .Set(i => i.At, now)
            .Set(i => i.ExpiresAt, expiresAt)
            .Inc(i => i.Count, 1)
            .SetOnInsert(i => i.SchemaVersion, 1);
        try
        {
            await interactions.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // ux_user_product_kind: otra peticion la creo al mismo tiempo; ahora ya existe y solo se actualiza.
            await interactions.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InteractionDocument>> FindByUserAsync(string userId, int limit, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? await interactions.Find(i => i.UserId == userId).SortByDescending(i => i.At).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

    /// <inheritdoc />
    public async Task<IReadOnlyList<InteractionDocument>> FindByCommunityAsync(string communityId, DateTime since, int limit, CancellationToken cancellationToken) =>
        ObjectId.TryParse(communityId, out _)
            ? await interactions.Find(Filter.Eq(i => i.CommunityId, communityId) & Filter.Gte(i => i.At, since))
                .SortByDescending(i => i.At)
                .Limit(limit)
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

    /// <inheritdoc />
    public async Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? (await interactions.DeleteManyAsync(i => i.UserId == userId, cancellationToken).ConfigureAwait(false)).DeletedCount
            : 0;
}

/// <summary>Interacciones en memoria (Development y pruebas).</summary>
internal sealed class InMemoryInteractionRepository : IInteractionRepository
{
    private readonly Lock gate = new();
    private readonly List<InteractionDocument> interactions = [];

    /// <inheritdoc />
    public Task RecordAsync(string userId, string communityId, string productId, string kind, DateTime now, DateTime expiresAt, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var index = interactions.FindIndex(i => i.UserId == userId && i.ProductId == productId && i.Kind == kind);
            var count = index < 0 ? 0 : interactions[index].Count;
            var updated = new InteractionDocument
            {
                Id = index < 0 ? MongoConventions.NewId() : interactions[index].Id,
                UserId = userId,
                CommunityId = communityId,
                ProductId = productId,
                Kind = kind,
                Count = count + 1,
                At = now,
                ExpiresAt = expiresAt,
            };
            if (index < 0)
            {
                interactions.Add(updated);
            }
            else
            {
                interactions[index] = updated;
            }

            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InteractionDocument>> FindByUserAsync(string userId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<InteractionDocument> found = [.. interactions.Where(i => i.UserId == userId).OrderByDescending(i => i.At).Take(limit)];
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InteractionDocument>> FindByCommunityAsync(string communityId, DateTime since, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<InteractionDocument> found = [.. interactions
                .Where(i => i.CommunityId == communityId && i.At >= since)
                .OrderByDescending(i => i.At)
                .Take(limit)];
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)interactions.RemoveAll(i => i.UserId == userId));
        }
    }
}
