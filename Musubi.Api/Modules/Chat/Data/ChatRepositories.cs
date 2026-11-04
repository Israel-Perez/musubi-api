using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Musubi.Api.Modules.Chat.Data;

// Contratos de persistencia de M-07 (HU-20). Mongo* (nube y local) e InMemory* (UseInMemoryDatabase=true)
// pasan la misma bateria de pruebas (TestShared/Chat).

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

    /// <summary>Guarda la copia del ultimo mensaje y la hora de actividad.</summary>
    /// <param name="id">Conversacion.</param>
    /// <param name="last">El mensaje.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task SetLastMessageAsync(string id, LastMessage last, CancellationToken cancellationToken);

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
