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
/// Favoritos de HU-53 (maqueta <c>Favoritos</c> y el corazon de <c>Producto</c>). Las rutas no llevan el id del
/// usuario: solo se administran los favoritos del token (OWASP API1). Filas canonicas en docs/endpoints.md.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/favorites")]
[Authorize]
public sealed class FavoritesController : ControllerBase
{
    private FavoriteService favorites => HttpContext.RequestServices.GetRequiredService<FavoriteService>();

    private string me => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    private string communityId => User.FindFirst(MusubiClaims.Community)?.Value ?? string.Empty;

    /// <summary>"Favoritos": lo guardado por el usuario, con el estado actual de cada publicacion.</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La lista.</returns>
    [HttpGet]
    [ProducesResponseType<FavoritesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await favorites.ListAsync(me, communityId, cancellationToken).ConfigureAwait(false));

    /// <summary>Guarda un producto. Idempotente: dos toques no duplican.</summary>
    /// <param name="productId">Publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204, o el error.</returns>
    [HttpPut("{productId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Save(string productId, CancellationToken cancellationToken)
    {
        var result = await favorites.AddAsync(me, productId, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error ? this.Problem(error) : NoContent();
    }

    /// <summary>Quita un producto. Idempotente: si no estaba guardado, tambien responde 204.</summary>
    /// <param name="productId">Publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpDelete("{productId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Remove(string productId, CancellationToken cancellationToken)
    {
        await favorites.RemoveAsync(me, productId, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
