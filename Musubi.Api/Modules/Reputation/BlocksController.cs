using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Reputation;

/// <summary>
/// Bloqueos (HU-57; maquetas <c>chatMoreSheet</c> y <c>aj-bloqueados</c>). Filas canonicas en docs/endpoints.md.
/// Las rutas no llevan el id de quien bloquea: solo se administran los bloqueos del token (OWASP API1).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/blocks")]
[Authorize]
public sealed class BlocksController : ControllerBase
{
    private BlockService blocks => HttpContext.RequestServices.GetRequiredService<BlockService>();

    private string me => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>"Usuarios bloqueados" (Ajustes → Privacidad).</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las personas que bloqueaste.</returns>
    [HttpGet]
    [ProducesResponseType<BlockedUsersResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await blocks.ListAsync(me, cancellationToken).ConfigureAwait(false));

    /// <summary>"Bloquear a …" desde el chat. Idempotente.</summary>
    /// <param name="request">A quien.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Block(BlockRequest request, CancellationToken cancellationToken)
    {
        var result = await blocks.BlockAsync(me, request, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error ? this.Problem(error) : NoContent();
    }

    /// <summary>"Desbloquear". Idempotente: si no estaba bloqueada tambien responde 204.</summary>
    /// <param name="userId">Id de la persona bloqueada.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpDelete("{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unblock(string userId, CancellationToken cancellationToken)
    {
        await blocks.UnblockAsync(me, userId, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
