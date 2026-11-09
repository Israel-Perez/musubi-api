using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>
/// Solicitudes de compra "Se busca" (HU-13; maquetas <c>Necesidad-1…3</c>, <c>Necesidad-publicada</c> y
/// <c>Mis-solicitudes</c>). Filas canonicas en docs/endpoints.md. Siempre con sesion.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/wanted")]
[Authorize]
public sealed class WantedController : ControllerBase
{
    private WantedService wanted => HttpContext.RequestServices.GetRequiredService<WantedService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

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

    /// <summary>Una solicitud ("Ver mi solicitud"; las de otros llegan con HU-15).</summary>
    /// <param name="id">Id.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La solicitud.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<WantedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken) =>
        this.Respond(
            await wanted.GetAsync(userId, User.FindFirst(MusubiClaims.Community)?.Value, id, cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);
}
