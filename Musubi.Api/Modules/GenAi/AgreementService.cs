using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Chat.Data;
using Musubi.Api.Modules.GenAi.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.GenAi;

/// <summary>
/// El acuerdo con IA (CU-16, Flujo E). HU-32: la IA solo corre cuando un participante toca "Resumir acuerdo" (RN-39),
/// los dos lo saben por el aviso del chat (RN-40), sale lo minimo (RNF-06) y si la IA falla el chat sigue (RNF-15).
/// HU-33: la IA propone producto, precio, cantidad, lugar, fecha y condiciones; lo que no se dijo queda "No
/// identificado" (RN-42) y la IA no actua por nadie (RN-43). HU-34: los dos corrigen y confirman la misma version
/// (RN-41); "Crear pedido" lo hace M-08 con los datos ya llenados (RF-47).
/// </summary>
internal sealed class AgreementService(
    IAgreementRepository agreements,
    IAgreementExtractor extractor,
    IConversationDirectory conversations,
    IConversationNotices notices,
    IProductDirectory products,
    IOfferDirectory offers,
    IBlockDirectory blocks,
    IMemberDirectory members,
    IOpenOrders openOrders,
    IMeetingPointDirectory points,
    IContentModerator moderator,
    IOptions<GenAiOptions> options,
    TimeProvider time)
{
    /// <summary>Caracteres del producto (como el pedido).</summary>
    public const int MaxTitleLength = 80;

    /// <summary>Caracteres de las condiciones.</summary>
    public const int MaxConditionsLength = 200;

    /// <summary>Precio maximo por pieza (como una publicacion).</summary>
    public const long MaxPriceCents = ListingService.MaxPriceCents;

    /// <summary>RN-44: el acuerdo se borra solo a los 30 dias (decision de la usuaria, 10-Nov-2026).</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private static readonly TimeSpan CampusOffset = WantedService.CampusOffset;

    // Lo que se compra segun la conversacion.
    private sealed record ItemContext(string Title, long? ListedPriceCents, int MaxQuantity);

    /// <summary>"Resumir acuerdo" (maqueta <c>Chat</c>): la IA lee el chat y deja la propuesta.</summary>
    /// <param name="userId">Quien lo pide (del token).</param>
    /// <param name="body">La conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo o el error.</returns>
    public async Task<Result<AgreementResponse>> RequestAsync(string userId, RequestAgreementRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.ConversationId is not { } conversationId || !ObjectId.TryParse(conversationId, out _))
        {
            return AgreementsErrors.InvalidConversation;
        }

        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var conversation = await conversations.FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null || RoleOf(conversation, userId) is null)
        {
            return AgreementsErrors.ConversationNotFound;
        }

        if (await blocks.BetweenAsync(conversation.BuyerId, conversation.SellerId, cancellationToken).ConfigureAwait(false))
        {
            return AgreementsErrors.Blocked;
        }

        if (await openOrders.ForConversationAsync(conversation.Id, cancellationToken).ConfigureAwait(false))
        {
            return AgreementsErrors.OrderOpen;
        }

        var item = await ItemAsync(conversation, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return AgreementsErrors.ProductUnavailable;
        }

        var lines = await notices.RecentTextsAsync(conversation.Id, options.Value.MaxMessages, cancellationToken).ConfigureAwait(false);
        if (lines.Count == 0)
        {
            return AgreementsErrors.NothingToSummarize;
        }

        // RNF-15: sin proveedor no sale nada del servidor y la app abre el resumen a mano.
        if (!extractor.Enabled)
        {
            return AgreementsErrors.Unavailable;
        }

        var campus = await points.ListAsync(conversation.CommunityId, cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync(conversation, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow().UtcDateTime;
        var prompt = new AgreementPrompt(
            item.Title,
            item.ListedPriceCents,
            item.MaxQuantity,
            campus.Select(p => new PromptPoint(p.Slug, p.Name)).ToList(),
            DateOnly.FromDateTime(now + CampusOffset),
            lines.Select(l => new PromptLine(
                string.Equals(l.SenderId, conversation.BuyerId, StringComparison.Ordinal) ? TranscriptRedactor.Buyer : TranscriptRedactor.Seller,
                l.At + CampusOffset,
                TranscriptRedactor.Redact(l.Text, names.Buyer, names.Seller))).ToList());

        // RN-40: el chat avisa a los dos en cuanto el texto sale hacia la IA, salga bien o no.
        await notices.PostNoticeAsync(conversation.Id, userId, $"{FirstName(member.FullName)} pidió un resumen con IA", cancellationToken).ConfigureAwait(false);
        var extracted = await extractor.ExtractAsync(prompt, cancellationToken).ConfigureAwait(false);
        if (extracted is null)
        {
            return AgreementsErrors.Unavailable;
        }

        var proposal = await ProposalAsync(extracted, item, campus, now, cancellationToken).ConfigureAwait(false);
        var saved = await agreements.ReplaceForConversationAsync(
            new AgreementDocument
            {
                Id = MongoConventions.NewId(),
                CommunityId = conversation.CommunityId,
                ConversationId = conversation.Id,
                BuyerId = conversation.BuyerId,
                SellerId = conversation.SellerId,
                RequestedBy = userId,
                Proposal = proposal,
                NotIdentified = AgreementFields.Missing(proposal),
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = now + Retention,
            },
            cancellationToken).ConfigureAwait(false);
        await ChangedAsync(saved, cancellationToken).ConfigureAwait(false);
        return Result<AgreementResponse>.Ok(ToResponse(saved, userId, item.MaxQuantity, names));
    }

    /// <summary>El acuerdo vigente de una conversacion propia (el aviso del chat).</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="conversationId">La conversacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cero o un acuerdo, o el error.</returns>
    public async Task<Result<AgreementListResponse>> ByConversationAsync(string userId, string? conversationId, CancellationToken cancellationToken)
    {
        if (conversationId is null || !ObjectId.TryParse(conversationId, out _))
        {
            return AgreementsErrors.InvalidConversation;
        }

        var conversation = await conversations.FindAsync(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null || RoleOf(conversation, userId) is null)
        {
            return AgreementsErrors.ConversationNotFound;
        }

        var agreement = await agreements.FindByConversationAsync(conversationId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (agreement is null)
        {
            return Result<AgreementListResponse>.Ok(new AgreementListResponse([]));
        }

        var item = await ItemAsync(conversation, cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync(conversation, cancellationToken).ConfigureAwait(false);
        return Result<AgreementListResponse>.Ok(new AgreementListResponse([ToResponse(agreement, userId, item?.MaxQuantity ?? 1, names)]));
    }

    /// <summary>Corregir la propuesta (los lapices de <c>Acuerdo-esperando</c>). Borra las dos confirmaciones.</summary>
    /// <param name="userId">Quien corrige (del token).</param>
    /// <param name="id">El acuerdo.</param>
    /// <param name="body">Lo que cambia.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo o el error.</returns>
    public async Task<Result<AgreementResponse>> UpdateAsync(string userId, string id, UpdateAgreementRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Revision is not { } revision)
        {
            return AgreementsErrors.InvalidRevision;
        }

        var (agreement, conversation, failure) = await LoadAsync(userId, id, cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        if (agreement!.Revision != revision)
        {
            return AgreementsErrors.Changed;
        }

        var item = await ItemAsync(conversation!, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return AgreementsErrors.ProductUnavailable;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var (proposal, invalid) = await EditAsync(agreement.Proposal, body, item, conversation!.CommunityId, now, cancellationToken).ConfigureAwait(false);
        if (invalid is not null)
        {
            return invalid;
        }

        var changed = await agreements.UpdateProposalAsync(agreement.Id, revision, proposal!, now, cancellationToken).ConfigureAwait(false);
        if (changed is null)
        {
            return AgreementsErrors.Changed;
        }

        await ChangedAsync(changed, cancellationToken).ConfigureAwait(false);
        return Result<AgreementResponse>.Ok(ToResponse(changed, userId, item.MaxQuantity, await NamesAsync(conversation, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>"Confirmar acuerdo" (RN-41): vale para la version que se vio; con los dos queda confirmado.</summary>
    /// <param name="userId">Quien confirma (del token).</param>
    /// <param name="id">El acuerdo.</param>
    /// <param name="body">La version.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El acuerdo o el error.</returns>
    public async Task<Result<AgreementResponse>> ConfirmAsync(string userId, string id, ConfirmAgreementRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Revision is not { } revision)
        {
            return AgreementsErrors.InvalidRevision;
        }

        var (agreement, conversation, failure) = await LoadAsync(userId, id, cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        if (agreement!.Revision != revision)
        {
            return AgreementsErrors.Changed;
        }

        if (AgreementFields.Missing(agreement.Proposal).Count > 0)
        {
            return AgreementsErrors.Incomplete;
        }

        var now = time.GetUtcNow().UtcDateTime;
        // La fecha pudo quedar atras desde que se propuso: se corrige antes de confirmar.
        if (!OrderService.IsValidDeliveryTime(agreement.Proposal.At!.Value, now))
        {
            return AgreementsErrors.InvalidDeliveryTime;
        }

        if (await blocks.BetweenAsync(conversation!.BuyerId, conversation.SellerId, cancellationToken).ConfigureAwait(false))
        {
            return AgreementsErrors.Blocked;
        }

        var confirmed = await agreements.ConfirmAsync(agreement.Id, revision, RoleOf(conversation, userId)!, now, cancellationToken).ConfigureAwait(false);
        if (confirmed is null)
        {
            return AgreementsErrors.Changed;
        }

        await ChangedAsync(confirmed, cancellationToken).ConfigureAwait(false);
        var item = await ItemAsync(conversation, cancellationToken).ConfigureAwait(false);
        return Result<AgreementResponse>.Ok(ToResponse(confirmed, userId, item?.MaxQuantity ?? 1, await NamesAsync(conversation, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>Descartar el acuerdo: se borra y la conversacion sigue (HU-34).</summary>
    /// <param name="userId">Quien descarta (del token).</param>
    /// <param name="id">El acuerdo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Exito o el error.</returns>
    public async Task<Result<bool>> DiscardAsync(string userId, string id, CancellationToken cancellationToken)
    {
        var (agreement, _, failure) = await LoadAsync(userId, id, cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        await agreements.DeleteAsync(agreement!.Id, cancellationToken).ConfigureAwait(false);
        await ChangedAsync(agreement, cancellationToken).ConfigureAwait(false);
        return Result<bool>.Ok(true);
    }

    /// <summary>El papel de quien consulta, o <c>null</c> si no participa.</summary>
    /// <param name="conversation">La conversacion.</param>
    /// <param name="userId">Usuario.</param>
    /// <returns><c>buyer</c>, <c>seller</c> o <c>null</c>.</returns>
    internal static string? RoleOf(ConversationSummary conversation, string userId)
    {
        if (string.Equals(conversation.BuyerId, userId, StringComparison.Ordinal))
        {
            return AgreementRoles.Buyer;
        }

        return string.Equals(conversation.SellerId, userId, StringComparison.Ordinal) ? AgreementRoles.Seller : null;
    }

    /// <summary>"Ana Sofia Ramirez" → "Ana".</summary>
    /// <param name="fullName">Nombre completo.</param>
    /// <returns>El primer nombre.</returns>
    internal static string FirstName(string fullName) =>
        fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is [var first, ..] ? first : fullName;

    /// <summary>Convierte el dia y la hora del campus que propuso la IA en UTC, o <c>null</c> si falta uno.</summary>
    /// <param name="date">Dia.</param>
    /// <param name="time">Hora.</param>
    /// <returns>La fecha en UTC.</returns>
    internal static DateTime? CampusToUtc(DateOnly? date, TimeOnly? time) =>
        date is { } d && time is { } t
            ? DateTime.SpecifyKind(d.ToDateTime(t) - CampusOffset, DateTimeKind.Utc)
            : null;

    private static AgreementResponse ToResponse(AgreementDocument a, string userId, int maxQuantity, (string Buyer, string Seller) names) => new(
        a.Id,
        a.ConversationId,
        a.Revision,
        a.Status,
        string.Equals(a.BuyerId, userId, StringComparison.Ordinal) ? AgreementRoles.Buyer : AgreementRoles.Seller,
        string.Equals(a.RequestedBy, a.BuyerId, StringComparison.Ordinal) ? AgreementRoles.Buyer : AgreementRoles.Seller,
        new AgreementPartyResponse(a.BuyerId, names.Buyer),
        new AgreementPartyResponse(a.SellerId, names.Seller),
        new AgreementProposalResponse(
            a.Proposal.Title,
            a.Proposal.PriceCents,
            a.Proposal.Quantity,
            a.Proposal.PointSlug,
            a.Proposal.PointName,
            a.Proposal.Detail,
            a.Proposal.At,
            a.Proposal.Conditions),
        a.NotIdentified,
        AgreementFields.Missing(a.Proposal),
        maxQuantity,
        a.BuyerConfirmedAt,
        a.SellerConfirmedAt,
        a.CreatedAt,
        a.UpdatedAt,
        a.ExpiresAt);

    private Task ChangedAsync(AgreementDocument agreement, CancellationToken cancellationToken) =>
        notices.AgreementChangedAsync([agreement.BuyerId, agreement.SellerId], agreement.ConversationId, cancellationToken);

    // El acuerdo, si existe y quien consulta participa (OWASP API1: si no, es como si no existiera).
    private async Task<(AgreementDocument? Agreement, ConversationSummary? Conversation, ApiError? Error)> LoadAsync(
        string userId,
        string id,
        CancellationToken cancellationToken)
    {
        if (await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false) is not { Active: true })
        {
            return (null, null, ApiErrors.Forbidden);
        }

        var agreement = await agreements.FindByIdAsync(id, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (agreement is null
            || !(string.Equals(agreement.BuyerId, userId, StringComparison.Ordinal) || string.Equals(agreement.SellerId, userId, StringComparison.Ordinal)))
        {
            return (null, null, AgreementsErrors.NotFound);
        }

        var conversation = await conversations.FindAsync(agreement.ConversationId, cancellationToken).ConfigureAwait(false);
        return conversation is null ? (null, null, AgreementsErrors.NotFound) : (agreement, conversation, null);
    }

    private async Task<(string Buyer, string Seller)> NamesAsync(ConversationSummary conversation, CancellationToken cancellationToken)
    {
        var found = await members.FindMembersAsync([conversation.BuyerId, conversation.SellerId], cancellationToken).ConfigureAwait(false);
        return (
            found.GetValueOrDefault(conversation.BuyerId)?.FullName ?? conversation.BuyerName ?? ChatService.UnknownMember,
            found.GetValueOrDefault(conversation.SellerId)?.FullName ?? conversation.SellerName ?? ChatService.UnknownMember);
    }

    // La publicacion (o la oferta aceptada) y cuantas piezas se pueden pedir; null si ya no esta disponible.
    private async Task<ItemContext?> ItemAsync(ConversationSummary conversation, CancellationToken cancellationToken)
    {
        if (conversation.ContextType == ConversationContextTypes.Offer)
        {
            var offer = await offers.FindAsync(conversation.ContextId, cancellationToken).ConfigureAwait(false);
            if (offer is null || !offer.Accepted || !string.Equals(offer.SellerId, conversation.SellerId, StringComparison.Ordinal))
            {
                return null;
            }

            var max = offer.RequestQuantity;
            if (offer.ProductId is { } linkedId)
            {
                var linked = await products.FindAsync(linkedId, cancellationToken).ConfigureAwait(false);
                if (linked is not { Available: true })
                {
                    return null;
                }

                max = linked.Stock;
            }

            return new ItemContext(offer.Title, offer.PriceCents, Math.Clamp(max, 1, OrderService.MaxQuantity));
        }

        var product = await products.FindAsync(conversation.ContextId, cancellationToken).ConfigureAwait(false);
        return product is { Available: true } && string.Equals(product.SellerId, conversation.SellerId, StringComparison.Ordinal)
            ? new ItemContext(product.Title, product.PriceCents, Math.Clamp(product.Stock, 1, OrderService.MaxQuantity))
            : null;
    }

    // HU-33: cada valor de la IA se valida con las reglas del pedido; lo que no pasa queda "No identificado".
    private async Task<AgreementProposal> ProposalAsync(
        ExtractedAgreement e,
        ItemContext item,
        IReadOnlyList<MeetingPointSummary> campus,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var fallbackTitle = item.Title[..Math.Min(item.Title.Length, MaxTitleLength)];
        var title = Clean(e.Product, MaxTitleLength) ?? fallbackTitle;
        long? price = e.PriceMxn is { } mxn && mxn > 0 && decimal.Round(mxn * 100) is var cents && cents is >= 100 and <= MaxPriceCents
            ? (long)cents
            : null;
        int? quantity = e.Quantity is { } q && q >= 1 && q <= item.MaxQuantity ? q : null;
        var point = e.MeetingPoint is null ? null : campus.FirstOrDefault(p => string.Equals(p.Slug, e.MeetingPoint, StringComparison.Ordinal));
        var at = CampusToUtc(e.Date, e.Time);
        if (at is { } when && !OrderService.IsValidDeliveryTime(when, now))
        {
            at = null;
        }

        // 02-07: lo que la IA copio del chat se modera igual que si lo escribiera una persona.
        var texts = new[] { title, Clean(e.MeetingDetail, OrderService.MaxDetailLength), Clean(e.Conditions, MaxConditionsLength) };
        var present = texts.Where(t => t is not null).Select(t => t!).ToList();
        IReadOnlyList<ModerationVerdict> verdicts = present.Count == 0 ? [] : await moderator.CheckTextsAsync(present, cancellationToken).ConfigureAwait(false);
        var allowed = present.Where((_, i) => verdicts[i] == ModerationVerdict.Allowed).ToHashSet(StringComparer.Ordinal);
        string? Keep(string? t) => t is not null && allowed.Contains(t) ? t : null;

        return new AgreementProposal
        {
            Title = Keep(texts[0]) ?? fallbackTitle,
            PriceCents = price,
            Quantity = quantity,
            PointSlug = point?.Slug,
            PointName = point?.Name,
            Detail = Keep(texts[1]),
            At = at,
            Conditions = Keep(texts[2]),
        };
    }

    // Lo que corrige una persona: mismas reglas que el pedido, con su mensaje en espanol.
    private async Task<(AgreementProposal? Proposal, ApiError? Error)> EditAsync(
        AgreementProposal current,
        UpdateAgreementRequest body,
        ItemContext item,
        string communityId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var title = current.Title;
        if (body.Title is not null)
        {
            title = body.Title.Trim();
            if (title.Length is 0 or > MaxTitleLength)
            {
                return (null, AgreementsErrors.InvalidTitle);
            }
        }

        if (body.PriceCents is { } priceCents && priceCents is < 100 or > MaxPriceCents)
        {
            return (null, AgreementsErrors.InvalidPrice);
        }

        if (body.Quantity is { } quantity && (quantity < 1 || quantity > item.MaxQuantity))
        {
            return (null, AgreementsErrors.InvalidQuantity);
        }

        var (pointSlug, pointName) = (current.PointSlug, current.PointName);
        if (body.MeetingPoint is not null)
        {
            var point = (await points.ListAsync(communityId, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(p => string.Equals(p.Slug, body.MeetingPoint, StringComparison.Ordinal));
            if (point is null)
            {
                return (null, AgreementsErrors.InvalidMeetingPoint);
            }

            (pointSlug, pointName) = (point.Slug, point.Name);
        }

        var detail = body.MeetingDetail is null ? current.Detail : Clean(body.MeetingDetail, int.MaxValue);
        if (detail is { Length: > OrderService.MaxDetailLength })
        {
            return (null, AgreementsErrors.InvalidDetail);
        }

        var conditions = body.Conditions is null ? current.Conditions : Clean(body.Conditions, int.MaxValue);
        if (conditions is { Length: > MaxConditionsLength })
        {
            return (null, AgreementsErrors.InvalidConditions);
        }

        var at = current.At;
        if (body.DeliveryAt is { } requested)
        {
            at = requested.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(requested, DateTimeKind.Utc) : requested.ToUniversalTime();
            if (!OrderService.IsValidDeliveryTime(at.Value, now))
            {
                return (null, AgreementsErrors.InvalidDeliveryTime);
            }
        }

        // 02-07: solo se modera lo que cambio.
        var written = new List<(string Text, string Field)>();
        if (body.Title is not null)
        {
            written.Add((title, "el producto"));
        }

        if (body.MeetingDetail is not null && detail is not null)
        {
            written.Add((detail, "el detalle del lugar"));
        }

        if (body.Conditions is not null && conditions is not null)
        {
            written.Add((conditions, "las condiciones"));
        }

        if (written.Count > 0)
        {
            var verdicts = await moderator.CheckTextsAsync(written.ConvertAll(w => w.Text), cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < written.Count; i++)
            {
                if (ModerationErrors.For(verdicts[i], ModerationErrors.TextRejected(written[i].Field)) is { } rejected)
                {
                    return (null, rejected);
                }
            }
        }

        return (new AgreementProposal
        {
            Title = title,
            PriceCents = body.PriceCents ?? current.PriceCents,
            Quantity = body.Quantity ?? current.Quantity,
            PointSlug = pointSlug,
            PointName = pointName,
            Detail = detail,
            At = at,
            Conditions = conditions,
        }, null);
    }

    private static string? Clean(string? text, int max)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > max ? null : trimmed;
    }
}
