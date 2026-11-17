using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Admin;

/// <summary>
/// Bandeja de reportes del administrador (HU-39, RF-54; sin pantalla en la maqueta). Filas canonicas en
/// docs/endpoints.md. Solo el rol <c>admin</c> (RNF-03): un estudiante recibe 403 <c>auth.forbidden</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/reports")]
[Authorize(Roles = UserRoles.Admin)]
public sealed class AdminReportsController : ControllerBase
{
    private ModerationService moderation => HttpContext.RequestServices.GetRequiredService<ModerationService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>"Reportes": los abiertos (por defecto) o los revisados del campus.</summary>
    /// <param name="status"><c>open</c> o <c>closed</c>.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>200 con los reportes.</returns>
    [HttpGet]
    [ProducesResponseType<AdminReportsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken cancellationToken) =>
        this.Respond(await moderation.ListReportsAsync(userId, status, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Descartar" o "Procedente" (con una publicacion, "Retirar publicacion").</summary>
    /// <param name="id">Reporte.</param>
    /// <param name="request">La accion y la nota.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>200 con el reporte revisado.</returns>
    [HttpPost("{id}/resolution")]
    [ProducesResponseType<AdminReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resolve(string id, ResolveReportRequest request, CancellationToken cancellationToken) =>
        this.Respond(await moderation.ResolveAsync(userId, id, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);
}
