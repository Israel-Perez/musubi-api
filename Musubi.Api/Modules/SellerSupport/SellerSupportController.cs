using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.SellerSupport;

/// <summary>
/// Apoyo a vendedores (M-12, maqueta <c>Mis-productos</c> → "Llevan tiempo sin venderse" y <c>Vender-listo</c>). Filas
/// canonicas en docs/endpoints.md. Siempre con sesion; solo de lo propio (RNF-03, OWASP API1).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products")]
[Authorize]
public sealed class SellerSupportController : ControllerBase
{
    private SellerSupportService support => HttpContext.RequestServices.GetRequiredService<SellerSupportService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Mis publicaciones que llevan mas de 3 semanas sin venderse, con quien las busca e ideas (HU-36/37/38).</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La lista (vacia si no hay).</returns>
    [HttpGet("mine/slow")]
    [ProducesResponseType<SlowListingList>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Slow(CancellationToken cancellationToken) =>
        Ok(await support.SlowAsync(userId, cancellationToken).ConfigureAwait(false));

    /// <summary>"Quienes la buscan": las solicitudes que coinciden con mi publicacion y cuantas alertas (HU-38).</summary>
    /// <param name="id">Mi publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La demanda; de otra persona o inexistente, <c>listings.product_not_found</c>.</returns>
    [HttpGet("{id}/demand")]
    [ProducesResponseType<ProductDemandResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Demand(string id, CancellationToken cancellationToken) =>
        this.Respond(await support.DemandAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Ideas con IA": un nombre mas claro y una mejor descripcion para mi publicacion (HU-37). No guarda nada.</summary>
    /// <param name="id">Mi publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La propuesta; sin IA, <c>seller_support.ideas_unavailable</c> (quedan las ideas predefinidas).</returns>
    [HttpPost("{id}/ideas")]
    [EnableRateLimiting(GenAiModuleServiceCollectionExtensions.SummaryRateLimit)]
    [ProducesResponseType<ListingIdeasResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Ideas(string id, CancellationToken cancellationToken) =>
        this.Respond(await support.IdeasAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);
}
