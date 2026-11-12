using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Orders.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.Reputation;

/// <summary>
/// Calificar un pedido completado (HU-27 quien compra a quien vende; HU-45 al reves) y leer las opiniones de un perfil.
/// Reglas: solo un pedido completado, una vez por parte (RN-24), de 1 a 5 estrellas (RN-25) y hasta 7 dias despues
/// de completado (decision de la usuaria del 12-Nov-2026). La reputacion es un solo promedio (RF-36).
/// </summary>
internal sealed class RatingService(
    IOrderRatingGate orders,
    IRatingRepository ratings,
    IProfileRatings profiles,
    IMemberDirectory members,
    IBlockDirectory blocks,
    IContentModerator moderator,
    TimeProvider time)
{
    /// <summary>Caracteres del comentario.</summary>
    public const int MaxComment = 280;

    /// <summary>Opiniones por pagina si no se pide otro tamano.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tope de la pagina de opiniones.</summary>
    public const int MaxPageSize = 50;

    /// <summary>"Enviar calificacion".</summary>
    /// <param name="userId">Quien califica (del token).</param>
    /// <param name="orderId">El pedido.</param>
    /// <param name="request">Estrellas, lo que destaca y el comentario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La calificacion o el error.</returns>
    public async Task<Result<RatingResponse>> RateAsync(string userId, string orderId, RateOrderRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var order = await orders.FindAsync(orderId, cancellationToken).ConfigureAwait(false);
        var raterRole = order is null ? null : RoleOf(order, userId);
        if (order is null || raterRole is null)
        {
            // Inexistente o de otras personas: no revela cual (OWASP API1).
            return OrdersErrors.NotFound;
        }

        var account = await members.FindAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var ratedRole = raterRole == OrderRoles.Buyer ? RatedRoles.Seller : RatedRoles.Buyer;
        var (tags, comment, invalid) = Validate(request, ratedRole);
        if (invalid is not null)
        {
            return invalid;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (!order.Completed)
        {
            return ReputationErrors.NotCompleted;
        }

        if (raterRole == OrderRoles.Buyer ? order.BuyerRated : order.SellerRated)
        {
            return ReputationErrors.AlreadyRated;
        }

        if (order.RateUntil is { } until && now > until)
        {
            return ReputationErrors.Expired;
        }

        if (comment is not null)
        {
            var verdicts = await moderator.CheckTextsAsync([comment], cancellationToken).ConfigureAwait(false);
            if (ModerationErrors.For(verdicts[0], ModerationErrors.TextRejected("el comentario")) is { } rejected)
            {
                return rejected;
            }
        }

        // Primero se marca el pedido (atomico): de dos toques a la vez solo uno pasa.
        if (!await orders.MarkRatedAsync(order.Id, userId, raterRole, now, cancellationToken).ConfigureAwait(false))
        {
            return ReputationErrors.AlreadyRated;
        }

        var rating = new RatingDocument
        {
            Id = ObjectId.GenerateNewId().ToString(),
            CommunityId = order.CommunityId,
            OrderId = order.Id,
            RaterId = userId,
            RaterName = ListingService.ShortName(account.FullName),
            RatedId = ratedRole == RatedRoles.Seller ? order.SellerId : order.BuyerId,
            RatedRole = ratedRole,
            Stars = request.Stars!.Value,
            Tags = [.. tags],
            Comment = comment,
            CreatedAt = now,
        };
        if (!await ratings.TryInsertAsync(rating, cancellationToken).ConfigureAwait(false))
        {
            return ReputationErrors.AlreadyRated;
        }

        await profiles.AddAsync(
            rating.RatedId,
            order.CommunityId,
            ratedRole == RatedRoles.Seller ? order.SellerName : order.BuyerName,
            rating.Stars,
            tags.Contains(RatingTags.OnTime, StringComparer.Ordinal),
            now,
            cancellationToken).ConfigureAwait(false);
        return Result<RatingResponse>.Ok(new RatingResponse(rating.Stars, rating.Tags, rating.Comment, rating.CreatedAt));
    }

    /// <summary>
    /// "Opiniones" de un perfil (maqueta <c>Perfil-vendedor</c>): las que recibio, de la mas nueva a la mas vieja. Mismas
    /// reglas que el perfil (HU-05): de otro campus, inactiva o con un bloqueo responde <c>profiles.not_found</c>.
    /// </summary>
    /// <param name="viewerId">Quien consulta (del token).</param>
    /// <param name="viewerCommunityId">Su campus (del token).</param>
    /// <param name="userId">La persona del perfil.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 50; 20 por defecto).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina o el error.</returns>
    public async Task<Result<ProfileRatingPage>> ListAsync(
        string viewerId,
        string? viewerCommunityId,
        string userId,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return ProfilesErrors.NotFound;
        }

        if (cursor is not null && !ObjectId.TryParse(cursor, out _))
        {
            return ReputationErrors.InvalidCursor;
        }

        var account = await members.FindAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is not { Active: true }
            || !string.Equals(account.CommunityId, viewerCommunityId, StringComparison.Ordinal)
            || await blocks.BetweenAsync(viewerId, account.Id, cancellationToken).ConfigureAwait(false))
        {
            return ProfilesErrors.NotFound;
        }

        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var fetched = await ratings.FindByRatedAsync(account.Id, cursor, size + 1, cancellationToken).ConfigureAwait(false);
        var hasMore = fetched.Count > size;
        var page = hasMore ? fetched.Take(size).ToList() : fetched;
        return Result<ProfileRatingPage>.Ok(new ProfileRatingPage(
            [.. page.Select(r => new ProfileRatingResponse(r.Id, r.Stars, r.Tags, r.Comment, r.RaterName, r.RatedRole, r.CreatedAt))],
            hasMore ? page[^1].Id : null));
    }

    private static string? RoleOf(RateableOrder order, string userId)
    {
        if (string.Equals(order.BuyerId, userId, StringComparison.Ordinal))
        {
            return OrderRoles.Buyer;
        }

        return string.Equals(order.SellerId, userId, StringComparison.Ordinal) ? OrderRoles.Seller : null;
    }

    // Estrellas, "¿Que destacas?" de la lista de quien recibe la calificacion y el comentario.
    private static (IReadOnlyList<string> Tags, string? Comment, ApiError? Error) Validate(RateOrderRequest request, string ratedRole)
    {
        if (request.Stars is not (>= 1 and <= 5))
        {
            return ([], null, ReputationErrors.InvalidStars);
        }

        var allowed = RatingTags.For(ratedRole);
        var tags = (request.Tags ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (tags.Exists(t => !allowed.Contains(t, StringComparer.Ordinal)))
        {
            return ([], null, ReputationErrors.InvalidTags);
        }

        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment is { Length: > MaxComment })
        {
            return ([], null, ReputationErrors.InvalidComment);
        }

        // En el orden de la lista, como se ven en la maqueta.
        return ([.. allowed.Where(t => tags.Contains(t, StringComparer.Ordinal))], comment, null);
    }
}
