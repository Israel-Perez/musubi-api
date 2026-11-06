using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.Chat;

/// <summary>Resultado de iniciar una conversacion: la nueva (201) o la que ya existia (200).</summary>
/// <param name="Conversation">La conversacion.</param>
/// <param name="Created">Se acaba de crear.</param>
internal sealed record ConversationStart(ConversationResponse Conversation, bool Created);

/// <summary>
/// Iniciar una conversacion con el vendedor, enviar mensajes (HU-20) y la bandeja con no leidos (HU-22).
/// Reglas: RN-21 (cada conversacion es
/// entre dos usuarios y nace de un elemento), RN-22 (solo los participantes la leen) y 02-07 (el texto se
/// modera antes de guardarlo). NOTE (HU-28): reportar un mensaje (RN-23) llega con los reportes.
/// </summary>
internal sealed class ChatService(
    IConversationRepository conversations,
    IMessageRepository messages,
    IProductDirectory products,
    IMemberDirectory members,
    IContentModerator moderator,
    ChatNotifier notifier,
    TimeProvider time)
{
    /// <summary>Largo maximo de un mensaje.</summary>
    public const int MaxMessageLength = 1000;

    /// <summary>Caracteres del ultimo mensaje que se copian a la conversacion (bandeja, HU-22).</summary>
    public const int PreviewLength = 120;

    /// <summary>Mensajes por pagina por defecto.</summary>
    public const int DefaultPageSize = 30;

    /// <summary>Mensajes por pagina como maximo.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Conversaciones por pagina de la bandeja por defecto.</summary>
    public const int DefaultInboxSize = 30;

    /// <summary>Conversaciones por pagina de la bandeja como maximo.</summary>
    public const int MaxInboxSize = 50;

    /// <summary>Papel del comprador.</summary>
    public const string BuyerRole = "buyer";

    /// <summary>Papel del vendedor.</summary>
    public const string SellerRole = "seller";

    /// <summary>Nombre que se muestra si la otra cuenta ya no existe.</summary>
    public const string UnknownMember = "Usuario de Musubi";

    /// <summary>Abre (o reabre) la conversacion del comprador con el vendedor de una publicacion.</summary>
    /// <param name="userId">Comprador (del token).</param>
    /// <param name="request">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La conversacion o el error.</returns>
    public async Task<Result<ConversationStart>> StartFromProductAsync(string userId, StartConversationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var product = string.IsNullOrEmpty(request.ProductId)
            ? null
            : await products.FindAsync(request.ProductId, cancellationToken).ConfigureAwait(false);
        // Otro campus se trata como inexistente (RNF-21): no se revela.
        if (product is null || !product.Visible || !string.Equals(product.CommunityId, member.CommunityId, StringComparison.Ordinal))
        {
            return ChatErrors.ProductUnavailable;
        }

        if (string.Equals(product.SellerId, member.Id, StringComparison.Ordinal))
        {
            return ChatErrors.OwnProduct;
        }

        var seller = await members.FindMemberAsync(product.SellerId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow().UtcDateTime;
        var (conversation, created) = await conversations.InsertOrGetAsync(
            new ConversationDocument
            {
                Id = MongoConventions.NewId(),
                CommunityId = member.CommunityId,
                BuyerId = member.Id,
                SellerId = product.SellerId,
                Participants = [member.Id, product.SellerId],
                BuyerName = member.FullName,
                SellerName = seller?.FullName,
                Context = new ConversationContext { Type = ConversationContextTypes.Product, Id = product.Id, Title = product.Title, Photo = product.Photo },
                LastMessageAt = now,
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);
        var response = await ToResponseAsync(conversation, member.Id, product, cancellationToken).ConfigureAwait(false);
        return Result<ConversationStart>.Ok(new ConversationStart(response, created));
    }

    /// <summary>Una conversacion donde participa el usuario.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La conversacion o <c>chat.conversation_not_found</c>.</returns>
    public async Task<Result<ConversationResponse>> GetAsync(string userId, string conversationId, CancellationToken cancellationToken)
    {
        var conversation = await FindForParticipantAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return ChatErrors.ConversationNotFound;
        }

        var product = await products.FindAsync(conversation.Context.Id, cancellationToken).ConfigureAwait(false);
        return Result<ConversationResponse>.Ok(await ToResponseAsync(conversation, userId, product, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Mensajes de una conversacion, del mas nuevo al mas viejo.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina o el error.</returns>
    public async Task<Result<MessagePage>> MessagesAsync(string userId, string conversationId, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        if (cursor is not null && !ObjectId.TryParse(cursor, out _))
        {
            return ChatErrors.InvalidCursor;
        }

        var conversation = await FindForParticipantAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return ChatErrors.ConversationNotFound;
        }

        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        // Uno de mas para saber si hay otra pagina sin contar.
        var page = await messages.FindPageAsync(conversation.Id, cursor, size + 1, cancellationToken).ConfigureAwait(false);
        var items = page.Take(size).Select(ToResponse).ToList();
        return Result<MessagePage>.Ok(new MessagePage(items, page.Count > size ? items[^1].Id : null));
    }

    /// <summary>Envia un mensaje: se modera, se guarda y se entrega en tiempo real a los dos participantes.</summary>
    /// <param name="userId">Remitente (del token).</param>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="request">El texto.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El mensaje guardado o el error.</returns>
    public async Task<Result<MessageResponse>> SendAsync(string userId, string conversationId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = (request.Text ?? string.Empty).Trim();
        if (text.Length is 0 or > MaxMessageLength)
        {
            return ChatErrors.InvalidMessage;
        }

        var conversation = await FindForParticipantAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return ChatErrors.ConversationNotFound;
        }

        // Una cuenta suspendida (HU-40) ya no escribe, aunque su access token siga vigente unos minutos.
        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        // 02-07: el texto se modera antes de guardarlo.
        var verdicts = await moderator.CheckTextsAsync([text], cancellationToken).ConfigureAwait(false);
        if (ModerationErrors.For(verdicts[0], ModerationErrors.TextRejected("el mensaje")) is { } rejected)
        {
            return rejected;
        }

        var message = new MessageDocument
        {
            Id = MongoConventions.NewId(),
            ConversationId = conversation.Id,
            CommunityId = conversation.CommunityId,
            SenderId = userId,
            Text = text,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        await messages.InsertAsync(message, cancellationToken).ConfigureAwait(false);
        var recipientRole = string.Equals(conversation.BuyerId, userId, StringComparison.Ordinal) ? SellerRole : BuyerRole;
        await conversations.RecordMessageAsync(
            conversation.Id,
            new LastMessage { SenderId = userId, Text = text.Length > PreviewLength ? text[..PreviewLength] : text, At = message.CreatedAt },
            recipientRole,
            cancellationToken).ConfigureAwait(false);

        var response = ToResponse(message);
        // NOTE (HU-31): aviso push al otro participante si no tiene la app abierta.
        await notifier.MessageCreatedAsync(conversation.Participants, response, cancellationToken).ConfigureAwait(false);
        return Result<MessageResponse>.Ok(response);
    }

    /// <summary>Bandeja (HU-22): mis conversaciones con mensajes, de la mas reciente a la mas vieja.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <param name="role">Filtro: <c>buyer</c> ("Compras"), <c>seller</c> ("Ventas") o <c>null</c> ("Todos").</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina o el error.</returns>
    public async Task<Result<InboxPage>> InboxAsync(string userId, string? role, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        if (role is not null and not BuyerRole and not SellerRole)
        {
            return ChatErrors.InvalidFilter;
        }

        DateTime? beforeAt = null;
        string? beforeId = null;
        if (cursor is not null)
        {
            if (!TryParseInboxCursor(cursor, out var at, out var id))
            {
                return ChatErrors.InvalidCursor;
            }

            (beforeAt, beforeId) = (at, id);
        }

        var size = Math.Clamp(limit ?? DefaultInboxSize, 1, MaxInboxSize);
        var page = await conversations.FindInboxPageAsync(new InboxQuery(userId, role, beforeAt, beforeId, size + 1), cancellationToken).ConfigureAwait(false);
        var items = new List<InboxItemResponse>(Math.Min(size, page.Count));
        foreach (var conversation in page.Take(size))
        {
            var isBuyer = string.Equals(conversation.BuyerId, userId, StringComparison.Ordinal);
            var counterpartId = isBuyer ? conversation.SellerId : conversation.BuyerId;
            var name = (isBuyer ? conversation.SellerName : conversation.BuyerName)
                ?? (await members.FindMemberAsync(counterpartId, cancellationToken).ConfigureAwait(false))?.FullName
                ?? UnknownMember;
            var last = conversation.LastMessage!;
            items.Add(new InboxItemResponse(
                conversation.Id,
                isBuyer ? BuyerRole : SellerRole,
                new CounterpartResponse(counterpartId, name),
                new InboxContextResponse(conversation.Context.Type, conversation.Context.Id, conversation.Context.Title, conversation.Context.Photo),
                new LastMessageResponse(last.SenderId, last.Text, last.At),
                isBuyer ? conversation.BuyerUnread : conversation.SellerUnread));
        }

        var next = page.Count > size ? InboxCursor(page[size - 1]) : null;
        return Result<InboxPage>.Ok(new InboxPage(items, next));
    }

    /// <summary>Marca como leida la conversacion para quien la abre (HU-22). Idempotente.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Exito o <c>chat.conversation_not_found</c>.</returns>
    public async Task<Result<bool>> MarkReadAsync(string userId, string conversationId, CancellationToken cancellationToken)
    {
        var conversation = await FindForParticipantAsync(userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return ChatErrors.ConversationNotFound;
        }

        var role = string.Equals(conversation.BuyerId, userId, StringComparison.Ordinal) ? BuyerRole : SellerRole;
        await conversations.MarkReadAsync(conversation.Id, role, cancellationToken).ConfigureAwait(false);
        return Result<bool>.Ok(true);
    }

    /// <summary>Cursor de la bandeja: <c>ticks_id</c> de la ultima conversacion de la pagina.</summary>
    /// <param name="conversation">La conversacion.</param>
    /// <returns>El cursor.</returns>
    internal static string InboxCursor(ConversationDocument conversation) =>
        string.Create(CultureInfo.InvariantCulture, $"{conversation.LastMessageAt.Ticks}_{conversation.Id}");

    private static bool TryParseInboxCursor(string cursor, out DateTime at, out string id)
    {
        at = default;
        id = string.Empty;
        var parts = cursor.Split('_');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks
            || !ObjectId.TryParse(parts[1], out _))
        {
            return false;
        }

        at = new DateTime(ticks, DateTimeKind.Utc);
        id = parts[1];
        return true;
    }

    private static MessageResponse ToResponse(MessageDocument m) => new(m.Id, m.ConversationId, m.SenderId, m.Text, m.CreatedAt);

    private async Task<ConversationDocument?> FindForParticipantAsync(string userId, string conversationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(conversationId))
        {
            return null;
        }

        var conversation = await conversations.FindByIdAsync(conversationId, cancellationToken).ConfigureAwait(false);
        // RN-22: si no participa, es como si no existiera.
        return conversation is not null && conversation.Participants.Contains(userId, StringComparer.Ordinal) ? conversation : null;
    }

    private async Task<ConversationResponse> ToResponseAsync(ConversationDocument conversation, string userId, ProductSummary? product, CancellationToken cancellationToken)
    {
        var isBuyer = string.Equals(conversation.BuyerId, userId, StringComparison.Ordinal);
        var counterpartId = isBuyer ? conversation.SellerId : conversation.BuyerId;
        var copied = isBuyer ? conversation.SellerName : conversation.BuyerName;
        var name = copied ?? (await members.FindMemberAsync(counterpartId, cancellationToken).ConfigureAwait(false))?.FullName ?? UnknownMember;
        // NOTE (HU-21): "En linea" / "Activa hace 5 min" segun visibility.online del perfil.
        var available = product is { Visible: true };
        var context = new ConversationContextResponse(
            conversation.Context.Type,
            conversation.Context.Id,
            available ? product!.Title : conversation.Context.Title,
            available ? product!.PriceCents : null,
            available ? product!.Condition : null,
            available ? product!.ConditionDetail : null,
            available ? product!.Photo : null,
            available);
        return new ConversationResponse(
            conversation.Id,
            isBuyer ? BuyerRole : SellerRole,
            new CounterpartResponse(counterpartId, name),
            context,
            conversation.CreatedAt);
    }
}
