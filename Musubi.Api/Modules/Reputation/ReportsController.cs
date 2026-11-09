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

namespace Musubi.Api.Modules.Reputation;

/// <summary>
/// Reportes (HU-28; maqueta <c>reportSheet</c> desde el chat y desde Ajustes). Fila canonica en docs/endpoints.md.
/// Siempre con sesion: el reporte queda a nombre de quien lo envia.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/reports")]
[Authorize]
public sealed class ReportsController : ControllerBase
{
    private ReportService reports => HttpContext.RequestServices.GetRequiredService<ReportService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>"Enviar reporte": guarda el reporte para la revision del equipo y le avisa.</summary>
    /// <param name="request">Lo reportado, el motivo y el detalle.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>201 con el id del reporte.</returns>
    [HttpPost]
    [EnableRateLimiting(ReputationModuleServiceCollectionExtensions.ReportRateLimit)]
    [ProducesResponseType<ReportResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create(CreateReportRequest request, CancellationToken cancellationToken) =>
        this.Respond(await reports.CreateAsync(userId, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);
}
