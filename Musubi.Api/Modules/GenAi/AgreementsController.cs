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

namespace Musubi.Api.Modules.GenAi;

/// <summary>
/// El acuerdo con IA (HU-32/33/34; maquetas <c>Chat</c>, <c>Acuerdo-esperando</c> y <c>Acuerdo-confirmado</c>): pedir
/// el resumen, verlo, corregirlo, confirmarlo y descartarlo. "Crear pedido" es <c>POST /orders</c> con
/// <c>agreementId</c>. Filas canonicas en docs/endpoints.md. Siempre con sesion.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/agreements")]
[Authorize]
public sealed class AgreementsController : ControllerBase
{
    /// <summary>Tope del cuerpo (un acuerdo pesa menos de 1 KB).</summary>
    public const int MaxBodyBytes = 16 * 1024;

    private AgreementService agreements => HttpContext.RequestServices.GetRequiredService<AgreementService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>"Resumir acuerdo": la IA lee el chat y propone el acuerdo (RN-39: solo a peticion).</summary>
    /// <param name="body">La conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo.</returns>
    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    [EnableRateLimiting(GenAiModuleServiceCollectionExtensions.SummaryRateLimit)]
    [ProducesResponseType<AgreementResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Summarize(RequestAgreementRequest body, CancellationToken cancellationToken) =>
        this.Respond(await agreements.RequestAsync(userId, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);

    /// <summary>El acuerdo vigente de una conversacion mia (cero o uno).</summary>
    /// <param name="conversationId">La conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los acuerdos.</returns>
    [HttpGet]
    [ProducesResponseType<AgreementListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ByConversation([FromQuery] string? conversationId, CancellationToken cancellationToken) =>
        this.Respond(await agreements.ByConversationAsync(userId, conversationId, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Corregir la propuesta: borra las dos confirmaciones.</summary>
    /// <param name="id">El acuerdo.</param>
    /// <param name="body">Lo que cambia.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo.</returns>
    [HttpPut("{id}")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<AgreementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Update(string id, UpdateAgreementRequest body, CancellationToken cancellationToken) =>
        this.Respond(await agreements.UpdateAsync(userId, id, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Confirmar acuerdo" (RN-41).</summary>
    /// <param name="id">El acuerdo.</param>
    /// <param name="body">La version que se vio.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo.</returns>
    [HttpPost("{id}/confirm")]
    [RequestSizeLimit(MaxBodyBytes)]
    [ProducesResponseType<AgreementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Confirm(string id, ConfirmAgreementRequest body, CancellationToken cancellationToken) =>
        this.Respond(await agreements.ConfirmAsync(userId, id, body, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Descartar el acuerdo (la conversacion sigue).</summary>
    /// <param name="id">El acuerdo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>204.</returns>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Discard(string id, CancellationToken cancellationToken)
    {
        var result = await agreements.DiscardAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.Error is { } error ? this.Problem(error) : NoContent();
    }
}
