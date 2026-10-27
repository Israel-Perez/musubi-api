using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Listings;

/// <summary>
/// Publicar productos (HU-06, HU-50) y la vitrina publica (HU-48 fase 2). Filas canonicas en docs/endpoints.md.
/// Con sesion por defecto; la vitrina es anonima y entonces no muestra al vendedor (RNF-05).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/products")]
[Authorize]
public sealed class ProductsController : ControllerBase
{
    private ListingService listings => HttpContext.RequestServices.GetRequiredService<ListingService>();

    private PhotoUploadService photos => HttpContext.RequestServices.GetRequiredService<PhotoUploadService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    private string? communityId => User.FindFirst(MusubiClaims.Community)?.Value;

    /// <summary>Vitrina: lo recien publicado del campus (maqueta <c>Inicio-visitante</c>, <c>Inicio</c>, "Filtrar").</summary>
    /// <param name="campus">Campus del visitante (sin sesion; por defecto el del piloto).</param>
    /// <param name="category">Slug de la categoria.</param>
    /// <param name="hideSoldOut">Ocultar agotados.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 50).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina.</returns>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<ProductPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Showcase(
        [FromQuery] string? campus,
        [FromQuery] string? category,
        [FromQuery] bool hideSoldOut,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        this.Respond(
            await listings.ShowcaseAsync(communityId, campus, category, hideSoldOut, cursor, limit, cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);

    /// <summary>Mis publicaciones ("Ver mis productos").</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones del usuario.</returns>
    [HttpGet("mine")]
    [ProducesResponseType<ProductPage>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        Ok(await listings.MineAsync(userId, cancellationToken).ConfigureAwait(false));

    /// <summary>"Productos parecidos se venden entre…" (maqueta <c>Vender-3</c>).</summary>
    /// <param name="category">Slug de la categoria.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El rango, o 204 si aun no hay suficientes publicaciones.</returns>
    [HttpGet("price-range")]
    [ProducesResponseType<PriceRangeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> PriceRange([FromQuery] string category, CancellationToken cancellationToken)
    {
        var range = await listings.PriceRangeAsync(communityId ?? string.Empty, category ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return range is null ? NoContent() : Ok(range);
    }

    /// <summary>Publica un producto (maqueta <c>Vender-4</c> → <c>Vender-listo</c>).</summary>
    /// <param name="request">Datos del producto.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion.</returns>
    [HttpPost]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Publish(CreateProductRequest request, CancellationToken cancellationToken) =>
        this.Respond(await listings.PublishAsync(userId, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);

    /// <summary>Sube una foto (maqueta <c>Vender-1</c>): sale en WebP, sin metadatos.</summary>
    /// <param name="photo">Imagen (JPG, PNG o WebP; hasta 10 MB).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La llave y la ruta de la foto.</returns>
    [HttpPost("photos")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(PhotoUploadService.MaxBytes + (64 * 1024))]
    [EnableRateLimiting(ListingsModuleServiceCollectionExtensions.UploadRateLimit)]
    [ProducesResponseType<PhotoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UploadPhoto(IFormFile photo, CancellationToken cancellationToken) =>
        this.Respond(await photos.UploadAsync(userId, photo, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);
}
