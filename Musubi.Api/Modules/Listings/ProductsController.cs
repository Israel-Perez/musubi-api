using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Caching;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Listings;

/// <summary>
/// Publicar productos (HU-06, HU-50), la vitrina publica (HU-48 fase 2) y el detalle (HU-11). Filas canonicas en docs/endpoints.md.
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

    /// <summary>Vitrina y busqueda con filtros (maqueta <c>Inicio-visitante</c>, <c>Inicio</c>, "Filtrar", <c>Busqueda</c> y <c>Busqueda-filtros</c>; HU-09, HU-10).</summary>
    /// <param name="campus">Campus del visitante (sin sesion; por defecto el del piloto).</param>
    /// <param name="category">Slugs de las categorias (se puede repetir el parametro).</param>
    /// <param name="minPriceCents">Precio minimo, inclusivo.</param>
    /// <param name="maxPriceCents">Precio maximo, inclusivo.</param>
    /// <param name="condition"><c>new</c> o <c>used</c>.</param>
    /// <param name="sort"><c>relevance</c>, <c>newest</c>, <c>oldest</c>, <c>priceAsc</c> o <c>priceDesc</c>.</param>
    /// <param name="q">Palabra clave: con ella solo salen las disponibles.</param>
    /// <param name="hideSoldOut">Ocultar agotados; sin el, la busqueda los oculta y la vitrina no.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 50).</param>
    /// <param name="page">Pagina (base 1); ignora <paramref name="cursor"/>.</param>
    /// <param name="sellerId">Perfil publico (HU-05): solo las publicaciones de esa persona; con <paramref name="hideSoldOut"/>=true, solo las activas.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina.</returns>
    [HttpGet]
    [AllowAnonymous]
    [OutputCache(PolicyName = CachingServiceCollectionExtensions.ShowcasePolicy)]
    [ConditionalGet]
    [ProducesResponseType<ProductPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Showcase(
        [FromQuery] string? campus,
        [FromQuery] string[]? category,
        [FromQuery] long? minPriceCents,
        [FromQuery] long? maxPriceCents,
        [FromQuery] string? condition,
        [FromQuery] string? sort,
        [FromQuery] string? q,
        [FromQuery] bool? hideSoldOut,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        [FromQuery] int? page,
        [FromQuery] string? sellerId,
        CancellationToken cancellationToken) =>
        this.Respond(
            await listings.ShowcaseAsync(
                new ShowcaseViewer(communityId is null ? null : userId, communityId),
                campus,
                category,
                minPriceCents,
                maxPriceCents,
                condition,
                sort,
                q,
                hideSoldOut,
                cursor,
                limit,
                page,
                sellerId,
                cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);

    /// <summary>Productos populares del campus por periodo UTC (HU-49).</summary>
    [HttpGet("popular")]
    [AllowAnonymous]
    [OutputCache(PolicyName = CachingServiceCollectionExtensions.PopularPolicy)]
    [ProducesResponseType<PopularProductPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Popular([FromQuery] string? period, [FromQuery] string? campus, [FromQuery] string? cursor, [FromQuery] int? limit, CancellationToken cancellationToken) =>
        this.Respond(await listings.PopularAsync(new ShowcaseViewer(communityId is null ? null : userId, communityId), campus, period, cursor, limit, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Mis publicaciones por pestana, con los conteos de las tres (HU-08, maqueta <c>Mis-productos</c>).</summary>
    /// <param name="tab"><c>active</c>, <c>sold</c> o <c>soldOut</c>; sin pestana, todas menos las quitadas.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 50).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones del usuario.</returns>
    [HttpGet("mine")]
    [ProducesResponseType<MyProductPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Mine(
        [FromQuery] string? tab,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        this.Respond(await listings.MineAsync(userId, tab, cursor, limit, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

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

    /// <summary>Detalle de una publicacion (maqueta <c>Producto</c> y <c>Producto-visitante</c>, HU-11).</summary>
    /// <param name="id">Publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El detalle; sin sesion, sin el vendedor (RNF-05).</returns>
    [HttpGet("{id}")]
    [AllowAnonymous]
    [ConditionalGet]
    [ProducesResponseType<ProductDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detail(string id, CancellationToken cancellationToken)
    {
        // Con [AllowAnonymous] la sesion es opcional: si viene, se usa.
        var viewer = string.IsNullOrEmpty(userId) ? null : userId;
        return this.Respond(
            await listings.DetailAsync(viewer, communityId, id, cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);
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
