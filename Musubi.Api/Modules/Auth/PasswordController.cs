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
/// Recuperar la contrasena con un codigo al correo institucional (HU-56). Publico y con rate limit
/// por IP; nunca revela si una cuenta existe. Filas canonicas en docs/endpoints.md.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth/password")]
[AllowAnonymous]
[EnableRateLimiting(AuthPlatformServiceCollectionExtensions.AuthRateLimit)]
public sealed class PasswordController : ControllerBase
{
    // Los servicios del modulo son internal (el contrato publico son los DTO): el controller,
    // que MVC exige publico, los toma del contenedor de la peticion.
    private PasswordRecoveryService recovery => HttpContext.RequestServices.GetRequiredService<PasswordRecoveryService>();

    /// <summary>Pide el codigo para recuperar la contrasena (HU-56). Responde igual exista o no la cuenta.</summary>
    /// <param name="request">Correo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>202 con desde cuando se puede volver a pedir.</returns>
    [HttpPost("forgot")]
    [ProducesResponseType<ResendCodeResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ForgotPassword(RecoveryRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status202Accepted, await recovery.StartAsync(request, cancellationToken).ConfigureAwait(false));

    /// <summary>Define la contrasena nueva con el codigo y cierra todas las sesiones (HU-56).</summary>
    /// <param name="request">Correo, codigo y contrasena nueva.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpPost("reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(RecoveryResetRequest request, CancellationToken cancellationToken)
    {
        var result = await recovery.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error ? this.Problem(error) : NoContent();
    }
}
