using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Orders;

/// <summary>
/// "Ya llegue" (HU-62, RF-31): en un pedido confirmado, una de las partes avisa que ya esta en el punto de entrega. La
/// otra recibe un push (<c>order_arrived</c>) y queda un aviso en el chat de los dos. Se avisa una vez por parte y desde
/// <see cref="ArrivalLead"/> antes de la hora acordada.
/// </summary>
internal sealed class OrderArrivalService(
    IOrderRepository orders,
    IMemberDirectory members,
    IConversationNotices notices,
    INotificationCenter center,
    TimeProvider time)
{
    /// <summary>Desde cuanto antes de la hora de entrega se puede avisar "Ya llegue".</summary>
    public static readonly TimeSpan ArrivalLead = TimeSpan.FromHours(2);

    /// <summary>Avisa "Ya llegue".</summary>
    /// <param name="userId">Quien llego (del token).</param>
    /// <param name="orderId">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido o el error.</returns>
    public async Task<Result<OrderResponse>> ArriveAsync(string userId, string orderId, CancellationToken cancellationToken)
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

        if (order.Status != OrderStatuses.Confirmed)
        {
            return order.Status == OrderStatuses.Pending ? OrdersErrors.NotConfirmed : OrdersErrors.Closed;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (now < order.Delivery.At - ArrivalLead)
        {
            return OrdersErrors.ArrivalTooEarly;
        }

        var arrived = await orders.MarkArrivedAsync(order.Id, userId, role, now, cancellationToken).ConfigureAwait(false);
        if (arrived is null)
        {
            // Ya lo habia avisado (o la otra parte cerro el pedido): se responde como esta, sin avisar otra vez.
            var current = await orders.FindByIdAsync(order.Id, cancellationToken).ConfigureAwait(false);
            return current is { Status: OrderStatuses.Confirmed }
                ? Result<OrderResponse>.Ok(OrderService.ToResponse(current, userId))
                : OrdersErrors.Closed;
        }

        var seller = role == OrderRoles.Seller;
        var name = ChatService.FirstName(seller ? arrived.SellerName : arrived.BuyerName);
        var place = arrived.Delivery.PointName;
        await notices.PostNoticeAsync(arrived.ConversationId, userId, $"{name} ya llegó a {place}", pushTitle: null, cancellationToken).ConfigureAwait(false);
        await center.NotifyAsync(
            new Notice(
                seller ? arrived.BuyerId : arrived.SellerId,
                NotificationTypes.OrderArrived,
                $"{name} ya llegó",
                $"Te espera en {place} para el pedido #{OrderNumbers.Display(arrived.Number)}.",
                NotificationTargets.Order,
                arrived.Id,
                NotificationCategories.Orders,
                $"order_arrived:{arrived.Id}:{role}"),
            cancellationToken).ConfigureAwait(false);
        return Result<OrderResponse>.Ok(OrderService.ToResponse(arrived, userId));
    }

    private static string? RoleOf(OrderDocument order, string userId)
    {
        if (string.Equals(order.BuyerId, userId, StringComparison.Ordinal))
        {
            return OrderRoles.Buyer;
        }

        return string.Equals(order.SellerId, userId, StringComparison.Ordinal) ? OrderRoles.Seller : null;
    }
}

/// <summary>
/// HU-62: una hora antes de la entrega de un pedido confirmado, las dos partes reciben "Hoy a la 1:00 p. m. en
/// Biblioteca con Valeria". Un pedido cancelado o completado ya no sale en la consulta, y la <c>dedupeKey</c> por pedido
/// y parte hace que el aviso salga una sola vez aunque la revision pase cada pocos minutos y corran dos instancias.
/// </summary>
internal sealed class DeliveryReminderJob(IOrderRepository orders, INotificationCenter center)
{
    /// <summary>Cuanto antes de la entrega se recuerda.</summary>
    public static readonly TimeSpan Before = TimeSpan.FromHours(1);

    /// <summary>Pedidos que se revisan por vuelta.</summary>
    public const int BatchSize = 200;

    /// <summary>Recuerda las entregas de la proxima hora.</summary>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos pedidos se revisaron.</returns>
    public async Task<int> RunAsync(DateTime now, CancellationToken cancellationToken)
    {
        var due = await orders.FindDeliveriesBetweenAsync(now, now + Before, BatchSize, cancellationToken).ConfigureAwait(false);
        foreach (var order in due)
        {
            await RemindAsync(order, order.BuyerId, OrderRoles.Buyer, order.SellerName, cancellationToken).ConfigureAwait(false);
            await RemindAsync(order, order.SellerId, OrderRoles.Seller, order.BuyerName, cancellationToken).ConfigureAwait(false);
        }

        return due.Count;
    }

    /// <summary>"Hoy a la 1:00 p. m. en Biblioteca con Valeria" (la hora del campus, como la ve la persona).</summary>
    /// <param name="order">El pedido.</param>
    /// <param name="otherName">El nombre de la otra parte.</param>
    /// <returns>El texto.</returns>
    internal static string Text(OrderDocument order, string otherName) =>
        $"Hoy a {CampusClock.Display(order.Delivery.At)} en {order.Delivery.PointName} con {ChatService.FirstName(otherName)}.";

    private Task RemindAsync(OrderDocument order, string userId, string role, string otherName, CancellationToken cancellationToken) =>
        center.NotifyAsync(
            new Notice(
                userId,
                NotificationTypes.DeliveryReminder,
                $"Tu entrega del pedido #{OrderNumbers.Display(order.Number)}",
                Text(order, otherName),
                NotificationTargets.Order,
                order.Id,
                NotificationCategories.Orders,
                $"delivery_reminder:{order.Id}:{role}"),
            cancellationToken);
}

/// <summary>La hora del campus como la escribe la app ("la 1:00 p. m.", "las 9:30 a. m."), sin depender de ICU.</summary>
internal static class CampusClock
{
    /// <summary>La hora con su articulo.</summary>
    /// <param name="utc">Fecha y hora (UTC).</param>
    /// <returns>El texto.</returns>
    public static string Display(DateTime utc)
    {
        var local = utc + WantedService.CampusOffset;
        var hour = local.Hour % 12 == 0 ? 12 : local.Hour % 12;
        var suffix = local.Hour < 12 ? "a. m." : "p. m.";
        var article = hour == 1 ? "la" : "las";
        return string.Create(CultureInfo.InvariantCulture, $"{article} {hour}:{local.Minute:D2} {suffix}");
    }
}

/// <summary>Corre <see cref="DeliveryReminderJob"/> cada <see cref="ReminderOptions.DeliveryReminderMinutes"/>.</summary>
internal sealed partial class DeliveryReminderWorker(
    DeliveryReminderJob job,
    IOptions<ReminderOptions> options,
    TimeProvider time,
    ILogger<DeliveryReminderWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.ReminderMinutes <= 0 || options.Value.DeliveryReminderMinutes <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.DeliveryReminderMinutes), time);
        do
        {
            try
            {
                await job.RunAsync(time.GetUtcNow().UtcDateTime, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException)
            {
                // Sin base, se intenta en la siguiente vuelta.
                LogSkipped(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recordatorio de entregas omitido ({Error})")]
    private static partial void LogSkipped(ILogger logger, string error);
}
