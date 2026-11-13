using System;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Orders.Data;

namespace Musubi.Api.Modules.Orders;

/// <summary>Lo que Reputacion (HU-27, HU-45) necesita de un pedido para calificarlo.</summary>
/// <param name="Id">El pedido.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="BuyerId">Quien compra.</param>
/// <param name="BuyerName">Su nombre (copia del pedido).</param>
/// <param name="SellerId">Quien vende.</param>
/// <param name="SellerName">Su nombre (copia del pedido).</param>
/// <param name="Completed">Si los dos confirmaron la entrega (RN-19).</param>
/// <param name="RateUntil">Hasta cuando se puede calificar, o <c>null</c> si no esta completado.</param>
/// <param name="BuyerRated">Quien compra ya califico.</param>
/// <param name="SellerRated">Quien vende ya califico.</param>
internal sealed record RateableOrder(
    string Id,
    string CommunityId,
    string BuyerId,
    string BuyerName,
    string SellerId,
    string SellerName,
    bool Completed,
    DateTime? RateUntil,
    bool BuyerRated,
    bool SellerRated);

/// <summary>Contrato de M-08 para M-13: leer un pedido y marcar que una parte ya lo califico (RN-24).</summary>
internal interface IOrderRatingGate
{
    /// <summary>El pedido, o <c>null</c> si no existe.</summary>
    /// <param name="orderId">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido.</returns>
    Task<RateableOrder?> FindAsync(string orderId, CancellationToken cancellationToken);

    /// <summary>Marca que una parte califico; <c>false</c> si ya lo habia hecho o el pedido ya no esta completado.</summary>
    /// <param name="orderId">El pedido.</param>
    /// <param name="userId">Quien califica.</param>
    /// <param name="role">Su parte: <c>buyer</c> o <c>seller</c>.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si se marco.</returns>
    Task<bool> MarkRatedAsync(string orderId, string userId, string role, DateTime now, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de pedidos.</summary>
internal sealed class OrderRatingGate(IOrderRepository orders) : IOrderRatingGate
{
    /// <inheritdoc />
    public async Task<RateableOrder?> FindAsync(string orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        return order is null
            ? null
            : new RateableOrder(
                order.Id,
                order.CommunityId,
                order.BuyerId,
                order.BuyerName,
                order.SellerId,
                order.SellerName,
                order.Status == OrderStatuses.Completed,
                OrderRatingWindow.Until(order),
                OrderRatingWindow.Rated(order, OrderRoles.Buyer),
                OrderRatingWindow.Rated(order, OrderRoles.Seller));
    }

    /// <inheritdoc />
    public Task<bool> MarkRatedAsync(string orderId, string userId, string role, DateTime now, CancellationToken cancellationToken) =>
        orders.MarkRatedAsync(orderId, userId, role, now, cancellationToken);
}
