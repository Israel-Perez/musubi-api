using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Orders;

/// <summary>
/// "Confirmar la compra" (HU-23; maquetas <c>Acuerdo-confirmado</c> y <c>Pedido-pago</c>): los puntos de entrega,
/// crear el pedido desde el chat, verlo y elegir como se paga. HU-24: aceptar, cancelar y confirmar la entrega. Filas canonicas en docs/endpoints.md. Siempre con
/// sesion.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
[Authorize]
public sealed class OrdersController : ControllerBase
{
    /// <summary>Tope del cuerpo de crear y pagar (un pedido pesa menos de 1 KB).</summary>
    public const int MaxBodyBytes = 16 * 1024;

    private OrderService orders => HttpContext.RequestServices.GetRequiredService<OrderService>();

    private OrderLifecycleService lifecycle => HttpContext.RequestServices.GetRequiredService<OrderLifecycleService>();

    private OrderPaymentService payments => HttpContext.RequestServices.GetRequiredService<OrderPaymentService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Los puntos de entrega del campus, con el edificio de mi carrera marcado.</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los puntos.</returns>
    [HttpGet("meeting-points")]
    [ProducesResponseType<MeetingPointsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MeetingPoints(CancellationToken cancellationToken) =>
        this.Respond(await orders.MeetingPointsAsync(userId, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Crear pedido": nace Pendiente (RN-16).</summary>
    /// <param name="body">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido.</returns>
    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(CreateOrderRequest body, CancellationToken cancellationToken) =>
        this.Respond(await orders.CreateAsync(userId, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);

    /// <summary>Los pedidos de una conversacion mia (el aviso del chat).</summary>
    /// <param name="conversationId">La conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los pedidos.</returns>
    [HttpGet]
    [ProducesResponseType<OrderListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ByConversation([FromQuery] string? conversationId, CancellationToken cancellationToken) =>
        this.Respond(await orders.ByConversationAsync(userId, conversationId, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Mis pedidos" (HU-25): mis compras por pestana, con los numeros de las tres.</summary>
    /// <param name="tab"><c>active</c>, <c>completed</c> o <c>cancelled</c>; sin pestana, todos.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 50; 20 por defecto).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina.</returns>
    [HttpGet("mine")]
    [ProducesResponseType<MyOrderPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Mine(
        [FromQuery] string? tab,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        this.Respond(await orders.MineAsync(userId, tab, cursor, limit, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Vendidos" (HU-26): mis ventas completadas, con el numero y el total.</summary>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 50; 20 por defecto).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina.</returns>
    [HttpGet("sales")]
    [ProducesResponseType<SalesPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sales([FromQuery] string? cursor, [FromQuery] int? limit, CancellationToken cancellationToken) =>
        this.Respond(await orders.SalesAsync(userId, cursor, limit, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Tu pedido": solo lo ven quien compra y quien vende.</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken) =>
        this.Respond(await orders.GetAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"¿Como quieres pagar?": hoy, efectivo al entregar (RN-18).</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="body">La forma de pago.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido.</returns>
    [HttpPut("{id}/payment")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetPayment(string id, SetPaymentRequest body, CancellationToken cancellationToken) =>
        this.Respond(await orders.SetPaymentAsync(userId, id, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Aceptar pedido" (HU-24, RF-34): solo quien vende, con el pago ya elegido; aparta las piezas.</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido confirmado.</returns>
    [HttpPost("{id}/accept")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Accept(string id, CancellationToken cancellationToken) =>
        this.Respond(await lifecycle.AcceptAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Rechazar" o "Cancelar pedido" (HU-24, RN-20): cualquiera de las partes, con un motivo de la lista.</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="body">El motivo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido cancelado.</returns>
    [HttpPost("{id}/cancel")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(string id, CancelOrderRequest body, CancellationToken cancellationToken) =>
        this.Respond(await lifecycle.CancelAsync(userId, id, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Entregue" o "Recibi" (HU-24, RN-19): con las dos confirmaciones el pedido se completa.</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido.</returns>
    [HttpPost("{id}/handover")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Handover(string id, CancellationToken cancellationToken) =>
        this.Respond(await lifecycle.ConfirmHandoverAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>
    /// "Paga por transferencia" (HU-55): a quien compra, la cuenta de quien vende con la CLABE completa; a quien vende,
    /// enmascarada. Solo las partes del pedido (RNF-03, RNF-05).
    /// </summary>
    /// <param name="id">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los datos de la transferencia.</returns>
    [HttpGet("{id}/transfer")]
    [ProducesResponseType<TransferDetailsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Transfer(string id, CancellationToken cancellationToken) =>
        this.Respond(await payments.TransferAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Enviar referencia a Diego" (HU-55): quien compra avisa que ya transfirio.</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="body">La referencia.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido.</returns>
    [HttpPost("{id}/transfer")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReportTransfer(string id, ReportTransferRequest body, CancellationToken cancellationToken) =>
        this.Respond(await payments.ReportTransferAsync(userId, id, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Todavia no me llega" (HU-55): quien vende aun no ve la transferencia; se le avisa a quien compra.</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido.</returns>
    [HttpPost("{id}/transfer/not-received")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TransferNotReceived(string id, CancellationToken cancellationToken) =>
        this.Respond(await payments.NotReceivedAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>
    /// El codigo de entrega (HU-55, efectivo): quien vende escribe el que le dicta quien compra y el pedido se completa.
    /// Hasta 5 intentos.
    /// </summary>
    /// <param name="id">El pedido.</param>
    /// <param name="body">El codigo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El pedido completado.</returns>
    [HttpPost("{id}/delivery-code")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeliveryCode(string id, DeliveryCodeRequest body, CancellationToken cancellationToken) =>
        this.Respond(await lifecycle.CompleteWithCodeAsync(userId, id, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);
}
