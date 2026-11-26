using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Orders.Data;

namespace Musubi.Api.Modules.Orders;

/// <summary>Contrato publico de M-08 para M-14 (HU-42, operaciones).</summary>
internal interface IOrderIndicators
{
    /// <summary>Pedidos completados en el periodo, piezas vendidas y los que nacieron de una oferta.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="from">Inicio (UTC, inclusivo).</param>
    /// <param name="to">Fin (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los conteos.</returns>
    Task<OrderUsage> OperationsAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken);

    /// <summary>Cuantas de estas conversaciones tienen al menos un pedido.</summary>
    /// <param name="conversationIds">Las conversaciones.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas.</returns>
    Task<long> ConversationsWithOrdersAsync(IReadOnlyCollection<string> conversationIds, CancellationToken cancellationToken);

    /// <summary>Cuantas publicaciones se vendieron (pedido completado) despues de una fecha cada una.</summary>
    /// <param name="since">Publicacion y desde cuando cuenta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas.</returns>
    Task<long> SoldAfterAsync(IReadOnlyDictionary<string, DateTime> since, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre los pedidos.</summary>
internal sealed class OrderIndicators(IOrderRepository orders) : IOrderIndicators
{
    /// <inheritdoc />
    public Task<OrderUsage> OperationsAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        orders.UsageAsync(communityId, from, to, cancellationToken);

    /// <inheritdoc />
    public Task<long> ConversationsWithOrdersAsync(IReadOnlyCollection<string> conversationIds, CancellationToken cancellationToken) =>
        orders.CountConversationsWithOrdersAsync(conversationIds, cancellationToken);

    /// <inheritdoc />
    public async Task<long> SoldAfterAsync(IReadOnlyDictionary<string, DateTime> since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(since);
        if (since.Count == 0)
        {
            return 0;
        }

        var done = await orders.CompletedForProductsAsync([.. since.Keys], cancellationToken).ConfigureAwait(false);
        return done.Where(d => d.CompletedAt >= since[d.ProductId]).Select(d => d.ProductId).Distinct(StringComparer.Ordinal).Count();
    }
}
