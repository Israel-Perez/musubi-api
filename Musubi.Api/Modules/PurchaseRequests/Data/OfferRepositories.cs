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
