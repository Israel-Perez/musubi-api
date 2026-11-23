using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Chat;

/// <summary>
/// Contrato publico de M-07 para los demas modulos (Modules/README: un modulo no lee las colecciones de otro).
/// Lo usan los reportes (HU-28) para confirmar que quien reporta desde el chat participa en la conversacion, y los
/// pedidos (HU-23) para saber quien compra, quien vende y que se compra.
/// </summary>
internal interface IConversationDirectory
{
    /// <summary>Una conversacion por id (HU-23).</summary>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El resumen, o <c>null</c> si no existe.</returns>
    Task<ConversationSummary?> FindAsync(string conversationId, CancellationToken cancellationToken);

    /// <summary>Los dos participantes de una conversacion.</summary>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los ids, o <c>null</c> si no existe.</returns>
    Task<IReadOnlyList<string>?> FindParticipantsAsync(string conversationId, CancellationToken cancellationToken);

    /// <summary>Cuantas conversaciones se abrieron desde cada publicacion (HU-36: "0 mensajes" de M-12).</summary>
    /// <param name="productIds">Las publicaciones.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las conversaciones por publicacion (las que no tienen no aparecen).</returns>
    Task<IReadOnlyDictionary<string, int>> CountForProductsAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de conversaciones.</summary>
internal sealed class ConversationDirectory(IConversationRepository conversations) : IConversationDirectory
{
    /// <inheritdoc />
    public async Task<ConversationSummary?> FindAsync(string conversationId, CancellationToken cancellationToken)
    {
        var c = await conversations.FindByIdAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return c is null
            ? null
            : new ConversationSummary(c.Id, c.CommunityId, c.BuyerId, c.SellerId, c.BuyerName, c.SellerName, c.Context.Type, c.Context.Id);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>?> FindParticipantsAsync(string conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindByIdAsync(conversationId, cancellationToken).ConfigureAwait(false);
        return conversation?.Participants;
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, int>> CountForProductsAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken) =>
        conversations.CountByProductsAsync(productIds, cancellationToken);
}

/// <summary>Lo que otro modulo necesita saber de una conversacion (HU-23).</summary>
/// <param name="Id">Id.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="BuyerId">Quien compra (quien la inicio, o quien busca en una oferta).</param>
/// <param name="SellerId">Quien vende.</param>
/// <param name="BuyerName">Copia del nombre de quien compra, o <c>null</c> en una conversacion vieja.</param>
/// <param name="SellerName">Copia del nombre de quien vende, o <c>null</c>.</param>
/// <param name="ContextType"><c>product</c> u <c>offer</c> (RN-21).</param>
/// <param name="ContextId">La publicacion o la oferta.</param>
internal sealed record ConversationSummary(
    string Id,
    string CommunityId,
    string BuyerId,
    string SellerId,
    string? BuyerName,
    string? SellerName,
    string ContextType,
    string ContextId);

/// <summary>Una conversacion que nace de una oferta (HU-17).</summary>
/// <param name="CommunityId">Campus.</param>
/// <param name="BuyerId">Quien busca.</param>
/// <param name="SellerId">Quien ofrece.</param>
/// <param name="OfferId">La oferta (contexto).</param>
/// <param name="Title">Lo que se ofrece (se recorta a 80).</param>
/// <param name="Photo">Ruta de la foto (<c>/media/products/...</c>), o <c>null</c>.</param>
internal sealed record OfferConversation(string CommunityId, string BuyerId, string SellerId, string OfferId, string Title, string? Photo);

/// <summary>Una conversacion sobre una publicacion abierta desde otro modulo (HU-52: un apartado).</summary>
/// <param name="CommunityId">Campus.</param>
/// <param name="BuyerId">Quien aparta.</param>
/// <param name="SellerId">Quien vende.</param>
/// <param name="ProductId">La publicacion (contexto).</param>
/// <param name="Title">Nombre (se recorta a 80).</param>
/// <param name="Photo">Ruta de la portada, o <c>null</c>.</param>
internal sealed record ProductConversation(string CommunityId, string BuyerId, string SellerId, string ProductId, string Title, string? Photo);

/// <summary>
/// Contrato publico de M-07 para abrir una conversacion desde otro modulo (HU-17: "Abrir chat" y "Aceptar" en
/// una oferta). Una por oferta y comprador (<c>ux_context_buyer</c>): si ya existe, la devuelve.
/// </summary>
internal interface IConversationOpener
{
    /// <summary>Abre (o reabre) la conversacion de una oferta.</summary>
    /// <param name="conversation">Participantes y contexto.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El id de la conversacion.</returns>
    Task<string> OpenForOfferAsync(OfferConversation conversation, CancellationToken cancellationToken);

    /// <summary>Abre (o reabre) la conversacion de quien compra con quien vende sobre una publicacion (HU-52).</summary>
    /// <param name="conversation">Participantes y contexto.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El id de la conversacion.</returns>
    Task<string> OpenForProductAsync(ProductConversation conversation, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de conversaciones.</summary>
internal sealed class ConversationOpener(IConversationRepository conversations, IMemberDirectory members, TimeProvider time) : IConversationOpener
{
    /// <summary>Largo maximo del nombre del contexto (db/005).</summary>
    public const int MaxTitle = 80;

    /// <inheritdoc />
    public Task<string> OpenForOfferAsync(OfferConversation conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        return OpenAsync(
            conversation.CommunityId,
            conversation.BuyerId,
            conversation.SellerId,
            ConversationContextTypes.Offer,
            conversation.OfferId,
            conversation.Title,
            conversation.Photo,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> OpenForProductAsync(ProductConversation conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        return OpenAsync(
            conversation.CommunityId,
            conversation.BuyerId,
            conversation.SellerId,
            ConversationContextTypes.Product,
            conversation.ProductId,
            conversation.Title,
            conversation.Photo,
            cancellationToken);
    }

    // Una por contexto y comprador (ux_context_buyer): si ya existe, la devuelve.
    private async Task<string> OpenAsync(
        string communityId,
        string buyerId,
        string sellerId,
        string contextType,
        string contextId,
        string fullTitle,
        string? photo,
        CancellationToken cancellationToken)
    {
        var names = await members.FindMembersAsync([buyerId, sellerId], cancellationToken).ConfigureAwait(false);
        var title = fullTitle.Length > MaxTitle ? fullTitle[..(MaxTitle - 1)].TrimEnd() + "…" : fullTitle;
        var now = time.GetUtcNow().UtcDateTime;
        var (document, _) = await conversations.InsertOrGetAsync(
            new ConversationDocument
            {
                Id = MongoConventions.NewId(),
                CommunityId = communityId,
                BuyerId = buyerId,
                SellerId = sellerId,
                Participants = [buyerId, sellerId],
                BuyerName = names.GetValueOrDefault(buyerId)?.FullName,
                SellerName = names.GetValueOrDefault(sellerId)?.FullName,
                Context = new ConversationContext
                {
                    Type = contextType,
                    Id = contextId,
                    Title = title,
                    Photo = photo,
                },
                LastMessageAt = now,
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);
        return document.Id;
    }
}

/// <summary>Un mensaje de texto para la IA (HU-33): quien lo dijo y que dijo, sin ids de mensaje.</summary>
/// <param name="SenderId">Quien lo envio.</param>
/// <param name="Text">El texto.</param>
/// <param name="At">Envio (UTC).</param>
internal sealed record TranscriptLine(string SenderId, string Text, DateTime At);

/// <summary>
/// Contrato publico de M-07 para la IA (M-10, HU-32/HU-33): leer los ultimos mensajes de texto de una conversacion,
/// solo cuando un participante pide el resumen (RN-39), dejar el aviso en el chat (RN-40) y avisar que el acuerdo
/// cambio (HU-34).
/// </summary>
internal interface IConversationNotices
{
    /// <summary>Los ultimos mensajes de texto (sin avisos), del mas viejo al mas nuevo.</summary>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="limit">Cuantos como maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los mensajes.</returns>
    Task<IReadOnlyList<TranscriptLine>> RecentTextsAsync(string conversationId, int limit, CancellationToken cancellationToken);

    /// <summary>Deja un aviso en la conversacion y lo entrega en tiempo real (cuenta como no leido del otro lado).</summary>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="senderId">Quien lo provoco (uno de los participantes).</param>
    /// <param name="text">El aviso (hasta 120 caracteres).</param>
    /// <param name="pushTitle">
    /// El titulo del push de mensaje a la otra persona, o <c>null</c> si quien llama ya le manda su propio aviso (HU-62).
    /// </param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task PostNoticeAsync(string conversationId, string senderId, string text, string? pushTitle, CancellationToken cancellationToken);

    /// <summary>Avisa a los dos participantes que el acuerdo cambio (evento <c>agreementChanged</c>).</summary>
    /// <param name="participants">Los participantes.</param>
    /// <param name="conversationId">Conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task AgreementChangedAsync(IReadOnlyList<string> participants, string conversationId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre los repositorios del chat y el hub.</summary>
internal sealed class ConversationNotices(
    IConversationRepository conversations,
    IMessageRepository messages,
    ChatNotifier notifier,
    INotificationCenter center,
    TimeProvider time) : IConversationNotices
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TranscriptLine>> RecentTextsAsync(string conversationId, int limit, CancellationToken cancellationToken)
    {
        // Se piden algunos de mas por si hay avisos entre los textos.
        var page = await messages.FindPageAsync(conversationId, null, Math.Min(limit + 10, ChatService.MaxPageSize), cancellationToken).ConfigureAwait(false);
        var lines = new List<TranscriptLine>(limit);
        foreach (var m in page)
        {
            if (m.Kind is null && lines.Count < limit)
            {
                lines.Add(new TranscriptLine(m.SenderId, m.Text, m.CreatedAt));
            }
        }

        lines.Reverse();
        return lines;
    }

    /// <inheritdoc />
    public async Task PostNoticeAsync(string conversationId, string senderId, string text, string? pushTitle, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindByIdAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return;
        }

        var notice = text.Length > ChatService.PreviewLength ? text[..ChatService.PreviewLength] : text;
        var message = new MessageDocument
        {
            Id = MongoConventions.NewId(),
            ConversationId = conversation.Id,
            CommunityId = conversation.CommunityId,
            SenderId = senderId,
            Text = notice,
            Kind = MessageKinds.Notice,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        await messages.InsertAsync(message, cancellationToken).ConfigureAwait(false);
        var recipientRole = string.Equals(conversation.BuyerId, senderId, StringComparison.Ordinal) ? ChatService.SellerRole : ChatService.BuyerRole;
        await conversations.RecordMessageAsync(
            conversation.Id,
            new LastMessage { SenderId = senderId, Text = notice, At = message.CreatedAt },
            recipientRole,
            cancellationToken).ConfigureAwait(false);
        await notifier.MessageCreatedAsync(conversation.Participants, ChatService.ToResponse(message), cancellationToken).ConfigureAwait(false);
        if (pushTitle is null)
        {
            return;
        }

        // HU-31: la otra persona se entera aunque no tenga la app abierta (RN-40).
        var recipientId = string.Equals(conversation.BuyerId, senderId, StringComparison.Ordinal) ? conversation.SellerId : conversation.BuyerId;
        await center.NotifyAsync(
            new Notice(recipientId, NotificationTypes.Message, pushTitle, notice, NotificationTargets.Conversation, conversation.Id),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task AgreementChangedAsync(IReadOnlyList<string> participants, string conversationId, CancellationToken cancellationToken) =>
        notifier.AgreementChangedAsync(participants, conversationId, cancellationToken);
}
