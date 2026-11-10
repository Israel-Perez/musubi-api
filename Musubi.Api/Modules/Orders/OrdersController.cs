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
/// crear el pedido desde el chat, verlo y elegir como se paga. Filas canonicas en docs/endpoints.md. Siempre con
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
}
