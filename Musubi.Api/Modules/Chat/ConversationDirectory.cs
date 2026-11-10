using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat.Data;
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
}

/// <summary>Implementacion sobre el repositorio de conversaciones.</summary>
internal sealed class ConversationOpener(IConversationRepository conversations, IMemberDirectory members, TimeProvider time) : IConversationOpener
{
    /// <summary>Largo maximo del nombre del contexto (db/005).</summary>
    public const int MaxTitle = 80;

    /// <inheritdoc />
    public async Task<string> OpenForOfferAsync(OfferConversation conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        var names = await members.FindMembersAsync([conversation.BuyerId, conversation.SellerId], cancellationToken).ConfigureAwait(false);
        var title = conversation.Title.Length > MaxTitle ? conversation.Title[..(MaxTitle - 1)].TrimEnd() + "…" : conversation.Title;
        var now = time.GetUtcNow().UtcDateTime;
        var (document, _) = await conversations.InsertOrGetAsync(
            new ConversationDocument
            {
                Id = MongoConventions.NewId(),
                CommunityId = conversation.CommunityId,
                BuyerId = conversation.BuyerId,
                SellerId = conversation.SellerId,
                Participants = [conversation.BuyerId, conversation.SellerId],
                BuyerName = names.GetValueOrDefault(conversation.BuyerId)?.FullName,
                SellerName = names.GetValueOrDefault(conversation.SellerId)?.FullName,
                Context = new ConversationContext
                {
                    Type = ConversationContextTypes.Offer,
                    Id = conversation.OfferId,
                    Title = title,
                    Photo = conversation.Photo,
                },
                LastMessageAt = now,
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);
        return document.Id;
    }
}
