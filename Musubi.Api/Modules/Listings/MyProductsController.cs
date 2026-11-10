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
/// Editar, pausar, reactivar y eliminar mis publicaciones (HU-07, maqueta <c>Mis-productos</c>). Filas canonicas
/// en docs/endpoints.md. Siempre con sesion; para otra persona responde 404, como si no existiera (OWASP API1).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products")]
[Authorize]
public sealed class MyProductsController : ControllerBase
{
    /// <summary>Tope del cuerpo al editar: el JSON cabe en menos de 3 KB (OWASP API4).</summary>
    public const int MaxEditBytes = 16 * 1024;

    private MyListingsService myListings => HttpContext.RequestServices.GetRequiredService<MyListingsService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Guarda los cambios de una publicacion ("Editar publicacion", <c>Vender-2…4</c>).</summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="request">Datos editables completos.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion ya cambiada.</returns>
    [HttpPut("{id}")]
    [RequestSizeLimit(MaxEditBytes)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(string id, UpdateProductRequest request, CancellationToken cancellationToken) =>
        this.Respond(await myListings.UpdateAsync(userId, id, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Pausa una publicacion: sale de la vitrina y sigue en "Mis productos".</summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion pausada.</returns>
    [HttpPost("{id}/pause")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pause(string id, CancellationToken cancellationToken) =>
        this.Respond(await myListings.SetPausedAsync(userId, id, paused: true, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Reactiva una publicacion pausada (disponible, o agotada si no tiene piezas).</summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion reactivada.</returns>
    [HttpPost("{id}/resume")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resume(string id, CancellationToken cancellationToken) =>
        this.Respond(await myListings.SetPausedAsync(userId, id, paused: false, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Elimina una publicacion y sus fotos ("Esta accion no se puede deshacer").</summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var result = await myListings.DeleteAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error ? this.Problem(error) : NoContent();
    }
}
