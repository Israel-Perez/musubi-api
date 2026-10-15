using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SecurityTest.Common;

/// <summary>Controller solo de pruebas (no existe en Musubi.Api): lanza una excepcion con un mensaje sensible.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/test-probe")]
[AllowAnonymous]
public sealed class ThrowingProbeController : ControllerBase
{
    /// <summary>Mensaje que nunca debe salir al cliente fuera de Development.</summary>
    public const string SensitiveMessage = "Detalle interno: Host=db;Username=musubi";

    [HttpGet("throw")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "MVC solo enruta acciones de instancia.")]
    public IActionResult Throw() => throw new InvalidOperationException(SensitiveMessage);
}
