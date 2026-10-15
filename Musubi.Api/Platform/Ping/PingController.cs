using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Musubi.Api.Platform.Ping;

/// <summary>Sonda publica de version de la API. Fila canonica en <c>docs/endpoints.md</c>; la consume <c>musubi-app</c> (01-03).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/ping")]
[AllowAnonymous]
public sealed class PingController : ControllerBase
{
    /// <summary>Responde <c>{ "status": "pong", "version": "v1" }</c>.</summary>
    /// <returns>Estado y version de la API.</returns>
    [HttpGet]
    [ProducesResponseType<PingResponse>(StatusCodes.Status200OK)]
    public ActionResult<PingResponse> Get() => Ok(new PingResponse("pong", "v1"));
}

/// <summary>Respuesta del ping (JSON camelCase).</summary>
/// <param name="Status">Siempre <c>pong</c>.</param>
/// <param name="Version">Version mayor de la API.</param>
public sealed record PingResponse(string Status, string Version);
