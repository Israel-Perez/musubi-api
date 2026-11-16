using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Listings;

/// <summary>
/// "Compara antes de comprar" y "Avísame" de <c>Producto</c> (HU-51). Siempre con sesion: el comparador muestra a
/// quienes venden (RNF-05) y el aviso es de quien lo pide; las rutas no llevan el id del usuario (OWASP API1). Filas
/// canonicas en docs/endpoints.md.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products")]
[Authorize]
public sealed class CompareController : ControllerBase
{
    private ProductComparisonService comparison => HttpContext.RequestServices.GetRequiredService<ProductComparisonService>();

    private StockAlertService alerts => HttpContext.RequestServices.GetRequiredService<StockAlertService>();

    private string me => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    private string? communityId => User.FindFirst(MusubiClaims.Community)?.Value;

    /// <summary>"Compara antes de comprar": hasta 5 opciones de la misma categoria con quien las vende.</summary>
    /// <param name="id">La publicacion que se ve.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las opciones.</returns>
    [HttpGet("{id}/compare")]
    [ProducesResponseType<ProductComparisonResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Compare(string id, CancellationToken cancellationToken) =>
        this.Respond(await comparison.CompareAsync(me, communityId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Si ya pedi "Avísame" de esta publicacion ("Te avisaremos").</summary>
    /// <param name="id">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El estado.</returns>
    [HttpGet("{id}/stock-alert")]
    [ProducesResponseType<StockAlertResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Status(string id, CancellationToken cancellationToken) =>
        this.Respond(await alerts.StatusAsync(me, communityId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Avísame": me avisan cuando la agotada vuelva a tener piezas (una vez).</summary>
    /// <param name="id">La publicacion agotada.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El aviso activo.</returns>
    [HttpPut("{id}/stock-alert")]
    [ProducesResponseType<StockAlertResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Subscribe(string id, CancellationToken cancellationToken) =>
        this.Respond(await alerts.SubscribeAsync(me, communityId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Quita mi aviso. Idempotente: si no lo tenia, tambien responde 204.</summary>
    /// <param name="id">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpDelete("{id}/stock-alert")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Unsubscribe(string id, CancellationToken cancellationToken)
    {
        await alerts.UnsubscribeAsync(me, id, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
