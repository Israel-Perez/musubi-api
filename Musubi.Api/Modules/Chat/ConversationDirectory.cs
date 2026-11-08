using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Chat.Data;

namespace Musubi.Api.Modules.Chat;

/// <summary>
/// Contrato publico de M-07 para los demas modulos (Modules/README: un modulo no lee las colecciones de otro).
/// Lo usan los reportes (HU-28) para confirmar que quien reporta desde el chat participa en la conversacion.
/// </summary>
internal interface IConversationDirectory
{
    /// <summary>Los dos participantes de una conversacion.</summary>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los ids, o <c>null</c> si no existe.</returns>
    Task<IReadOnlyList<string>?> FindParticipantsAsync(string conversationId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de conversaciones.</summary>
internal sealed class ConversationDirectory(IConversationRepository conversations) : IConversationDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> FindParticipantsAsync(string conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindByIdAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return conversation?.Participants;
    }
}
