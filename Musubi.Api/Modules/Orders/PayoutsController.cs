using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Orders;

/// <summary>
/// "Cuenta para recibir pagos" (HU-55, maqueta <c>Cuenta-pagos</c> desde Ajustes). Filas canonicas en docs/endpoints.md.
/// Siempre con sesion y solo la propia: la CLABE nunca sale completa por aqui.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payouts")]
[Authorize]
public sealed class PayoutsController : ControllerBase
{
    private PayoutService payouts => HttpContext.RequestServices.GetRequiredService<PayoutService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Los bancos que se reconocen en una CLABE (para mostrar el banco mientras se escribe).</summary>
    /// <returns>La lista.</returns>
    [HttpGet("banks")]
    [ProducesResponseType<BankListResponse>(StatusCodes.Status200OK)]
    public IActionResult Banks() => Ok(PayoutService.Banks());

    /// <summary>Mi cuenta, enmascarada, y si se pueden guardar cuentas.</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El estado.</returns>
    [HttpGet("account")]
    [ProducesResponseType<PayoutAccountStatus>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken cancellationToken) =>
        Ok(await payouts.MineAsync(userId, cancellationToken).ConfigureAwait(false));

    /// <summary>"Guardar cuenta": titular igual al nombre verificado y CLABE valida; reemplaza la anterior.</summary>
    /// <param name="body">Titular y CLABE.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta enmascarada.</returns>
    [HttpPut("account")]
    [RequestSizeLimit(OrdersController.MaxBodyBytes)]
    [ProducesResponseType<PayoutAccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Save(SavePayoutAccountRequest body, CancellationToken cancellationToken) =>
        this.Respond(await payouts.SaveAsync(userId, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Quitar cuenta": desde ahi me pagan solo en efectivo.</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpDelete("account")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(CancellationToken cancellationToken)
    {
        await payouts.DeleteAsync(userId, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}
