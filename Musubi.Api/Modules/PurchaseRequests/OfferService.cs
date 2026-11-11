using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>
/// "Yo tengo este producto" (HU-16, RF-20): vincular una publicacion propia disponible o hacer una oferta directa
/// sin publicarla (RN-13: solo la ve quien busca). Una por persona y solicitud, editable (RN-14); nunca a una
/// solicitud propia (RN-06). Si el precio pasa el presupuesto se permite y la respuesta lo marca. El texto se
/// modera antes de guardarlo (02-07).
/// HU-17 (RF-21): quien busca ve las ofertas de su solicitud, abre el chat con quien ofrece, la rechaza o la
/// acepta (la conversacion se abre y el pedido sale de ella en HU-23).
/// NOTE (HU-30): avisar a quien busca (RF-40) y a quien ofrece cuando se elige o se rechaza su oferta.
/// </summary>
internal sealed class OfferService(
    IOfferRepository offers,
    IWantedRepository requests,
    IMemberDirectory members,
    IProductDirectory products,
    IListingPhotos photos,
    IBlockDirectory blocks,
    IProfileDirectory profiles,
    IConversationOpener chats,
    IContentModerator moderator,
    INotificationCenter center,
    TimeProvider time)
{
    /// <summary>Ofertas que se leen por solicitud (RN-14: una por persona; 100 es de sobra para un campus).</summary>
    public const int MaxReceived = 100;

    /// <summary>
    /// Las ofertas de mi solicitud (HU-17): pendientes y aceptadas, sin las de quien bloqueé o me bloqueó (HU-57),
    /// con el nombre, la foto, la carrera visible y la reputacion de quien ofrece.
    /// </summary>
    /// <param name="userId">Quien busca (del token).</param>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las ofertas o <c>wanted.not_found</c> si la solicitud no es mia.</returns>
    public async Task<Result<ReceivedOffersResponse>> ReceivedAsync(string userId, string requestId, CancellationToken cancellationToken)
    {
        var request = await FindMineAsync(userId, requestId, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return WantedErrors.NotFound;
        }

        var hidden = await blocks.HiddenForAsync(userId, cancellationToken).ConfigureAwait(false);
        var received = (await offers.FindByRequestAsync(request.Id, MaxReceived, cancellationToken).ConfigureAwait(false))
            .Where(o => o.Status != OfferStatuses.Rejected && !hidden.Contains(o.SellerId, StringComparer.Ordinal))
            .ToList();
        var people = received.Select(o => o.SellerId).Distinct(StringComparer.Ordinal).ToList();
        var names = await members.FindMembersAsync(people, cancellationToken).ConfigureAwait(false);
        var cards = await profiles.FindCardsAsync(request.CommunityId, people, cancellationToken).ConfigureAwait(false);
        return Result<ReceivedOffersResponse>.Ok(new ReceivedOffersResponse(received.Select(o =>
        {
            var card = cards.GetValueOrDefault(o.SellerId);
            return new ReceivedOfferResponse(
                o.Id,
                o.Kind,
                o.ProductId,
                o.Title,
                o.PriceCents,
                o.Condition,
                o.ConditionDetail,
                o.PhotoKey is null ? null : ListingService.PhotoUrl(o.PhotoKey),
                o.Message,
                o.Status,
                new OffererResponse(
                    o.SellerId,
                    names.GetValueOrDefault(o.SellerId)?.FullName ?? o.SellerName,
                    card?.Photo,
                    card?.Career,
                    card?.RatingAvg,
                    card?.RatingCount ?? 0),
                o.CreatedAt);
        }).ToList()));
    }

    /// <summary>"Rechazar" (HU-17): solo una oferta pendiente de mi solicitud.</summary>
    /// <param name="userId">Quien busca (del token).</param>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="offerId">La oferta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c>, o el error.</returns>
    public async Task<Result<bool>> RejectAsync(string userId, string requestId, string offerId, CancellationToken cancellationToken)
    {
        var (_, offer, error) = await FindReceivedAsync(userId, requestId, offerId, cancellationToken).ConfigureAwait(false);
        if (offer is null)
        {
            return error!;
        }

        if (!await offers.TryDecideAsync(offer.Id, OfferStatuses.Rejected, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false))
        {
            return OfferErrors.NotPending;
        }

        // HU-31: quien ofrecio se entera (RF-41).
        await center.NotifyAsync(
            new Notice(
                offer.SellerId,
                NotificationTypes.OfferRejected,
                "Eligieron otra oferta",
                $"{(await RequestTitleAsync(offer.RequestId, cancellationToken).ConfigureAwait(false)) ?? offer.Title}. Gracias por ofrecer.",
                NotificationTargets.Wanted,
                offer.RequestId,
                NotificationCategories.Requests),
            cancellationToken).ConfigureAwait(false);
        return Result<bool>.Ok(true);
    }

    /// <summary>
    /// "Aceptar" (HU-17): mientras la solicitud siga activa, la oferta pendiente queda aceptada y se abre la
    /// conversacion con quien la hizo. Aceptar de nuevo una ya aceptada solo devuelve la conversacion.
    /// NOTE (HU-23): el pedido sale de la oferta aceptada.
    /// </summary>
    /// <param name="userId">Quien busca (del token).</param>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="offerId">La oferta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La conversacion o el error.</returns>
    public async Task<Result<OfferChatResponse>> AcceptAsync(string userId, string requestId, string offerId, CancellationToken cancellationToken)
    {
        var (request, offer, error) = await FindReceivedAsync(userId, requestId, offerId, cancellationToken).ConfigureAwait(false);
        if (offer is null)
        {
            return error!;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (offer.Status == OfferStatuses.Sent)
        {
            if (WantedService.EffectiveStatus(request!, now) != WantedStatuses.Active)
            {
                return OfferErrors.RequestClosed;
            }

            if (!await offers.TryDecideAsync(offer.Id, OfferStatuses.Accepted, now, cancellationToken).ConfigureAwait(false))
            {
                return OfferErrors.NotPending;
            }

            var conversationId = await OpenChatAsync(offer, cancellationToken).ConfigureAwait(false);
            await center.NotifyAsync(
                new Notice(
                    offer.SellerId,
                    NotificationTypes.OfferAccepted,
                    $"{ChatService.FirstName(request!.RequesterName)} aceptó tu oferta",
                    $"{offer.Title}. Ya pueden platicar en el chat.",
                    NotificationTargets.Conversation,
                    conversationId,
                    NotificationCategories.Requests),
                cancellationToken).ConfigureAwait(false);
            return Result<OfferChatResponse>.Ok(new OfferChatResponse(conversationId));
        }

        return Result<OfferChatResponse>.Ok(new OfferChatResponse(await OpenChatAsync(offer, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>"Abrir chat" (HU-17): la conversacion con quien ofrece, sin decidir.</summary>
    /// <param name="userId">Quien busca (del token).</param>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="offerId">La oferta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La conversacion o el error.</returns>
    public async Task<Result<OfferChatResponse>> ChatAsync(string userId, string requestId, string offerId, CancellationToken cancellationToken)
    {
        var (_, offer, error) = await FindReceivedAsync(userId, requestId, offerId, cancellationToken).ConfigureAwait(false);
        return offer is null
            ? error!
            : Result<OfferChatResponse>.Ok(new OfferChatResponse(await OpenChatAsync(offer, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>Mi solicitud (solo quien la publico), en cualquier estado.</summary>
    private async Task<WantedRequestDocument?> FindMineAsync(string userId, string requestId, CancellationToken cancellationToken)
    {
        var request = string.IsNullOrEmpty(requestId) ? null : await requests.FindByIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        return request is not null && string.Equals(request.RequesterId, userId, StringComparison.Ordinal) ? request : null;
    }

    /// <summary>Una oferta no rechazada de mi solicitud, sin bloqueo con quien la hizo.</summary>
    private async Task<(WantedRequestDocument? Request, OfferDocument? Offer, ApiError? Error)> FindReceivedAsync(
        string userId,
        string requestId,
        string offerId,
        CancellationToken cancellationToken)
    {
        var request = await FindMineAsync(userId, requestId, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return (null, null, WantedErrors.NotFound);
        }

        var offer = string.IsNullOrEmpty(offerId) ? null : await offers.FindByIdAsync(offerId, cancellationToken).ConfigureAwait(false);
        if (offer is null
            || !string.Equals(offer.RequestId, request.Id, StringComparison.Ordinal)
            || await blocks.BetweenAsync(userId, offer.SellerId, cancellationToken).ConfigureAwait(false))
        {
            return (request, null, OfferErrors.OfferNotFound);
        }

        return offer.Status == OfferStatuses.Rejected ? (request, null, OfferErrors.NotPending) : (request, offer, null);
    }

    private Task<string> OpenChatAsync(OfferDocument offer, CancellationToken cancellationToken) =>
        chats.OpenForOfferAsync(
            new OfferConversation(
                offer.CommunityId,
                offer.RequesterId,
                offer.SellerId,
                offer.Id,
                offer.Title,
                offer.PhotoKey is null ? null : ListingService.PhotoUrl(offer.PhotoKey)),
            cancellationToken);

    /// <summary>Largo minimo de la descripcion de una oferta directa.</summary>
    public const int MinDescription = 3;

    /// <summary>Largo maximo de la descripcion y del mensaje.</summary>
    public const int MaxText = 300;

    /// <summary>Mi oferta en una solicitud (para editarla, RN-14).</summary>
    /// <param name="userId">Quien ofrece (del token).</param>
    /// <param name="communityId">Su comunidad (del token).</param>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La oferta, <c>wanted.not_found</c> u <c>offers.not_found</c>.</returns>
    public async Task<Result<OfferResponse>> MineAsync(string userId, string? communityId, string requestId, CancellationToken cancellationToken)
    {
        var (request, error) = await FindOpenAsync(userId, communityId, requestId, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return error!;
        }

        var offer = await offers.FindMineAsync(request.Id, userId, cancellationToken).ConfigureAwait(false);
        return offer is null ? OfferErrors.NotFound : Result<OfferResponse>.Ok(ToResponse(offer, request));
    }

    /// <summary>Envia o edita mi oferta (RN-14).</summary>
    /// <param name="userId">Quien ofrece (del token).</param>
    /// <param name="communityId">Su comunidad (del token).</param>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="body">La oferta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La oferta y si es nueva, o el error.</returns>
    public async Task<Result<(OfferResponse Offer, bool Created)>> SaveAsync(
        string userId,
        string? communityId,
        string requestId,
        OfferRequest body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var (request, error) = await FindOpenAsync(userId, communityId, requestId, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return error!;
        }

        var message = string.IsNullOrWhiteSpace(body.Message) ? null : body.Message.Trim();
        if (message is { Length: > MaxText })
        {
            return OfferErrors.InvalidMessage;
        }

        var draft = string.IsNullOrWhiteSpace(body.ProductId)
            ? await DirectAsync(userId, body, cancellationToken).ConfigureAwait(false)
            : await ListingAsync(userId, request.CommunityId, body.ProductId, cancellationToken).ConfigureAwait(false);
        if (draft.Error is not null)
        {
            return draft.Error;
        }

        var (kind, productId, title, priceCents, condition, photoKey, detail) = draft.Value!;

        // 02-07: se modera lo que escribio la persona (la publicacion vinculada ya se modero al publicarla).
        var verdicts = await moderator.CheckTextsAsync([kind == OfferKinds.Direct ? title : string.Empty, message ?? string.Empty], cancellationToken)
            .ConfigureAwait(false);
        var rejected = ModerationErrors.For(verdicts[0], ModerationErrors.TextRejected("la descripción"))
            ?? ModerationErrors.For(verdicts[1], ModerationErrors.TextRejected("el mensaje"));
        if (rejected is not null)
        {
            return rejected;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var existing = await offers.FindMineAsync(request.Id, userId, cancellationToken).ConfigureAwait(false);
        // HU-17: una oferta que quien busca ya acepto o rechazo no se cambia.
        if (existing is not null && existing.Status != OfferStatuses.Sent)
        {
            return OfferErrors.NotEditable;
        }

        var offer = new OfferDocument
        {
            Id = existing?.Id ?? MongoConventions.NewId(),
            CommunityId = request.CommunityId,
            RequestId = request.Id,
            RequesterId = request.RequesterId,
            SellerId = userId,
            SellerName = ListingService.ShortName(member.FullName),
            Kind = kind,
            ProductId = productId,
            Title = title,
            PriceCents = priceCents,
            Condition = condition,
            ConditionDetail = detail,
            PhotoKey = photoKey,
            Message = message,
            Status = OfferStatuses.Sent,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        if (existing is not null)
        {
            await offers.ReplaceAsync(offer, cancellationToken).ConfigureAwait(false);
            return Result<(OfferResponse, bool)>.Ok((ToResponse(offer, request), false));
        }

        if (!await offers.TryInsertAsync(offer, cancellationToken).ConfigureAwait(false))
        {
            // Dos envios a la vez: el otro gano; este queda como edicion (RN-14).
            var winner = await offers.FindMineAsync(request.Id, userId, cancellationToken).ConfigureAwait(false);
            var edited = new OfferDocument
            {
                Id = winner!.Id,
                CommunityId = offer.CommunityId,
                RequestId = offer.RequestId,
                RequesterId = offer.RequesterId,
                SellerId = offer.SellerId,
                SellerName = offer.SellerName,
                Kind = offer.Kind,
                ProductId = offer.ProductId,
                Title = offer.Title,
                PriceCents = offer.PriceCents,
                Condition = offer.Condition,
                ConditionDetail = offer.ConditionDetail,
                PhotoKey = offer.PhotoKey,
                Message = offer.Message,
                Status = offer.Status,
                CreatedAt = winner.CreatedAt,
                UpdatedAt = now,
            };
            await offers.ReplaceAsync(edited, cancellationToken).ConfigureAwait(false);
            return Result<(OfferResponse, bool)>.Ok((ToResponse(edited, request), false));
        }

        await requests.IncrementOffersAsync(request.Id, cancellationToken).ConfigureAwait(false);
        // HU-31: "Alguien tiene lo que buscas" (maqueta Notificaciones).
        await center.NotifyAsync(
            new Notice(
                request.RequesterId,
                NotificationTypes.OfferReceived,
                "Alguien tiene lo que buscas",
                $"{offer.SellerName} respondió “Yo tengo este producto” a tu solicitud de {request.Title} · {Pesos(offer.PriceCents)}.",
                NotificationTargets.Wanted,
                request.Id,
                NotificationCategories.Requests),
            cancellationToken).ConfigureAwait(false);
        return Result<(OfferResponse, bool)>.Ok((ToResponse(offer, request), true));
    }

    /// <summary>
    /// La solicitud a la que se puede ofrecer: activa, de mi campus y sin bloqueo (si no, <c>wanted.not_found</c>,
    /// sin revelar cual) y que no sea mia (RN-06).
    /// </summary>
    private async Task<(WantedRequestDocument? Request, ApiError? Error)> FindOpenAsync(
        string userId,
        string? communityId,
        string requestId,
        CancellationToken cancellationToken)
    {
        var request = string.IsNullOrEmpty(requestId) ? null : await requests.FindByIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        var now = time.GetUtcNow().UtcDateTime;
        if (request is null
            || !string.Equals(request.CommunityId, communityId, StringComparison.Ordinal)
            || WantedService.EffectiveStatus(request, now) != WantedStatuses.Active)
        {
            return (null, WantedErrors.NotFound);
        }

        if (string.Equals(request.RequesterId, userId, StringComparison.Ordinal))
        {
            return (null, OfferErrors.OwnRequest);
        }

        return await blocks.BetweenAsync(userId, request.RequesterId, cancellationToken).ConfigureAwait(false)
            ? (null, WantedErrors.NotFound)
            : (request, null);
    }

    private async Task<Result<OfferDraft>> ListingAsync(
        string userId,
        string communityId,
        string productId,
        CancellationToken cancellationToken)
    {
        var product = await products.FindAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is not { Available: true }
            || !string.Equals(product.SellerId, userId, StringComparison.Ordinal)
            || !string.Equals(product.CommunityId, communityId, StringComparison.Ordinal))
        {
            return OfferErrors.ProductUnavailable;
        }

        return Result<OfferDraft>.Ok(
            new OfferDraft(OfferKinds.Listing, product.Id, product.Title, product.PriceCents, product.Condition, product.PhotoKey, product.ConditionDetail));
    }

    private async Task<Result<OfferDraft>> DirectAsync(
        string userId,
        OfferRequest body,
        CancellationToken cancellationToken)
    {
        var description = (body.Description ?? string.Empty).Trim();
        if (description.Length is < MinDescription or > MaxText)
        {
            return OfferErrors.InvalidDescription;
        }

        if (body.PriceCents is not (>= 100 and <= ListingService.MaxPriceCents))
        {
            return OfferErrors.InvalidPrice;
        }

        if (body.Condition is not (ProductConditions.New or ProductConditions.Used))
        {
            return OfferErrors.InvalidCondition;
        }

        var photo = string.IsNullOrWhiteSpace(body.Photo) ? null : body.Photo;
        if (photo is not null && !await photos.OwnsAsync(userId, photo, cancellationToken).ConfigureAwait(false))
        {
            return OfferErrors.InvalidPhoto;
        }

        return Result<OfferDraft>.Ok(
            new OfferDraft(OfferKinds.Direct, null, description, body.PriceCents.Value, body.Condition, photo, null));
    }

    private static OfferResponse ToResponse(OfferDocument o, WantedRequestDocument request) => new(
        o.Id,
        o.RequestId,
        o.Kind,
        o.ProductId,
        o.Title,
        o.PriceCents,
        o.Condition,
        o.PhotoKey is null ? null : ListingService.PhotoUrl(o.PhotoKey),
        o.Message,
        o.Status,
        o.PriceCents > request.BudgetCents,
        o.CreatedAt,
        o.UpdatedAt);

    /// <summary>"$1,650" sin depender de la cultura del servidor (la imagen corre con InvariantGlobalization).</summary>
    /// <param name="cents">Centavos.</param>
    /// <returns>El precio.</returns>
    internal static string Pesos(long cents) =>
        "$" + (cents / 100m).ToString(cents % 100 == 0 ? "#,0" : "#,0.00", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<string?> RequestTitleAsync(string requestId, CancellationToken cancellationToken) =>
        (await requests.FindByIdAsync(requestId, cancellationToken).ConfigureAwait(false))?.Title;
}

/// <summary>Lo que se va a guardar de una oferta, ya validado.</summary>
internal sealed record OfferDraft(
    string Kind,
    string? ProductId,
    string Title,
    long PriceCents,
    string Condition,
    string? PhotoKey,
    string? ConditionDetail);
