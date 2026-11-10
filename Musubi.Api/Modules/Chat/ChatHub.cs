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
/// Hub de tiempo real del chat (<c>/hubs/chat</c>, RNF-10). La app solo escucha: no expone metodos que
/// el cliente pueda llamar; los mensajes se envian por REST. Cada conexion es de su usuario
/// (<c>sub</c> del JWT), asi un mensaje llega a todos los dispositivos de los dos participantes.
/// </summary>
[Authorize]
internal sealed class ChatHub : Hub
{
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mensaje guardado sin entrega en tiempo real ({ConversationId}): backplane no disponible")]
    private static partial void LogUndelivered(ILogger logger, Exception exception, string conversationId);
}
