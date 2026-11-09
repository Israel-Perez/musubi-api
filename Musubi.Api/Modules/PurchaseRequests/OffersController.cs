using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>
/// "Yo tengo este producto" (HU-16; maquetas <c>Yo-tengo-vincular</c>, <c>Yo-tengo-oferta</c> y
/// <c>Yo-tengo-enviada</c>): mi oferta a una solicitud. Filas canonicas en docs/endpoints.md. Siempre con sesion.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/wanted/{requestId}/offers")]
[Authorize]
public sealed class OffersController : ControllerBase
{
    private OfferService offers => HttpContext.RequestServices.GetRequiredService<OfferService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    private string? communityId => User.FindFirst(MusubiClaims.Community)?.Value;

    /// <summary>Mi oferta en esa solicitud (para editarla, RN-14).</summary>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La oferta.</returns>
    [HttpGet("mine")]
    [ProducesResponseType<OfferResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Mine(string requestId, CancellationToken cancellationToken) =>
        this.Respond(await offers.MineAsync(userId, communityId, requestId, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Enviar oferta": la crea (201) o la reemplaza (200).</summary>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="body">La oferta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La oferta.</returns>
    [HttpPut("mine")]
    [ProducesResponseType<OfferResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<OfferResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Save(string requestId, OfferRequest body, CancellationToken cancellationToken)
    {
        var result = await offers.SaveAsync(userId, communityId, requestId, body, cancellationToken).ConfigureAwait(false);
        if (result.Error is not null)
        {
            return this.Problem(result.Error);
        }

        var status = result.Value.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK;
        return StatusCode(status, result.Value.Offer);
    }
}
