using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Orders;

/// <summary>
/// "Aceptar y actualizar mis pedidos" (HU-24, RF-31, RF-34, CU-18): quien vende acepta un pedido pendiente con el pago
/// elegido y se apartan sus piezas; cualquiera de las partes lo rechaza o cancela con un motivo de la lista y las
/// piezas regresan (RN-20); y se completa cuando los dos confirman la entrega (RN-19). Si la publicacion se queda sin
/// piezas, pasa a vendida (RN-05). Cada cambio avisa a la otra parte (HU-31).
/// NOTE (HU-52): el codigo de entrega cuenta como la confirmacion de quien vende.
/// NOTE (HU-55): quien vende valida la transferencia antes de aceptar.
/// </summary>
internal sealed class OrderLifecycleService(
    IOrderRepository orders,
    IProductStock stock,
    IProductDirectory products,
    IBlockDirectory blocks,
    IMemberDirectory members,
    INotificationCenter center,
    IProfileTrades trades,
    TimeProvider time)
{
    /// <summary>"Aceptar pedido" (RF-34): pasa a Confirmado y aparta las piezas.</summary>
    /// <param name="userId">Quien vende (del token).</param>
    /// <param name="orderId">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido o el error.</returns>
    public async Task<Result<OrderResponse>> AcceptAsync(string userId, string orderId, CancellationToken cancellationToken)
    {
        if (await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false) is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var order = await orders.FindByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || RoleOf(order, userId) is not { } role)
        {
            return OrdersErrors.NotFound;
        }

        if (role != OrderRoles.Seller)
        {
            return OrdersErrors.OnlySeller;
        }

        if (order.Status != OrderStatuses.Pending)
        {
            return OrdersErrors.NotPending;
        }

        if (order.Payment is null)
        {
            return OrdersErrors.PaymentMissing;
        }

        if (await blocks.BetweenAsync(order.BuyerId, order.SellerId, cancellationToken).ConfigureAwait(false))
        {
            return OrdersErrors.Blocked;
        }

        var productId = order.Item.ProductId;
        if (productId is not null && !await stock.TryReserveAsync(productId, order.Item.Quantity, cancellationToken).ConfigureAwait(false))
        {
            var product = await products.FindAsync(productId, cancellationToken).ConfigureAwait(false);
            return product is { Available: true } ? OrdersErrors.NotEnoughStock : OrdersErrors.ProductUnavailable;
        }

        var accepted = await orders.AcceptAsync(order.Id, userId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (accepted is null)
        {
            // R12: el pedido cambio entre la lectura y la escritura (lo cancelaron): las piezas regresan.
            if (productId is not null)
            {
                await stock.ReleaseAsync(productId, order.Item.Quantity, cancellationToken).ConfigureAwait(false);
            }

            return await CurrentErrorAsync(order.Id, OrdersErrors.NotPending, cancellationToken).ConfigureAwait(false);
        }

        await NotifyAsync(
            accepted.BuyerId,
            NotificationTypes.OrderConfirmed,
            "Tu pedido fue aceptado",
            $"{ChatService.FirstName(accepted.SellerName)} aceptó tu pedido #{OrderNumbers.Display(accepted.Number)}. Se ven en {accepted.Delivery.PointName}.",
            accepted,
            cancellationToken).ConfigureAwait(false);
        return Result<OrderResponse>.Ok(OrderService.ToResponse(accepted, userId));
    }

    /// <summary>"Rechazar" (quien vende, pendiente) o "Cancelar pedido" (cualquiera, antes de completar), con su motivo.</summary>
    /// <param name="userId">Quien cancela (del token).</param>
    /// <param name="orderId">El pedido.</param>
    /// <param name="body">El motivo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido o el error.</returns>
    public async Task<Result<OrderResponse>> CancelAsync(string userId, string orderId, CancelOrderRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false) is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var order = await orders.FindByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || RoleOf(order, userId) is not { } role)
        {
            return OrdersErrors.NotFound;
        }

        var allowed = role == OrderRoles.Seller ? CancelReasons.ForSeller : CancelReasons.ForBuyer;
        if (body.Reason is not { } reason || !allowed.Contains(reason, StringComparer.Ordinal))
        {
            return OrdersErrors.InvalidReason;
        }

        if (!OrderStatuses.Open.Contains(order.Status, StringComparer.Ordinal))
        {
            return OrdersErrors.Closed;
        }

        var cancelled = await orders.CancelAsync(order.Id, userId, role, reason, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (cancelled is null)
        {
            return OrdersErrors.Closed;
        }

        // La transicion anterior dice si las piezas estaban apartadas (venia de Confirmado).
        var previous = cancelled.History.Count >= 2 ? cancelled.History[^2].Status : OrderStatuses.Pending;
        if (previous == OrderStatuses.Confirmed && cancelled.Item.ProductId is { } productId)
        {
            await stock.ReleaseAsync(productId, cancelled.Item.Quantity, cancellationToken).ConfigureAwait(false);
        }

        var rejected = role == OrderRoles.Seller && previous == OrderStatuses.Pending;
        var who = ChatService.FirstName(role == OrderRoles.Seller ? cancelled.SellerName : cancelled.BuyerName);
        await NotifyAsync(
            OtherParty(cancelled, role),
            NotificationTypes.OrderCancelled,
            rejected ? "Tu pedido fue rechazado" : "Pedido cancelado",
            $"{who} {(rejected ? "rechazó" : "canceló")} el pedido #{OrderNumbers.Display(cancelled.Number)}: {CancelReasons.Label(reason)}.",
            cancelled,
            cancellationToken).ConfigureAwait(false);
        return Result<OrderResponse>.Ok(OrderService.ToResponse(cancelled, userId));
    }

    /// <summary>"Entregue" (quien vende) o "Recibi" (quien compra): con los dos, el pedido se completa (RN-19).</summary>
    /// <param name="userId">Quien confirma (del token).</param>
    /// <param name="orderId">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido o el error.</returns>
    public async Task<Result<OrderResponse>> ConfirmHandoverAsync(string userId, string orderId, CancellationToken cancellationToken)
    {
        if (await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false) is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var order = await orders.FindByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || RoleOf(order, userId) is not { } role)
        {
            return OrdersErrors.NotFound;
        }

        if (order.Status == OrderStatuses.Completed)
        {
            // Ya lo completaron los dos (un toque tardio): no cambia nada.
            return Result<OrderResponse>.Ok(OrderService.ToResponse(order, userId));
        }

        if (order.Status != OrderStatuses.Confirmed)
        {
            return order.Status == OrderStatuses.Pending ? OrdersErrors.NotConfirmed : OrdersErrors.Closed;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var confirmed = await orders.ConfirmHandoverAsync(order.Id, userId, role, now, cancellationToken).ConfigureAwait(false);
        if (confirmed is null)
        {
            return await CurrentErrorAsync(order.Id, OrdersErrors.Closed, cancellationToken).ConfigureAwait(false);
        }

        if (confirmed.Handover is { BuyerAt: not null, SellerAt: not null })
        {
            var completed = await orders.CompleteAsync(confirmed.Id, now, cancellationToken).ConfigureAwait(false);
            if (completed is null)
            {
                // La otra parte lo completo al mismo tiempo: ella avisa y marca la publicacion.
                var current = await orders.FindByIdAsync(order.Id, cancellationToken).ConfigureAwait(false) ?? confirmed;
                return Result<OrderResponse>.Ok(OrderService.ToResponse(current, userId));
            }

            await MarkSoldIfEmptyAsync(completed, cancellationToken).ConfigureAwait(false);
            // HU-26: "Ventas" y "Compras" del Perfil. CompleteAsync es atomico: se cuenta una sola vez.
            await trades.AddCompletedAsync(
                completed.SellerId,
                completed.SellerName,
                completed.BuyerId,
                completed.BuyerName,
                completed.CommunityId,
                now,
                cancellationToken).ConfigureAwait(false);
            await NotifyAsync(
                OtherParty(completed, role),
                NotificationTypes.OrderCompleted,
                "Pedido completado",
                $"El pedido #{OrderNumbers.Display(completed.Number)} quedó completado. ¡Gracias por usar Musubi!",
                completed,
                cancellationToken).ConfigureAwait(false);
            return Result<OrderResponse>.Ok(OrderService.ToResponse(completed, userId));
        }

        var before = role == OrderRoles.Seller ? order.Handover?.SellerAt : order.Handover?.BuyerAt;
        if (before is null)
        {
            var number = OrderNumbers.Display(confirmed.Number);
            var (title, text) = role == OrderRoles.Seller
                ? ("Confirma que lo recibiste", $"{ChatService.FirstName(confirmed.SellerName)} marcó el pedido #{number} como entregado. Confírmalo cuando lo tengas.")
                : ("Confirma la entrega", $"{ChatService.FirstName(confirmed.BuyerName)} confirmó que recibió el pedido #{number}. Confirma que lo entregaste.");
            await NotifyAsync(OtherParty(confirmed, role), NotificationTypes.OrderHandover, title, text, confirmed, cancellationToken).ConfigureAwait(false);
        }

        return Result<OrderResponse>.Ok(OrderService.ToResponse(confirmed, userId));
    }

    private static string? RoleOf(OrderDocument order, string userId)
    {
        if (string.Equals(order.BuyerId, userId, StringComparison.Ordinal))
        {
            return OrderRoles.Buyer;
        }

        return string.Equals(order.SellerId, userId, StringComparison.Ordinal) ? OrderRoles.Seller : null;
    }

    private static string OtherParty(OrderDocument order, string role) => role == OrderRoles.Seller ? order.BuyerId : order.SellerId;

    // R7 (RN-05): la ultima pieza entregada deja la publicacion vendida, si no hay otro pedido abierto que la espere.
    private async Task MarkSoldIfEmptyAsync(OrderDocument completed, CancellationToken cancellationToken)
    {
        if (completed.Item.ProductId is { } productId && !await orders.HasOpenForProductAsync(productId, cancellationToken).ConfigureAwait(false))
        {
            await stock.MarkSoldIfEmptyAsync(productId, cancellationToken).ConfigureAwait(false);
        }
    }

    // R12: el error que corresponde al estado que dejo la otra persona.
    private async Task<ApiError> CurrentErrorAsync(string orderId, ApiError fallback, CancellationToken cancellationToken)
    {
        var current = await orders.FindByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        return current?.Status switch
        {
            OrderStatuses.Cancelled or OrderStatuses.Completed => OrdersErrors.Closed,
            OrderStatuses.Confirmed => OrdersErrors.NotPending,
            _ => fallback,
        };
    }

    private Task NotifyAsync(string userId, string type, string title, string body, OrderDocument order, CancellationToken cancellationToken) =>
        center.NotifyAsync(
            new Notice(userId, type, title, body, NotificationTargets.Order, order.Id, NotificationCategories.Orders),
            cancellationToken);
}
