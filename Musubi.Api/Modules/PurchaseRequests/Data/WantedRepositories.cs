using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.PurchaseRequests.Data;

// Persistencia de M-05 (HU-13). Mongo e InMemory pasan la misma bateria (TestShared/PurchaseRequests).

/// <summary>Solicitudes de compra.</summary>
internal interface IWantedRepository
{
    /// <summary>Guarda una solicitud nueva.</summary>
    /// <param name="request">La solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task InsertAsync(WantedRequestDocument request, CancellationToken cancellationToken);

    /// <summary>Una solicitud por id, o <c>null</c>.</summary>
    /// <param name="id">Id.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La solicitud o <c>null</c>.</returns>
    Task<WantedRequestDocument?> FindByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Cuantas solicitudes activas y sin vencer tiene alguien (RN-12).</summary>
    /// <param name="requesterId">Quien busca.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El numero.</returns>
    Task<long> CountActiveAsync(string requesterId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Las solicitudes de alguien, de la mas nueva a la mas vieja ("Mis solicitudes").</summary>
    /// <param name="requesterId">Quien busca.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las solicitudes.</returns>
    Task<IReadOnlyList<WantedRequestDocument>> FindByRequesterAsync(string requesterId, int limit, CancellationToken cancellationToken);

    /// <summary>Borra las solicitudes de alguien (HU-46, R11).</summary>
    /// <param name="requesterId">Quien busca.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se borraron.</returns>
    Task<long> DeleteByRequesterAsync(string requesterId, CancellationToken cancellationToken);
}

/// <summary>Solicitudes en MongoDB (indices <c>ix_community_status_id</c> e <c>ix_requester_id</c>).</summary>
internal sealed class MongoWantedRepository(IMongoDatabase database) : IWantedRepository
{
    private readonly IMongoCollection<WantedRequestDocument> requests = database.GetCollection<WantedRequestDocument>(MongoSchema.WantedRequests);

    /// <inheritdoc />
    public Task InsertAsync(WantedRequestDocument request, CancellationToken cancellationToken) =>
        requests.InsertOneAsync(request, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<WantedRequestDocument?> FindByIdAsync(string id, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await requests.Find(r => r.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public Task<long> CountActiveAsync(string requesterId, DateTime now, CancellationToken cancellationToken) =>
        requests.CountDocumentsAsync(
            r => r.RequesterId == requesterId && r.Status == WantedStatuses.Active && r.ExpiresAt > now,
            cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WantedRequestDocument>> FindByRequesterAsync(string requesterId, int limit, CancellationToken cancellationToken) =>
        await requests.Find(r => r.RequesterId == requesterId)
            .SortByDescending(r => r.Id)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<long> DeleteByRequesterAsync(string requesterId, CancellationToken cancellationToken)
    {
        var result = await requests.DeleteManyAsync(r => r.RequesterId == requesterId, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount;
    }
}

/// <summary>Solicitudes en memoria (UseInMemoryDatabase=true).</summary>
internal sealed class InMemoryWantedRepository : IWantedRepository
{
    private readonly Lock gate = new();
    private readonly List<WantedRequestDocument> requests = [];

    /// <inheritdoc />
    public Task InsertAsync(WantedRequestDocument request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            requests.Add(request);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<WantedRequestDocument?> FindByIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(requests.Find(r => string.Equals(r.Id, id, StringComparison.Ordinal)));
        }
    }

    /// <inheritdoc />
    public Task<long> CountActiveAsync(string requesterId, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)requests.Count(r =>
                string.Equals(r.RequesterId, requesterId, StringComparison.Ordinal) && r.Status == WantedStatuses.Active && r.ExpiresAt > now));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WantedRequestDocument>> FindByRequesterAsync(string requesterId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<WantedRequestDocument>>(requests
                .Where(r => string.Equals(r.RequesterId, requesterId, StringComparison.Ordinal))
                .OrderByDescending(r => r.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByRequesterAsync(string requesterId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)requests.RemoveAll(r => string.Equals(r.RequesterId, requesterId, StringComparison.Ordinal)));
        }
    }
}
