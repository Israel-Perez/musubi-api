using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.PurchaseRequests.Data;

// Persistencia de las ofertas (HU-16). Mongo e InMemory pasan la misma bateria (TestShared/PurchaseRequests).

/// <summary>Ofertas a las solicitudes.</summary>
internal interface IOfferRepository
{
    /// <summary>La oferta de alguien a una solicitud (RN-14: una por persona), o <c>null</c>.</summary>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="sellerId">Quien ofrece.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La oferta o <c>null</c>.</returns>
    Task<OfferDocument?> FindMineAsync(string requestId, string sellerId, CancellationToken cancellationToken);

    /// <summary>Guarda una oferta nueva si esa persona aun no tiene una en la solicitud (<c>ux_request_seller</c>).</summary>
    /// <param name="offer">La oferta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>false</c> si ya existia (dos envios a la vez).</returns>
    Task<bool> TryInsertAsync(OfferDocument offer, CancellationToken cancellationToken);

    /// <summary>Reemplaza una oferta existente (RN-14: se puede editar).</summary>
    /// <param name="offer">La oferta con el mismo id.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task ReplaceAsync(OfferDocument offer, CancellationToken cancellationToken);

    /// <summary>Una oferta por id, o <c>null</c>.</summary>
    /// <param name="id">Id.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La oferta o <c>null</c>.</returns>
    Task<OfferDocument?> FindByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Las ofertas de una solicitud, de la mas nueva a la mas vieja (HU-17).</summary>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las ofertas.</returns>
    Task<IReadOnlyList<OfferDocument>> FindByRequestAsync(string requestId, int limit, CancellationToken cancellationToken);

    /// <summary>Acepta o rechaza una oferta que sigue enviada, de forma atomica (HU-17).</summary>
    /// <param name="id">La oferta.</param>
    /// <param name="status"><see cref="OfferStatuses.Accepted"/> o <see cref="OfferStatuses.Rejected"/>.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>false</c> si ya no estaba enviada.</returns>
    Task<bool> TryDecideAsync(string id, string status, DateTime now, CancellationToken cancellationToken);

    /// <summary>Cuales de estas llaves de foto usa alguna oferta de ese vendedor (HU-07; indice <c>ix_seller</c>).</summary>
    /// <param name="sellerId">Quien ofrece.</param>
    /// <param name="photoKeys">Llaves a revisar.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las llaves en uso, sin repetir.</returns>
    Task<IReadOnlyCollection<string>> FindPhotoKeysInUseAsync(string sellerId, IReadOnlyCollection<string> photoKeys, CancellationToken cancellationToken);

    /// <summary>Borra las ofertas que hizo alguien y las que recibio en sus solicitudes (HU-46, R11).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se borraron.</returns>
    Task<long> DeleteByPersonAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Ofertas en MongoDB (indices <c>ux_request_seller</c>, <c>ix_request_id</c> e <c>ix_seller</c>).</summary>
internal sealed class MongoOfferRepository(IMongoDatabase database) : IOfferRepository
{
    private readonly IMongoCollection<OfferDocument> offers = database.GetCollection<OfferDocument>(MongoSchema.Offers);

    /// <inheritdoc />
    public async Task<OfferDocument?> FindMineAsync(string requestId, string sellerId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(requestId, out _) && ObjectId.TryParse(sellerId, out _)
            ? await offers.Find(o => o.RequestId == requestId && o.SellerId == sellerId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<bool> TryInsertAsync(OfferDocument offer, CancellationToken cancellationToken)
    {
        try
        {
            await offers.InsertOneAsync(offer, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // El indice unico resuelve dos envios simultaneos de la misma persona (RN-14).
            return false;
        }
    }

    /// <inheritdoc />
    public Task ReplaceAsync(OfferDocument offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return offers.ReplaceOneAsync(o => o.Id == offer.Id, offer, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OfferDocument?> FindByIdAsync(string id, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await offers.Find(o => o.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfferDocument>> FindByRequestAsync(string requestId, int limit, CancellationToken cancellationToken) =>
        ObjectId.TryParse(requestId, out _)
            ? await offers.Find(o => o.RequestId == requestId).SortByDescending(o => o.Id).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

    /// <inheritdoc />
    public async Task<bool> TryDecideAsync(string id, string status, DateTime now, CancellationToken cancellationToken)
    {
        var result = await offers.UpdateOneAsync(
            o => o.Id == id && o.Status == OfferStatuses.Sent,
            Builders<OfferDocument>.Update.Set(o => o.Status, status).Set(o => o.DecidedAt, now),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> FindPhotoKeysInUseAsync(string sellerId, IReadOnlyCollection<string> photoKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photoKeys);
        if (photoKeys.Count == 0 || !ObjectId.TryParse(sellerId, out _))
        {
            return [];
        }

        var filter = Builders<OfferDocument>.Filter.Eq(o => o.SellerId, sellerId)
            & Builders<OfferDocument>.Filter.In(o => o.PhotoKey, photoKeys);
        var keys = await offers.Find(filter).Project(o => o.PhotoKey).ToListAsync(cancellationToken).ConfigureAwait(false);
        return keys.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc />
    public async Task<long> DeleteByPersonAsync(string userId, CancellationToken cancellationToken)
    {
        var result = await offers.DeleteManyAsync(o => o.SellerId == userId || o.RequesterId == userId, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount;
    }
}

/// <summary>Ofertas en memoria (UseInMemoryDatabase=true).</summary>
internal sealed class InMemoryOfferRepository : IOfferRepository
{
    private readonly Lock gate = new();
    private readonly List<OfferDocument> offers = [];

    /// <inheritdoc />
    public Task<OfferDocument?> FindMineAsync(string requestId, string sellerId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(offers.Find(o =>
                string.Equals(o.RequestId, requestId, StringComparison.Ordinal) && string.Equals(o.SellerId, sellerId, StringComparison.Ordinal)));
        }
    }

    /// <inheritdoc />
    public Task<bool> TryInsertAsync(OfferDocument offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        lock (gate)
        {
            if (offers.Exists(o => string.Equals(o.RequestId, offer.RequestId, StringComparison.Ordinal)
                && string.Equals(o.SellerId, offer.SellerId, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            offers.Add(offer);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task ReplaceAsync(OfferDocument offer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(offer);
        lock (gate)
        {
            var i = offers.FindIndex(o => string.Equals(o.Id, offer.Id, StringComparison.Ordinal));
            if (i >= 0)
            {
                offers[i] = offer;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OfferDocument?> FindByIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(offers.Find(o => string.Equals(o.Id, id, StringComparison.Ordinal)));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OfferDocument>> FindByRequestAsync(string requestId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<OfferDocument>>(offers
                .Where(o => string.Equals(o.RequestId, requestId, StringComparison.Ordinal))
                .OrderByDescending(o => o.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task<bool> TryDecideAsync(string id, string status, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var i = offers.FindIndex(o => string.Equals(o.Id, id, StringComparison.Ordinal) && o.Status == OfferStatuses.Sent);
            if (i < 0)
            {
                return Task.FromResult(false);
            }

            var o = offers[i];
            offers[i] = new OfferDocument
            {
                Id = o.Id, CommunityId = o.CommunityId, RequestId = o.RequestId, RequesterId = o.RequesterId,
                SellerId = o.SellerId, SellerName = o.SellerName, Kind = o.Kind, ProductId = o.ProductId,
                Title = o.Title, PriceCents = o.PriceCents, Condition = o.Condition, ConditionDetail = o.ConditionDetail,
                PhotoKey = o.PhotoKey, Message = o.Message, Status = status, DecidedAt = now,
                CreatedAt = o.CreatedAt, UpdatedAt = o.UpdatedAt, SchemaVersion = o.SchemaVersion,
            };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<string>> FindPhotoKeysInUseAsync(string sellerId, IReadOnlyCollection<string> photoKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photoKeys);
        lock (gate)
        {
            IReadOnlyCollection<string> keys = offers
                .Where(o => string.Equals(o.SellerId, sellerId, StringComparison.Ordinal) && o.PhotoKey is not null && photoKeys.Contains(o.PhotoKey))
                .Select(o => o.PhotoKey!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(keys);
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByPersonAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)offers.RemoveAll(o =>
                string.Equals(o.SellerId, userId, StringComparison.Ordinal) || string.Equals(o.RequesterId, userId, StringComparison.Ordinal)));
        }
    }

    /// <summary>Todas (para las pruebas).</summary>
    /// <returns>Una copia.</returns>
    public IReadOnlyList<OfferDocument> All()
    {
        lock (gate)
        {
            return offers.ToList();
        }
    }
}
