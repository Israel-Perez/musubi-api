using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace SecurityTest.Common;

/// <summary>Controller solo de pruebas (no existe en Musubi.Api): ejercita la autenticacion, el rate limit y el contrato de errores (02-03).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/test-auth")]
public sealed class AuthProbeController : ControllerBase
{
    /// <summary>Sin atributo de auth: lo protege la politica de respaldo.</summary>
    [HttpGet("fallback")]
    public IActionResult Fallback() => Ok();

    [Authorize]
    [HttpGet("protected")]
    public IActionResult Protected() => Ok(new
    {
        sub = User.FindFirst(MusubiClaims.Subject)?.Value,
        community = User.FindFirst(MusubiClaims.Community)?.Value,
        roles = User.FindAll(MusubiClaims.Role).Select(c => c.Value).ToArray(),
    });

    [Authorize(Roles = "admin")]
    [HttpGet("admin")]
    public IActionResult AdminOnly() => Ok();

    [AllowAnonymous]
    [EnableRateLimiting(AuthPlatformServiceCollectionExtensions.AuthRateLimit)]
    [HttpPost("login")]
    public IActionResult Login() => Ok();

    [AllowAnonymous]
    [HttpPost("validate")]
    public IActionResult Validate(ProbeInput input) => Ok(input);

    [AllowAnonymous]
    [HttpGet("business-error")]
    public IActionResult BusinessError() => this.Problem(AuthErrors.InvalidCredentials);
}

/// <summary>Entrada con validacion para probar el 400 del contrato.</summary>
public sealed class ProbeInput
{
    [Required]
    [EmailAddress]
    public string? Email { get; set; }
}
