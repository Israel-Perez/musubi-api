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
/// Registro, verificacion del correo institucional y consentimientos (HU-01, HU-03, HU-47).
/// Publico por naturaleza (aun no hay sesion) y con rate limit por IP (OWASP API2/API4).
/// Filas canonicas en docs/endpoints.md.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[AllowAnonymous]
[EnableRateLimiting(AuthPlatformServiceCollectionExtensions.AuthRateLimit)]
public sealed class AuthController : ControllerBase
{
    // Los servicios del modulo son internal (el contrato publico son los DTO): el controller,
    // que MVC exige publico, los toma del contenedor de la peticion.
    private RegistrationService registration => HttpContext.RequestServices.GetRequiredService<RegistrationService>();

    /// <summary>Campus detectado por el dominio del correo (maqueta "Campus detectado").</summary>
    /// <param name="request">Correo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Siglas y nombre del campus.</returns>
    [HttpPost("campus")]
    [ProducesResponseType<CampusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Campus(CampusRequest request, CancellationToken cancellationToken) =>
        Respond(await registration.ResolveCampusAsync(request?.Email ?? string.Empty, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Crea la cuenta y envia el codigo de verificacion (HU-01, HU-47).</summary>
    /// <param name="request">Datos del registro.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Vencimiento del codigo.</returns>
    [HttpPost("register")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        Respond(await registration.RegisterAsync(request, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);

    /// <summary>Reenvia el codigo (HU-03).</summary>
    /// <param name="request">Correo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Desde cuando se puede volver a pedir.</returns>
    [HttpPost("verification/resend")]
    [ProducesResponseType<ResendCodeResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Resend(ResendCodeRequest request, CancellationToken cancellationToken) =>
        Respond(await registration.ResendAsync(request?.Email ?? string.Empty, cancellationToken).ConfigureAwait(false), StatusCodes.Status202Accepted);

    /// <summary>Verifica el correo y abre la sesion (HU-03).</summary>
    /// <param name="request">Correo y codigo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La sesion.</returns>
    [HttpPost("verification/confirm")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Confirm(ConfirmCodeRequest request, CancellationToken cancellationToken) =>
        Respond(
            await registration.ConfirmAsync(request, Request.Headers.UserAgent.ToString(), cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);

    private ObjectResult Respond<T>(Result<T> result, int successStatus) =>
        result.Error is { } error ? this.Problem(error) : StatusCode(successStatus, result.Value);
}
