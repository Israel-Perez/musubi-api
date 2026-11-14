using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Recommendations;

/// <summary>
/// Recomendaciones con ML (HU-35, M-11): "Recomendado para ti" (con sesion) y "Ver parecidos" (tambien sin sesion, como el
/// detalle). Filas canonicas en docs/endpoints.md. El historial que las alimenta es privado y nunca se expone (RNF-07).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}")]
[Authorize]
public sealed class RecommendationsController : ControllerBase
{
    private RecommendationService recommendations => HttpContext.RequestServices.GetRequiredService<RecommendationService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    private string? communityId => User.FindFirst(MusubiClaims.Community)?.Value;

    /// <summary>"Recomendado para ti" (maqueta: <c>Inicio</c>, titulo de la guia de estilo).</summary>
    /// <param name="limit">De 1 a 20 (10 si no se manda).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las recomendaciones.</returns>
    [HttpGet("recommendations")]
    [ProducesResponseType<RecommendationPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ForYou([FromQuery] int? limit, CancellationToken cancellationToken) =>
        Ok(await recommendations.ForYouAsync(userId, communityId ?? string.Empty, limit, cancellationToken).ConfigureAwait(false));

    /// <summary>"Ver parecidos" de una publicacion (maqueta <c>Producto</c> y <c>Producto-visitante</c>).</summary>
    /// <param name="id">Publicacion.</param>
    /// <param name="limit">De 1 a 20 (10 si no se manda).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las parecidas; sin sesion, sin el vendedor (RNF-05).</returns>
    [HttpGet("products/{id}/similar")]
    [AllowAnonymous]
    [ProducesResponseType<RecommendationPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Similar(string id, [FromQuery] int? limit, CancellationToken cancellationToken)
    {
        // Con [AllowAnonymous] la sesion es opcional: si viene, se usa.
        var viewer = string.IsNullOrEmpty(userId) ? null : userId;
        return this.Respond(
            await recommendations.SimilarAsync(viewer, viewer is null ? null : communityId, id, limit, cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);
    }
}
