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
/// "Avísame cuando aparezca" (HU-12, maqueta <c>Busqueda-vacia</c>) y "Mis alertas" (Ajustes). Privadas: cada quien ve y
/// quita solo las suyas (RNF-07, OWASP API1). Filas canonicas en docs/endpoints.md.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/saved-searches")]
[Authorize]
public sealed class SavedSearchesController : ControllerBase
{
    private SavedSearchService searches => HttpContext.RequestServices.GetRequiredService<SavedSearchService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Guarda la busqueda (o renueva la misma por otros 30 dias).</summary>
    /// <param name="request">La busqueda y sus filtros.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>201 si es nueva; 200 si ya la tenia.</returns>
    [HttpPost]
    [ProducesResponseType<SavedSearchResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<SavedSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Save([FromBody] SaveSearchRequest request, CancellationToken cancellationToken)
    {
        var result = await searches.SaveAsync(userId, request, cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            return this.Problem(error);
        }

        var (search, created) = result.Value;
        return StatusCode(created ? StatusCodes.Status201Created : StatusCodes.Status200OK, search);
    }

    /// <summary>"Mis alertas": las vigentes, de la mas nueva a la mas vieja.</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las busquedas.</returns>
    [HttpGet]
    [ProducesResponseType<SavedSearchList>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        Ok(await searches.MineAsync(userId, cancellationToken).ConfigureAwait(false));

    /// <summary>Quita una alerta (idempotente).</summary>
    /// <param name="id">La busqueda.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        await searches.DeleteAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
