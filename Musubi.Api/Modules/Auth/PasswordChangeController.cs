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

namespace Musubi.Api.Modules.Auth;

/// <summary>
/// Cambiar la contrasena con sesion (HU-04, maqueta <c>Ajustes</c>). Con el rate limit de auth: pedir la
/// contrasena actual no debe servir para adivinarla. Fila canonica en docs/endpoints.md.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/password/change")]
[Authorize]
[EnableRateLimiting(AuthPlatformServiceCollectionExtensions.AuthRateLimit)]
public sealed class PasswordChangeController : ControllerBase
{
    /// <summary>Cambia la contrasena: cierra todas las sesiones y devuelve una nueva para este dispositivo (OWASP ASVS V3.3).</summary>
    /// <param name="request">Contrasena actual y nueva.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La sesion nueva.</returns>
    [HttpPost]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Change(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        this.Respond(
            await HttpContext.RequestServices.GetRequiredService<PasswordChangeService>().ChangeAsync(
                User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty,
                request,
                Request.Headers.UserAgent.ToString(),
                cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);
}
