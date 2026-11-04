using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Musubi.Api.Modules.Chat.Data;

// Implementaciones en memoria para UseInMemoryDatabase=true (dotnet run sin MongoDB y suites WAF).
// TestShared/Chat verifica que se comporten igual que las de Mongo.

/// <summary>Conversaciones en memoria.</summary>
internal sealed class InMemoryConversationRepository : IConversationRepository
{
    private readonly Lock gate = new();
    private readonly List<ConversationDocument> conversations = [];

    /// <inheritdoc />
    public Task<(ConversationDocument Conversation, bool Created)> InsertOrGetAsync(ConversationDocument conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        lock (gate)
        {
            // Misma regla que el indice unico ux_context_buyer.
            var existing = conversations.Find(c =>
                string.Equals(c.Context.Type, conversation.Context.Type, StringComparison.Ordinal)
                && string.Equals(c.Context.Id, conversation.Context.Id, StringComparison.Ordinal)
                && string.Equals(c.BuyerId, conversation.BuyerId, StringComparison.Ordinal));
            if (existing is not null)
            {
                return Task.FromResult((existing, false));
            }

            conversations.Add(conversation);
            return Task.FromResult((conversation, true));
        }
    }

    /// <inheritdoc />
    public Task<ConversationDocument?> FindByIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(conversations.Find(c => string.Equals(c.Id, id, StringComparison.Ordinal)));
        }
    }

    /// <inheritdoc />
    public Task SetLastMessageAsync(string id, LastMessage last, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(last);
        lock (gate)
        {
            var index = conversations.FindIndex(c => string.Equals(c.Id, id, StringComparison.Ordinal));
            if (index >= 0)
            {
                var current = conversations[index];
                conversations[index] = new ConversationDocument
                {
                    Id = current.Id,
                    CommunityId = current.CommunityId,
                    BuyerId = current.BuyerId,
                    SellerId = current.SellerId,
                    Participants = current.Participants,
                    Context = current.Context,
                    LastMessage = last,
                    LastMessageAt = last.At > current.LastMessageAt ? last.At : current.LastMessageAt,
                    CreatedAt = current.CreatedAt,
                    SchemaVersion = current.SchemaVersion,
                };
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> FindIdsByParticipantAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<string>>(conversations.Where(c => c.Participants.Contains(userId, StringComparer.Ordinal)).Select(c => c.Id).ToList());
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByParticipantAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)conversations.RemoveAll(c => c.Participants.Contains(userId, StringComparer.Ordinal)));
        }
    }
}

/// <summary>Mensajes en memoria (el id es un ObjectId nuevo: ordena por tiempo como en Mongo).</summary>
internal sealed class InMemoryMessageRepository : IMessageRepository
{
    private readonly Lock gate = new();
    private readonly List<MessageDocument> messages = [];

    /// <inheritdoc />
    public Task InsertAsync(MessageDocument message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (gate)
        {
            messages.Add(message);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MessageDocument>> FindPageAsync(string conversationId, string? beforeId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<MessageDocument>>(messages
                .Where(m => string.Equals(m.ConversationId, conversationId, StringComparison.Ordinal)
                    && (beforeId is null || string.CompareOrdinal(m.Id, beforeId) < 0))
                .OrderByDescending(m => m.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByConversationsAsync(IReadOnlyList<string> conversationIds, CancellationToken cancellationToken)
    {
        if (conversationIds is null || conversationIds.Count == 0)
        {
            return Task.FromResult(0L);
        }

        lock (gate)
        {
            return Task.FromResult((long)messages.RemoveAll(m => conversationIds.Contains(m.ConversationId, StringComparer.Ordinal)));
        }
    }
}
