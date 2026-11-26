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
/// "Indicadores de uso" (HU-42, RF-57, CU-27; sin pantalla en la maqueta). Fila canonica en docs/endpoints.md. Solo el
/// rol <c>admin</c> (RNF-03) y solo su campus. Son conteos agregados, sin personas.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/indicators")]
[Authorize(Roles = UserRoles.Admin)]
public sealed class AdminIndicatorsController : ControllerBase
{
    private UsageIndicatorsService indicators => HttpContext.RequestServices.GetRequiredService<UsageIndicatorsService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Los indicadores del campus en la ultima semana o el ultimo mes.</summary>
    /// <param name="period"><c>week</c> (por defecto) o <c>month</c>.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>200 con los indicadores por area.</returns>
    [HttpGet]
    [ProducesResponseType<UsageIndicatorsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get([FromQuery] string? period, CancellationToken cancellationToken) =>
        this.Respond(await indicators.IndicatorsAsync(userId, period, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);
}
