using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Chat.Data;

/// <summary>Conversaciones en MongoDB (indices <c>ux_context_buyer</c> e <c>ix_participant_lastAt</c>).</summary>
internal sealed class MongoConversationRepository(IMongoDatabase database) : IConversationRepository
{
    private readonly IMongoCollection<ConversationDocument> conversations = database.GetCollection<ConversationDocument>(MongoSchema.Conversations);

    /// <inheritdoc />
    public async Task<(ConversationDocument Conversation, bool Created)> InsertOrGetAsync(ConversationDocument conversation, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(conversation);
        try
        {
            await conversations.InsertOneAsync(conversation, cancellationToken: cancellationToken).ConfigureAwait(false);
            return (conversation, true);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // El indice unico resuelve la carrera de dos toques seguidos en "Mensaje".
            var existing = await conversations
                .Find(c => c.Context.Type == conversation.Context.Type && c.Context.Id == conversation.Context.Id && c.BuyerId == conversation.BuyerId)
                .FirstAsync(cancellationToken).ConfigureAwait(false);
            return (existing, false);
        }
    }

    /// <inheritdoc />
    public async Task<ConversationDocument?> FindByIdAsync(string id, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await conversations.Find(c => c.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public Task SetLastMessageAsync(string id, LastMessage last, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(last);
        // $max: si dos mensajes se guardan casi a la vez, la bandeja no retrocede.
        var update = Builders<ConversationDocument>.Update
            .Set(c => c.LastMessage, last)
            .Max(c => c.LastMessageAt, last.At);
        return conversations.UpdateOneAsync(c => c.Id == id, update, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindIdsByParticipantAsync(string userId, CancellationToken cancellationToken)
    {
        var ids = await conversations.Find(Builders<ConversationDocument>.Filter.AnyEq(c => c.Participants, userId))
            .Project(c => c.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.ToList();
    }

    /// <inheritdoc />
    public async Task<long> DeleteByParticipantAsync(string userId, CancellationToken cancellationToken)
    {
        var result = await conversations.DeleteManyAsync(Builders<ConversationDocument>.Filter.AnyEq(c => c.Participants, userId), cancellationToken)
            .ConfigureAwait(false);
        return result.DeletedCount;
    }
}

/// <summary>Mensajes en MongoDB (indice <c>ix_conversation_id</c>: pagina por cursor).</summary>
internal sealed class MongoMessageRepository(IMongoDatabase database) : IMessageRepository
{
    private static readonly FilterDefinitionBuilder<MessageDocument> Filter = Builders<MessageDocument>.Filter;

    private readonly IMongoCollection<MessageDocument> messages = database.GetCollection<MessageDocument>(MongoSchema.Messages);

    /// <inheritdoc />
    public Task InsertAsync(MessageDocument message, CancellationToken cancellationToken) =>
        messages.InsertOneAsync(message, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessageDocument>> FindPageAsync(string conversationId, string? beforeId, int limit, CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(m => m.ConversationId, conversationId);
        if (beforeId is not null)
        {
            filter &= Filter.Lt("_id", ObjectId.Parse(beforeId));
        }

        return await messages.Find(filter)
            .SortByDescending(m => m.Id)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> DeleteByConversationsAsync(IReadOnlyList<string> conversationIds, CancellationToken cancellationToken)
    {
        if (conversationIds is null || conversationIds.Count == 0)
        {
            return 0;
        }

        var result = await messages.DeleteManyAsync(Filter.In(m => m.ConversationId, conversationIds), cancellationToken).ConfigureAwait(false);
        return result.DeletedCount;
    }
}
