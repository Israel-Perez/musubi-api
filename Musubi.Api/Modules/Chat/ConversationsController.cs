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

namespace Musubi.Api.Modules.Chat;

/// <summary>
/// Conversaciones y mensajes (HU-20). Filas canonicas en docs/endpoints.md. Siempre con sesion; solo los
/// participantes leen o escriben (RN-22). La entrega en tiempo real va por <see cref="ChatHub"/>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/conversations")]
[Authorize]
public sealed class ConversationsController : ControllerBase
{
    private ChatService chat => HttpContext.RequestServices.GetRequiredService<ChatService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>"Mensaje" en una publicacion: abre la conversacion con el vendedor, o la que ya existia.</summary>
    /// <param name="request">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>201 si es nueva, 200 si ya existia.</returns>
    [HttpPost]
    [ProducesResponseType<ConversationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ConversationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Start(StartConversationRequest request, CancellationToken cancellationToken)
    {
        var result = await chat.StartFromProductAsync(userId, request, cancellationToken).ConfigureAwait(false);
        if (result.Error is { } error)
        {
            return this.Problem(error);
        }

        var start = result.Value!;
        return StatusCode(start.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK, start.Conversation);
    }

    /// <summary>Encabezado y tarjeta de la conversacion (maqueta <c>Chat-Valeria</c>).</summary>
    /// <param name="id">Conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La conversacion.</returns>
    [HttpGet("{id}")]
    [ProducesResponseType<ConversationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken) =>
        this.Respond(await chat.GetAsync(userId, id, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Mensajes, del mas nuevo al mas viejo (la app los invierte para mostrarlos).</summary>
    /// <param name="id">Conversacion.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 100; 30 por defecto).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina.</returns>
    [HttpGet("{id}/messages")]
    [ProducesResponseType<MessagePage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Messages(string id, [FromQuery] string? cursor, [FromQuery] int? limit, CancellationToken cancellationToken) =>
        this.Respond(await chat.MessagesAsync(userId, id, cursor, limit, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Envia un mensaje de texto (se modera antes de guardarlo).</summary>
    /// <param name="id">Conversacion.</param>
    /// <param name="request">El texto.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El mensaje guardado.</returns>
    [HttpPost("{id}/messages")]
    [EnableRateLimiting(ChatModuleServiceCollectionExtensions.MessageRateLimit)]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Send(string id, SendMessageRequest request, CancellationToken cancellationToken) =>
        this.Respond(await chat.SendAsync(userId, id, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);
}
