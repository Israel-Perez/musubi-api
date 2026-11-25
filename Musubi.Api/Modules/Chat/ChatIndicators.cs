using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Chat.Data;

namespace Musubi.Api.Modules.Chat;

/// <summary>Contrato publico de M-07 para M-14 (HU-42, chat).</summary>
internal interface IChatIndicators
{
    /// <summary>Las conversaciones que empezaron en el periodo.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="from">Inicio (UTC, inclusivo).</param>
    /// <param name="to">Fin (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Sus ids.</returns>
    Task<IReadOnlyList<string>> StartedAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre las conversaciones.</summary>
internal sealed class ChatIndicators(IConversationRepository conversations) : IChatIndicators
{
    /// <summary>Conversaciones que se leen como maximo.</summary>
    public const int MaxConversations = 10_000;

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> StartedAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        conversations.StartedBetweenAsync(communityId, from, to, MaxConversations, cancellationToken);
}
