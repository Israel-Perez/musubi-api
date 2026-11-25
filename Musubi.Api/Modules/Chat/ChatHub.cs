using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Musubi.Api.Modules.Chat;

/// <summary>
/// Hub de tiempo real del chat (<c>/hubs/chat</c>, RNF-10). Los mensajes se envian por REST; el unico metodo que el
/// cliente llama es <see cref="Typing"/> ("Escribiendo…", HU-65), que no guarda nada. Cada conexion es de su usuario
/// (<c>sub</c> del JWT), asi un mensaje llega a todos los dispositivos de los dos participantes.
/// </summary>
[Authorize]
internal sealed class ChatHub(ChatService chat) : Hub
{
    /// <summary>HU-65: evento con un <see cref="TypingEvent"/> para la otra persona.</summary>
    public const string TypingEventName = "typing";

    /// <summary>HU-65: evento con un <see cref="ConversationReadEvent"/> ("Leido").</summary>
    public const string ConversationRead = "conversationRead";

    /// <summary>
    /// "Escribiendo…" (HU-65): la app lo llama a lo mas cada pocos segundos mientras la persona escribe. Solo llega a la
    /// otra parte si quien lo llama participa, esta activo y no hay un bloqueo.
    /// </summary>
    /// <param name="conversationId">Conversacion.</param>
    /// <returns>Tarea.</returns>
    public async Task Typing(string conversationId)
    {
        var userId = Context.User?.FindFirst(Musubi.Api.Platform.Auth.MusubiClaims.Subject)?.Value;
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(conversationId))
        {
            return;
        }

        await chat.TypingAsync(userId, conversationId, Context.ConnectionAborted).ConfigureAwait(false);
    }

    /// <summary>Ruta del hub.</summary>
    public const string Path = "/hubs/chat";

    /// <summary>Evento con un <see cref="MessageResponse"/> nuevo.</summary>
    public const string MessageCreated = "messageCreated";

    /// <summary>
    /// Evento con <c>{ conversationId }</c>: el acuerdo de esa conversacion cambio (HU-34: se pidio, se edito, se
    /// confirmo, se descarto o ya es pedido). La app vuelve a leerlo; el evento no lleva datos del acuerdo.
    /// </summary>
    public const string AgreementChanged = "agreementChanged";
}

/// <summary>Carga del evento <see cref="ChatHub.AgreementChanged"/>.</summary>
/// <param name="ConversationId">La conversacion.</param>
public sealed record AgreementChangedEvent(string ConversationId);

/// <summary>
/// Entrega los eventos del chat por el hub. Si Redis (el backplane) no responde, el mensaje ya quedo
/// guardado: se registra y la app lo ve al volver a cargar la conversacion (degradacion controlada).
/// </summary>
internal sealed partial class ChatNotifier(IHubContext<ChatHub> hub, ILogger<ChatNotifier> logger)
{
    /// <summary>Avisa a los participantes de un mensaje nuevo (tambien al remitente: sus otros dispositivos).</summary>
    /// <param name="participants">Ids de los participantes.</param>
    /// <param name="message">El mensaje.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public async Task MessageCreatedAsync(IReadOnlyList<string> participants, MessageResponse message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            await hub.Clients.Users(participants).SendAsync(ChatHub.MessageCreated, message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogUndelivered(logger, ex, message.ConversationId);
        }
    }

    /// <summary>Avisa a los participantes que el acuerdo de su conversacion cambio (HU-34).</summary>
    /// <param name="participants">Ids de los participantes.</param>
    /// <param name="conversationId">La conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public async Task AgreementChangedAsync(IReadOnlyList<string> participants, string conversationId, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Users(participants)
                .SendAsync(ChatHub.AgreementChanged, new AgreementChangedEvent(conversationId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogUndelivered(logger, ex, conversationId);
        }
    }

    /// <summary>HU-65: "Escribiendo…" a la otra persona (todas sus conexiones).</summary>
    /// <param name="recipientId">La otra persona.</param>
    /// <param name="typing">El evento.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public async Task TypingAsync(string recipientId, TypingEvent typing, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(typing);
        try
        {
            await hub.Clients.User(recipientId).SendAsync(ChatHub.TypingEventName, typing, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogUndelivered(logger, ex, typing.ConversationId);
        }
    }

    /// <summary>HU-65: "Leido" a los dos participantes (la otra persona lo pinta; quien leyo lo ignora).</summary>
    /// <param name="participants">Los participantes.</param>
    /// <param name="read">El evento.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public async Task ConversationReadAsync(IReadOnlyList<string> participants, ConversationReadEvent read, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        try
        {
            await hub.Clients.Users(participants).SendAsync(ChatHub.ConversationRead, read, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogUndelivered(logger, ex, read.ConversationId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mensaje guardado sin entrega en tiempo real ({ConversationId}): backplane no disponible")]
    private static partial void LogUndelivered(ILogger logger, Exception exception, string conversationId);
}
