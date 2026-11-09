using System;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
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
/// NOTE (HU-30): avisar a quien busca (RF-40). NOTE (HU-17): ver, aceptar o rechazar las ofertas.
/// </summary>
internal sealed class OfferService(
    IOfferRepository offers,
    IWantedRepository requests,
    IMemberDirectory members,
    IProductDirectory products,
    IListingPhotos photos,
    IBlockDirectory blocks,
    IContentModerator moderator,
    TimeProvider time)
{
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

        var (kind, productId, title, priceCents, condition, photoKey) = draft.Value;

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

    private async Task<Result<(string Kind, string? ProductId, string Title, long PriceCents, string Condition, string? PhotoKey)>> ListingAsync(
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

        return Result<(string, string?, string, long, string, string?)>.Ok(
            (OfferKinds.Listing, product.Id, product.Title, product.PriceCents, product.Condition, product.PhotoKey));
    }

    private async Task<Result<(string Kind, string? ProductId, string Title, long PriceCents, string Condition, string? PhotoKey)>> DirectAsync(
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

        return Result<(string, string?, string, long, string, string?)>.Ok(
            (OfferKinds.Direct, null, description, body.PriceCents.Value, body.Condition, photo));
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
}
