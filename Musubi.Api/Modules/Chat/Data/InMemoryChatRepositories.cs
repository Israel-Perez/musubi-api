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
    public Task RecordMessageAsync(string id, LastMessage last, string recipientRole, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(last);
        var toBuyer = string.Equals(recipientRole, ChatService.BuyerRole, StringComparison.Ordinal);
        Replace(id, current => Copy(
            current,
            last,
            last.At > current.LastMessageAt ? last.At : current.LastMessageAt,
            current.BuyerUnread + (toBuyer ? 1 : 0),
            current.SellerUnread + (toBuyer ? 0 : 1)));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkReadAsync(string id, string role, CancellationToken cancellationToken)
    {
        var buyer = string.Equals(role, ChatService.BuyerRole, StringComparison.Ordinal);
        Replace(id, current => Copy(
            current,
            current.LastMessage,
            current.LastMessageAt,
            buyer ? 0 : current.BuyerUnread,
            buyer ? current.SellerUnread : 0));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ConversationDocument>> FindInboxPageAsync(InboxQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<ConversationDocument>>(conversations
                .Where(c => c.LastMessage is not null && c.Participants.Contains(query.UserId, StringComparer.Ordinal))
                .Where(c => query.Role switch
                {
                    ChatService.BuyerRole => string.Equals(c.BuyerId, query.UserId, StringComparison.Ordinal),
                    ChatService.SellerRole => string.Equals(c.SellerId, query.UserId, StringComparison.Ordinal),
                    _ => true,
                })
                .Where(c => query.BeforeAt is not { } at || query.BeforeId is not { } beforeId
                    || c.LastMessageAt < at
                    || (c.LastMessageAt == at && string.CompareOrdinal(c.Id, beforeId) < 0))
                .OrderByDescending(c => c.LastMessageAt)
                .ThenByDescending(c => c.Id, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToList());
        }
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

    private static ConversationDocument Copy(ConversationDocument c, LastMessage? last, DateTime lastAt, int buyerUnread, int sellerUnread) => new()
    {
        Id = c.Id,
        CommunityId = c.CommunityId,
        BuyerId = c.BuyerId,
        SellerId = c.SellerId,
        Participants = c.Participants,
        BuyerName = c.BuyerName,
        SellerName = c.SellerName,
        Context = c.Context,
        LastMessage = last,
        LastMessageAt = lastAt,
        BuyerUnread = buyerUnread,
        SellerUnread = sellerUnread,
        CreatedAt = c.CreatedAt,
        SchemaVersion = c.SchemaVersion,
    };

    private void Replace(string id, Func<ConversationDocument, ConversationDocument> change)
    {
        lock (gate)
        {
            var index = conversations.FindIndex(c => string.Equals(c.Id, id, StringComparison.Ordinal));
            if (index >= 0)
            {
                conversations[index] = change(conversations[index]);
            }
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
