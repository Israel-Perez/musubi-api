using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Musubi.Api.Modules.Chat.Data;

// Contratos de persistencia de M-07 (HU-20). Mongo* (nube y local) e InMemory* (UseInMemoryDatabase=true)
// pasan la misma bateria de pruebas (TestShared/Chat).

/// <summary>Consulta de la bandeja (HU-22).</summary>
/// <param name="UserId">Participante.</param>
/// <param name="Role">Solo donde es comprador (<c>buyer</c>), vendedor (<c>seller</c>), o ambas (<c>null</c>).</param>
/// <param name="BeforeAt">Cursor: actividad de la ultima conversacion de la pagina anterior.</param>
/// <param name="BeforeId">Cursor: id de esa conversacion (desempata la misma hora).</param>
/// <param name="Limit">Maximo de resultados.</param>
internal sealed record InboxQuery(string UserId, string? Role, DateTime? BeforeAt, string? BeforeId, int Limit);

/// <summary>Conversaciones.</summary>
internal interface IConversationRepository
{
    /// <summary>
    /// Inserta la conversacion o, si ya existe una del mismo comprador sobre el mismo elemento, devuelve
    /// esa (indice unico <c>ux_context_buyer</c>: dos toques seguidos no crean dos conversaciones).
    /// </summary>
    /// <param name="conversation">La conversacion nueva.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La conversacion guardada y si se acaba de crear.</returns>
    Task<(ConversationDocument Conversation, bool Created)> InsertOrGetAsync(ConversationDocument conversation, CancellationToken cancellationToken);

    /// <summary>Una conversacion por id.</summary>
    /// <param name="id">Id (ObjectId en texto).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La conversacion o <c>null</c> (tambien si el id no es un ObjectId).</returns>
    Task<ConversationDocument?> FindByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>
    /// Registra un mensaje en la conversacion: guarda la copia del ultimo mensaje, adelanta la hora de
    /// actividad (nunca la regresa) y suma uno a los no leidos de quien lo recibe (HU-22).
    /// </summary>
    /// <param name="id">Conversacion.</param>
    /// <param name="last">El mensaje.</param>
    /// <param name="recipientRole">Quien lo recibe: <see cref="ChatService.BuyerRole"/> o <see cref="ChatService.SellerRole"/>.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task RecordMessageAsync(string id, LastMessage last, string recipientRole, CancellationToken cancellationToken);

    /// <summary>Deja en cero los no leidos de un lado de la conversacion (HU-22).</summary>
    /// <param name="id">Conversacion.</param>
    /// <param name="role">Quien leyo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task MarkReadAsync(string id, string role, CancellationToken cancellationToken);

    /// <summary>
    /// Bandeja (HU-22): conversaciones con al menos un mensaje donde participa el usuario, de la mas
    /// reciente a la mas vieja (por <c>lastMessageAt</c> y luego por id), paginadas por cursor.
    /// </summary>
    /// <param name="query">La consulta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las conversaciones.</returns>
    Task<IReadOnlyList<ConversationDocument>> FindInboxPageAsync(InboxQuery query, CancellationToken cancellationToken);

    /// <summary>Ids de las conversaciones donde participa el usuario.</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los ids.</returns>
    Task<IReadOnlyList<string>> FindIdsByParticipantAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Borra las conversaciones donde participa el usuario (HU-46, R11).</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se borraron.</returns>
    Task<long> DeleteByParticipantAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Mensajes.</summary>
internal interface IMessageRepository
{
    /// <summary>Inserta un mensaje.</summary>
    /// <param name="message">El mensaje.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task InsertAsync(MessageDocument message, CancellationToken cancellationToken);

    /// <summary>Mensajes de una conversacion, del mas nuevo al mas viejo, paginados por cursor (R2).</summary>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="beforeId">Cursor: solo ids menores que este.</param>
    /// <param name="limit">Maximo de resultados.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los mensajes.</returns>
    Task<IReadOnlyList<MessageDocument>> FindPageAsync(string conversationId, string? beforeId, int limit, CancellationToken cancellationToken);

    /// <summary>Borra los mensajes de esas conversaciones (HU-46, R11).</summary>
    /// <param name="conversationIds">Conversaciones.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se borraron.</returns>
    Task<long> DeleteByConversationsAsync(IReadOnlyList<string> conversationIds, CancellationToken cancellationToken);
}
