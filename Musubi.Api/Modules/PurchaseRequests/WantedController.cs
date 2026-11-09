using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Caching;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>
/// Solicitudes de compra "Se busca" (HU-13; maquetas <c>Necesidad-1…3</c>, <c>Necesidad-publicada</c> y
/// <c>Mis-solicitudes</c>) y el tablero del campus (HU-15; <c>Se-busca</c> y <c>Se-busca-visitante</c>). Filas
/// canonicas en docs/endpoints.md. Con sesion salvo el tablero, que tambien lo ve un visitante (HU-48).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/wanted")]
[Authorize]
public sealed class WantedController : ControllerBase
{
    private WantedService wanted => HttpContext.RequestServices.GetRequiredService<WantedService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    private string? communityId => User.FindFirst(MusubiClaims.Community)?.Value;

    /// <summary>Tablero "Se busca" del campus, con la busqueda y los filtros de la maqueta.</summary>
    /// <param name="campus">Campus del visitante (sin sesion; por defecto el del piloto).</param>
    /// <param name="category">Slugs de las categorias (se puede repetir el parametro).</param>
    /// <param name="minBudgetCents">Presupuesto minimo, inclusivo.</param>
    /// <param name="maxBudgetCents">Presupuesto maximo, inclusivo.</param>
    /// <param name="neededWithinDays">"Fecha limite" en dias (1 a 60).</param>
    /// <param name="q">Palabras del producto o de la descripcion.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 50).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina.</returns>
    [HttpGet]
    [AllowAnonymous]
    [OutputCache(PolicyName = CachingServiceCollectionExtensions.WantedBoardPolicy)]
    [ConditionalGet]
    [ProducesResponseType<WantedPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Board(
        [FromQuery] string? campus,
        [FromQuery] string[]? category,
        [FromQuery] long? minBudgetCents,
        [FromQuery] long? maxBudgetCents,
        [FromQuery] int? neededWithinDays,
        [FromQuery] string? q,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        // Con [AllowAnonymous] la sesion es opcional: si viene, se usa.
        this.Respond(
            await wanted.BoardAsync(
                new ShowcaseViewer(communityId is null ? null : userId, communityId),
                new WantedBoardFilter(campus, category, minBudgetCents, maxBudgetCents, neededWithinDays, q, cursor, limit),
                cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);

    /// <summary>"Publicar solicitud".</summary>
    /// <param name="request">Lo que necesito.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>201 con la solicitud.</returns>
    [HttpPost]
    [ProducesResponseType<WantedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Create(CreateWantedRequest request, CancellationToken cancellationToken) =>
        this.Respond(await wanted.CreateAsync(userId, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);

    /// <summary>"Mis solicitudes" (Activas y Cerradas).</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Mis solicitudes.</returns>
    [HttpGet("mine")]
    [ProducesResponseType<WantedListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        Ok(await wanted.MineAsync(userId, cancellationToken).ConfigureAwait(false));

    /// <summary>Una solicitud ("Ver mi solicitud" o una del tablero).</summary>
    /// <param name="id">Id.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La solicitud.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<WantedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken) =>
        this.Respond(
            await wanted.GetAsync(userId, communityId, id, cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);
}
