using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Reputation;

/// <summary>
/// Calificaciones (HU-27, HU-45; maquetas <c>Calificar</c> y <c>Perfil-vendedor</c> → "Opiniones"). Filas canonicas en
/// docs/endpoints.md. Quien califica sale del token (OWASP API1).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Authorize]
public sealed class RatingsController : ControllerBase
{
    /// <summary>Tope del cuerpo de "Enviar calificacion" (OWASP API4).</summary>
    public const int MaxBodyBytes = 4 * 1024;

    private RatingService ratings => HttpContext.RequestServices.GetRequiredService<RatingService>();

    private string me => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>"Enviar calificacion" de un pedido completado.</summary>
    /// <param name="id">El pedido.</param>
    /// <param name="request">Estrellas, lo que destacas y el comentario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La calificacion.</returns>
    [HttpPost("orders/{id}/rating")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<RatingResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rate(string id, RateOrderRequest request, CancellationToken cancellationToken) =>
        this.Respond(await ratings.RateAsync(me, id, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);

    /// <summary>"Opiniones" de un perfil del campus.</summary>
    /// <param name="id">La persona.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 50; 20 por defecto).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina.</returns>
    [HttpGet("profiles/{id}/ratings")]
    [ProducesResponseType<ProfileRatingPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(string id, [FromQuery] string? cursor, [FromQuery] int? limit, CancellationToken cancellationToken) =>
        this.Respond(
            await ratings.ListAsync(me, User.FindFirst(MusubiClaims.Community)?.Value, id, cursor, limit, cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);
}
