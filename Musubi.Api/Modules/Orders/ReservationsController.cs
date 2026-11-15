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
/// Apartados de 24 h (HU-52; maquetas <c>Apartar</c>, <c>Apartado-listo</c>, <c>Mis-apartados</c>,
/// <c>Verificar-comprador</c> y <c>Apartado-verificado</c>). Filas canonicas en docs/endpoints.md. Siempre con sesion y
/// solo las partes del apartado (RNF-03, OWASP API1).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reservations")]
[Authorize]
public sealed class ReservationsController : ControllerBase
{
    private ReservationService reservations => HttpContext.RequestServices.GetRequiredService<ReservationService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>"Apartar": guarda una pieza 24 h (uno activo a la vez) y devuelve el QR y el codigo.</summary>
    /// <param name="body">Publicacion, punto y hora.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado.</returns>
    [HttpPost]
    [RequestSizeLimit(OrdersController.MaxBodyBytes)]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateReservationRequest body, CancellationToken cancellationToken) =>
        this.Respond(await reservations.CreateAsync(userId, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);

    /// <summary>"Mis apartados": "Yo aparté" (<c>tab=buyer</c>) o "Me apartaron" (<c>tab=seller</c>).</summary>
    /// <param name="tab">La pestana.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina.</returns>
    [HttpGet("mine")]
    [ProducesResponseType<ReservationPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] string? tab, CancellationToken cancellationToken) =>
        Ok(await reservations.MineAsync(userId, tab, cancellationToken).ConfigureAwait(false));

    /// <summary>Un apartado mio.</summary>
    /// <param name="id">El apartado.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken) =>
        this.Respond(await reservations.GetAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Cancelar apartado": cualquiera de las partes; la pieza regresa.</summary>
    /// <param name="id">El apartado.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado cancelado.</returns>
    [HttpPost("{id}/cancel")]
    [RequestSizeLimit(OrdersController.MaxBodyBytes)]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(string id, CancellationToken cancellationToken) =>
        this.Respond(await reservations.CancelAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Verificar comprador": quien vende escanea el QR o escribe el codigo (5 intentos).</summary>
    /// <param name="id">El apartado.</param>
    /// <param name="body">El codigo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado verificado.</returns>
    [HttpPost("{id}/verify")]
    [RequestSizeLimit(OrdersController.MaxBodyBytes)]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Verify(string id, VerifyReservationRequest body, CancellationToken cancellationToken) =>
        this.Respond(await reservations.VerifyAsync(userId, id, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Confirmar entrega": nace el pedido completado.</summary>
    /// <param name="id">El apartado.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El apartado recogido.</returns>
    [HttpPost("{id}/complete")]
    [RequestSizeLimit(OrdersController.MaxBodyBytes)]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(string id, CancellationToken cancellationToken) =>
        this.Respond(await reservations.CompleteAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);
}
