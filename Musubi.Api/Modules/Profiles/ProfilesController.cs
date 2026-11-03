using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Profiles;

/// <summary>
/// Mi perfil y el perfil publico (HU-04; maquetas <c>Perfil</c>, <c>Ajustes</c> y <c>Privacidad</c>). Filas
/// canonicas en docs/endpoints.md. Las rutas de escritura no llevan id: solo se edita el perfil del token (OWASP API1).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/profiles")]
[Authorize]
public sealed class ProfilesController : ControllerBase
{
    private ProfileService profiles => HttpContext.RequestServices.GetRequiredService<ProfileService>();

    private ProfilePhotoService photos => HttpContext.RequestServices.GetRequiredService<ProfilePhotoService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Mi perfil (maqueta <c>Perfil</c>).</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil.</returns>
    [HttpGet("me")]
    [ProducesResponseType<MyProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        this.Respond(await profiles.MeAsync(userId, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Datos personales": carrera y periodo.</summary>
    /// <param name="request">Carrera y periodo (<c>null</c> los borra).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil.</returns>
    [HttpPut("me")]
    [ProducesResponseType<MyProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        this.Respond(await profiles.UpdateAsync(userId, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Quien te ve" (maqueta <c>Privacidad</c>).</summary>
    /// <param name="request">Interruptores.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil.</returns>
    [HttpPut("me/visibility")]
    [ProducesResponseType<MyProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetVisibility(VisibilityRequest request, CancellationToken cancellationToken) =>
        this.Respond(await profiles.SetVisibilityAsync(userId, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Sube o reemplaza mi foto: cuadrada de 512 px, en WebP y sin metadatos.</summary>
    /// <param name="photo">Imagen (JPG, PNG o WebP; hasta 10 MB).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil.</returns>
    [HttpPut("me/photo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(PhotoUploadService.MaxBytes + (64 * 1024))]
    [EnableRateLimiting(ListingsModuleServiceCollectionExtensions.UploadRateLimit)]
    [ProducesResponseType<MyProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UploadPhoto(IFormFile photo, CancellationToken cancellationToken) =>
        this.Respond(await photos.UploadAsync(userId, photo, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Quita mi foto (idempotente).</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpDelete("me/photo")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemovePhoto(CancellationToken cancellationToken)
    {
        var result = await photos.RemoveAsync(userId, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error ? this.Problem(error) : NoContent();
    }

    /// <summary>Perfil de otra persona del mismo campus (NOTE (HU-05): reputacion, publicaciones y opiniones).</summary>
    /// <param name="id">Id del usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil publico.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<PublicProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Public(string id, CancellationToken cancellationToken) =>
        this.Respond(
            await profiles.PublicAsync(User.FindFirst(MusubiClaims.Community)?.Value, id, cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);
}
