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
/// Iniciar, renovar y cerrar sesion (HU-02, 02-03). Publico (aun no hay access token valido) y con
/// rate limit por IP (OWASP API2/API4). Filas canonicas en docs/endpoints.md.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[AllowAnonymous]
[EnableRateLimiting(AuthPlatformServiceCollectionExtensions.AuthRateLimit)]
public sealed class SessionController : ControllerBase
{
    // Los servicios del modulo son internal (el contrato publico son los DTO): el controller,
    // que MVC exige publico, los toma del contenedor de la peticion.
    private LoginService login => HttpContext.RequestServices.GetRequiredService<LoginService>();

    private string userAgent => Request.Headers.UserAgent.ToString();

    /// <summary>Inicia sesion con correo y contrasena (HU-02).</summary>
    /// <param name="request">Correo y contrasena.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La sesion.</returns>
    [HttpPost("login")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        this.Respond(await login.LoginAsync(request, userAgent, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Renueva la sesion: rota el refresh token (02-03, L-16g).</summary>
    /// <param name="request">Refresh token.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La sesion renovada.</returns>
    [HttpPost("refresh")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        this.Respond(await login.RefreshAsync(request, userAgent, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Cierra la sesion de este dispositivo (HU-02, L-10). Idempotente.</summary>
    /// <param name="request">Refresh token de la sesion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        await login.LogoutAsync(request, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
