using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Notifications;

/// <summary>
/// Avisos (HU-31; maqueta <c>Notificaciones</c>): el centro de notificaciones y el registro del dispositivo para push.
/// Filas canonicas en docs/endpoints.md. Siempre con sesion.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    /// <summary>Tope del cuerpo (un token pesa menos de 1 KB).</summary>
    public const int MaxBodyBytes = 4 * 1024;

    private NotificationService notifications => HttpContext.RequestServices.GetRequiredService<NotificationService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Mis avisos, del mas nuevo al mas viejo, con cuantos no he leido.</summary>
    /// <param name="category"><c>orders</c>, <c>requests</c>, <c>matches</c> o nada ("Todas").</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 50).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina.</returns>
    [HttpGet]
    [ProducesResponseType<NotificationPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] string? category, [FromQuery] string? cursor, [FromQuery] int? limit, CancellationToken cancellationToken) =>
        this.Respond(await notifications.ListAsync(userId, category, cursor, limit, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Marcar leidas": todos mis avisos.</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpPost("read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReadAll(CancellationToken cancellationToken)
    {
        var result = await notifications.MarkReadAsync(userId, null, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error ? this.Problem(error) : NoContent();
    }

    /// <summary>Marca un aviso como leido (al tocarlo).</summary>
    /// <param name="id">El aviso.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpPost("{id}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Read(string id, CancellationToken cancellationToken)
    {
        var result = await notifications.MarkReadAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error ? this.Problem(error) : NoContent();
    }

    /// <summary>Registra el token de FCM de este dispositivo (al iniciar sesion y cuando FCM lo cambia).</summary>
    /// <param name="body">El token.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpPut("devices")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RegisterDevice(DeviceRequest body, CancellationToken cancellationToken)
    {
        var result = await notifications.RegisterDeviceAsync(userId, body, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error ? this.Problem(error) : NoContent();
    }

    /// <summary>Quita el token de este dispositivo (al cerrar sesion).</summary>
    /// <param name="body">El token.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpPost("devices/remove")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemoveDevice(DeviceRequest body, CancellationToken cancellationToken)
    {
        var result = await notifications.RemoveDeviceAsync(userId, body, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error ? this.Problem(error) : NoContent();
    }
}
