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
/// Suspender y reactivar cuentas (HU-40, RF-55; sin pantalla en la maqueta). Filas canonicas en docs/endpoints.md.
/// Solo el rol <c>admin</c> (RNF-03) y solo cuentas de su campus.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/accounts")]
[Authorize(Roles = UserRoles.Admin)]
public sealed class AdminAccountsController : ControllerBase
{
    private ModerationService moderation => HttpContext.RequestServices.GetRequiredService<ModerationService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>"Cuentas suspendidas" del campus.</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>200 con las cuentas.</returns>
    [HttpGet("suspended")]
    [ProducesResponseType<AdminAccountsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Suspended(CancellationToken cancellationToken) =>
        this.Respond(await moderation.ListSuspendedAsync(userId, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Una cuenta con sus reportes procedentes y su historial de moderacion.</summary>
    /// <param name="id">Cuenta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>200 con la cuenta.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<AdminAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken) =>
        this.Respond(await moderation.GetAccountAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Suspender cuenta" con su motivo.</summary>
    /// <param name="id">Cuenta.</param>
    /// <param name="request">El motivo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>200 con la cuenta suspendida.</returns>
    [HttpPost("{id}/suspension")]
    [ProducesResponseType<AdminAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Suspend(string id, SuspendAccountRequest request, CancellationToken cancellationToken) =>
        this.Respond(await moderation.SuspendAsync(userId, id, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Reactivar cuenta".</summary>
    /// <param name="id">Cuenta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>200 con la cuenta reactivada.</returns>
    [HttpDelete("{id}/suspension")]
    [ProducesResponseType<AdminAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reactivate(string id, CancellationToken cancellationToken) =>
        this.Respond(await moderation.ReactivateAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);
}
