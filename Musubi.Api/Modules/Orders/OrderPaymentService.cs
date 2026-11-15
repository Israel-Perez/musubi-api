using System;
using System.Text.RegularExpressions;
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
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Orders;

/// <summary>
/// Pago por transferencia (HU-55, D-01; maquetas <c>Pago-transferencia</c>, <c>Pago-validando</c> y <c>Pago-vendedor</c>):
/// quien compra ve la cuenta de quien vende y captura la referencia; quien vende la busca en su banco y confirma que le
/// llego (<see cref="OrderLifecycleService.AcceptAsync"/>) o dice que todavia no. Musubi no procesa el dinero.
/// </summary>
internal sealed partial class OrderPaymentService(
    IOrderRepository orders,
    IPayoutDirectory payouts,
    IMemberDirectory members,
    INotificationCenter center,
    TimeProvider time)
{
    /// <summary>"•••• 4821": la CLABE de quien vende como la ve ella misma.</summary>
    /// <param name="last4">Ultimos 4 digitos.</param>
    /// <returns>La CLABE enmascarada.</returns>
    public static string Masked(string last4) => "•••• " + last4;

    /// <summary>Los datos para transferir (quien compra) o para buscar el pago en el banco (quien vende).</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="orderId">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los datos o el error.</returns>
    public async Task<Result<TransferDetailsResponse>> TransferAsync(string userId, string orderId, CancellationToken cancellationToken)
    {
        var order = await orders.FindByIdAsync(orderId, cancellationToken).ConfigureAwait(false);
        if (order is null || RoleOf(order, userId) is not { } role)
        {
            return OrdersErrors.NotFound;
        }

        if (order.Payment is not { Method: PaymentMethods.Transfer } payment)
        {
            return OrdersErrors.NotTransfer;
        }

        if (await payouts.ForTransferAsync(order.SellerId, cancellationToken).ConfigureAwait(false) is not { } account)
        {
            return OrdersErrors.TransferUnavailable;
        }

        // RNF-05: la CLABE completa solo a quien tiene que transferir; quien vende ve la suya enmascarada.
        var clabe = role == OrderRoles.Buyer ? Clabe.Format(account.Clabe) : Masked(account.ClabeLast4);
        return Result<TransferDetailsResponse>.Ok(new TransferDetailsResponse(
            order.Item.TotalCents,
            account.BankName,
            account.HolderName,
            clabe,
            account.ClabeLast4,
            OrderNumbers.Display(order.Number),
            payment.Reference,
            payment.ReportedAt,
            payment.NotReceivedAt,
            payment.ValidatedAt));
    }

    /// <summary>"Enviar referencia a Diego": quien compra avisa que ya transfirio.</summary>
    /// <param name="userId">Quien compra (del token).</param>
    /// <param name="orderId">El pedido.</param>
    /// <param name="body">La referencia.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido o el error.</returns>
    public async Task<Result<OrderResponse>> ReportTransferAsync(string userId, string orderId, ReportTransferRequest body, CancellationToken cancellationToken)
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

        if (role != OrderRoles.Buyer)
        {
            return OrdersErrors.OnlyBuyer;
        }

        if (StateError(order) is { } stateError)
        {
            return stateError;
        }

        var reference = (body.Reference ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (!ReferencePattern().IsMatch(reference))
        {
            return OrdersErrors.InvalidReference;
        }

        if (!await payouts.CanReceiveAsync(order.SellerId, cancellationToken).ConfigureAwait(false))
        {
            return OrdersErrors.TransferUnavailable;
        }

        var reported = await orders.ReportTransferAsync(order.Id, userId, reference, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (reported is null)
        {
            return OrdersErrors.NotPending;
        }

        var number = OrderNumbers.Display(reported.Number);
        await NotifyAsync(
            reported.SellerId,
            NotificationTypes.TransferReported,
            $"{ChatService.FirstName(reported.BuyerName)} dice que ya te transfirió",
            $"Pedido #{number} · {Money(reported.Item.TotalCents)}. Búscalo en tu banco y confirma que te llegó.",
            reported,
            null,
            cancellationToken).ConfigureAwait(false);
        return Result<OrderResponse>.Ok(OrderService.ToResponse(reported, userId));
    }

    /// <summary>"Todavia no me llega": quien vende aun no ve la transferencia; se le avisa a quien compra.</summary>
    /// <param name="userId">Quien vende (del token).</param>
    /// <param name="orderId">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido o el error.</returns>
    public async Task<Result<OrderResponse>> NotReceivedAsync(string userId, string orderId, CancellationToken cancellationToken)
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

        if (StateError(order) is { } stateError)
        {
            return stateError;
        }

        if (order.Payment?.ReportedAt is null)
        {
            return OrdersErrors.TransferNotReported;
        }

        var marked = await orders.MarkTransferNotReceivedAsync(order.Id, userId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (marked is null)
        {
            return OrdersErrors.NotPending;
        }

        await NotifyAsync(
            marked.BuyerId,
            NotificationTypes.TransferNotReceived,
            $"{ChatService.FirstName(marked.SellerName)} todavía no ve tu transferencia",
            $"Revisa la referencia del pedido #{OrderNumbers.Display(marked.Number)} o escríbele con tu comprobante.",
            marked,
            null,
            cancellationToken).ConfigureAwait(false);
        return Result<OrderResponse>.Ok(OrderService.ToResponse(marked, userId));
    }

    /// <summary>"$500" o "$1,250.50", como en la app.</summary>
    /// <param name="cents">Centavos.</param>
    /// <returns>El monto.</returns>
    internal static string Money(long cents) =>
        "$" + (cents % 100 == 0
            ? (cents / 100).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            : (cents / 100m).ToString("N2", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Avisa de un pedido (HU-31): con <paramref name="dedupeKey"/>, una sola vez.</summary>
    /// <param name="userId">A quien.</param>
    /// <param name="type">Tipo.</param>
    /// <param name="title">Titulo.</param>
    /// <param name="body">Texto.</param>
    /// <param name="order">El pedido (lo abre el aviso).</param>
    /// <param name="dedupeKey">Para no repetir, o <c>null</c>.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    internal Task NotifyAsync(string userId, string type, string title, string body, OrderDocument order, string? dedupeKey, CancellationToken cancellationToken) =>
        center.NotifyAsync(
            new Notice(userId, type, title, body, NotificationTargets.Order, order.Id, NotificationCategories.Orders, dedupeKey),
            cancellationToken);

    private static string? RoleOf(OrderDocument order, string userId)
    {
        if (string.Equals(order.BuyerId, userId, StringComparison.Ordinal))
        {
            return OrderRoles.Buyer;
        }

        return string.Equals(order.SellerId, userId, StringComparison.Ordinal) ? OrderRoles.Seller : null;
    }

    // Pendiente y por transferencia: lo demas ya no se puede reportar ni rechazar.
    private static ApiError? StateError(OrderDocument order)
    {
        if (order.Status != OrderStatuses.Pending)
        {
            return order.Status == OrderStatuses.Confirmed ? OrdersErrors.NotPending : OrdersErrors.Closed;
        }

        return order.Payment?.Method == PaymentMethods.Transfer ? null : OrdersErrors.NotTransfer;
    }

    // Referencia o clave de rastreo: letras y numeros (sin espacios), de 4 a 30.
    [GeneratedRegex("^[A-Z0-9-]{4,30}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex ReferencePattern();
}

/// <summary>
/// HU-55 (decision de la usuaria del 15-Nov-2026): si quien compra ya aviso "Ya transferi" y a las 24 h quien vende no
/// lo confirma, se le recuerda una vez. El pedido sigue pendiente: no se cancela solo, porque el dinero ya esta con
/// quien vende.
/// </summary>
internal sealed class TransferReminderJob(IOrderRepository orders, OrderPaymentService payments)
{
    /// <summary>Horas despues del aviso.</summary>
    public static readonly TimeSpan After = TimeSpan.FromHours(24);

    /// <summary>Pedidos que se revisan por vuelta.</summary>
    public const int BatchSize = 200;

    /// <summary>Recuerda a quien vende las transferencias que lleva mas de 24 h sin confirmar.</summary>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se revisaron.</returns>
    public async Task<int> RunAsync(DateTime now, CancellationToken cancellationToken)
    {
        var due = await orders.FindTransfersToRemindAsync(now - After, BatchSize, cancellationToken).ConfigureAwait(false);
        foreach (var order in due)
        {
            // dedupeKey: una sola vez por pedido, aunque el proceso pase cada hora y corran dos instancias.
            await payments.NotifyAsync(
                order.SellerId,
                NotificationTypes.TransferReminder,
                $"Confirma la transferencia de {ChatService.FirstName(order.BuyerName)}",
                $"Te avisó hace más de un día que pagó el pedido #{OrderNumbers.Display(order.Number)}. Revisa tu banco.",
                order,
                $"transfer_reminder:{order.Id}",
                cancellationToken).ConfigureAwait(false);
        }

        return due.Count;
    }
}

/// <summary>Corre <see cref="TransferReminderJob"/> cada <see cref="ReminderOptions.ReminderMinutes"/>.</summary>
internal sealed partial class TransferReminderWorker(
    TransferReminderJob job,
    IOptions<ReminderOptions> options,
    TimeProvider time,
    ILogger<TransferReminderWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.ReminderMinutes <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.ReminderMinutes), time);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recordatorio de transferencias omitido ({Error})")]
    private static partial void LogSkipped(ILogger logger, string error);
}
