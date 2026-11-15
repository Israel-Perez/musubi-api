using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Matching;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.SellerSupport;

/// <summary>
/// Apoyo a vendedores (M-12; decisiones de la usuaria del 15-Nov-2026). HU-36: las publicaciones disponibles que llevan
/// mas de 3 semanas sin venderse (RN-35, RN-36: sin pedidos completados ni abiertos), con sus vistas y mensajes. HU-38:
/// quien busca algo similar (RN-37). HU-37: "Ideas para venderla" (RN-38).
/// </summary>
internal sealed class SellerSupportService(
    ISellerListings listings,
    IProductDemand demand,
    IConversationDirectory conversations,
    IOpenOrders orders,
    TimeProvider time)
{
    /// <summary>Dias sin venderse a partir de los que una publicacion "lleva tiempo" ("mas de 3 semanas", maqueta).</summary>
    public const int ThresholdDays = 21;

    /// <summary>Dias de vistas que se cuentan (los agregados de HU-49 viven 30 dias).</summary>
    public const int ViewDays = 30;

    /// <summary>Publicaciones que se muestran como maximo.</summary>
    public const int MaxItems = 20;

    /// <summary>Solicitudes de "Quienes la buscan" como maximo.</summary>
    public const int MaxRequests = 20;

    /// <summary>Disponibles que se leen antes de quitar las que tienen pedidos.</summary>
    public const int MaxCandidates = 50;

    /// <summary>Lo que lleva tiempo sin venderse de quien consulta.</summary>
    /// <param name="userId">Quien vende (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones, de la que lleva mas dias a la que lleva menos.</returns>
    public async Task<SlowListingList> SlowAsync(string userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var slow = await SlowOfAsync(userId, now, cancellationToken).ConfigureAwait(false);
        if (slow.Count == 0)
        {
            return new SlowListingList([], ThresholdDays);
        }

        var ids = slow.Select(listing => listing.Product.Id).ToList();
        var communityId = slow[0].Match.CommunityId;
        var views = await listings.ViewsSinceAsync(communityId, ids, now.Date.AddDays(1 - ViewDays), cancellationToken).ConfigureAwait(false);
        var messages = await conversations.CountForProductsAsync(ids, cancellationToken).ConfigureAwait(false);
        var ranges = new Dictionary<string, PriceRangeResponse?>(StringComparer.Ordinal);
        var items = new List<SlowListingResponse>(slow.Count);
        foreach (var listing in slow)
        {
            var found = await demand.ForAsync(listing.Match, cancellationToken).ConfigureAwait(false);
            var category = listing.Match.CategorySlug;
            if (!ranges.TryGetValue(category, out var range))
            {
                range = await listings.PriceRangeAsync(communityId, category, cancellationToken).ConfigureAwait(false);
                ranges[category] = range;
            }

            items.Add(new SlowListingResponse(
                listing.Product,
                DaysListed(listing.Product.PublishedAt, now),
                views.GetValueOrDefault(listing.Product.Id),
                messages.GetValueOrDefault(listing.Product.Id),
                new DemandSummaryResponse(found.People, found.Requests.Count),
                SalesTips.For(listing.Product, found, range)));
        }

        return new SlowListingList(items, ThresholdDays);
    }

    /// <summary>"Quienes la buscan" de una publicacion propia (HU-38).</summary>
    /// <param name="userId">Quien vende (del token).</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La demanda o <c>listings.product_not_found</c> (inexistente, quitada o de otra persona: OWASP API1).</returns>
    public async Task<Result<ProductDemandResponse>> DemandAsync(string userId, string productId, CancellationToken cancellationToken)
    {
        if (await listings.FindOwnAsync(userId, productId, cancellationToken).ConfigureAwait(false) is not { } listing)
        {
            return ListingsErrors.ProductNotFound;
        }

        var found = await demand.ForAsync(listing.Match, cancellationToken).ConfigureAwait(false);
        return Result<ProductDemandResponse>.Ok(new ProductDemandResponse(
            found.People,
            found.AlertPeople,
            [.. found.Requests.Take(MaxRequests).Select(r => new DemandRequestResponse(r.Id, r.Title, r.BudgetCents, r.RequesterName, r.ExpiresAt))]));
    }

    /// <summary>Dias completos desde que se publico.</summary>
    /// <param name="publishedAt">Publicacion (UTC).</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <returns>Los dias.</returns>
    public static int DaysListed(DateTime publishedAt, DateTime now) => Math.Max(0, (int)(now - publishedAt).TotalDays);

    /// <summary>
    /// RN-35 y RN-36: las disponibles publicadas hace mas de 3 semanas que nunca se vendieron por un pedido completado
    /// y que no tienen uno pendiente o confirmado (esas ya se estan vendiendo).
    /// </summary>
    /// <param name="sellerId">Quien vende.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones.</returns>
    private async Task<IReadOnlyList<SellerListing>> SlowOfAsync(string sellerId, DateTime now, CancellationToken cancellationToken)
    {
        var old = await listings.AvailableAsync(sellerId, now.AddDays(-ThresholdDays), MaxCandidates, cancellationToken).ConfigureAwait(false);
        if (old.Count == 0)
        {
            return [];
        }

        return await WithoutOrdersAsync(old, orders, MaxItems, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Quita las que ya se vendieron por un pedido o tienen uno abierto (RN-36).</summary>
    /// <param name="candidates">Las candidatas, todas de la misma persona o de varias.</param>
    /// <param name="orders">Los pedidos (M-08).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las que siguen sin venderse.</returns>
    public static async Task<IReadOnlyList<SellerListing>> WithoutOrdersAsync(
        IReadOnlyList<SellerListing> candidates,
        IOpenOrders orders,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(orders);
        var sold = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);
        var result = new List<SellerListing>();
        foreach (var listing in candidates)
        {
            var sellerId = listing.Match.SellerId;
            if (!sold.TryGetValue(sellerId, out var soldIds))
            {
                soldIds = await orders.SoldProductIdsAsync(sellerId, cancellationToken).ConfigureAwait(false);
                sold[sellerId] = soldIds;
            }

            if (soldIds.Contains(listing.Product.Id)
                || await orders.ForProductAsync(listing.Product.Id, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            result.Add(listing);
            if (result.Count == limit)
            {
                break;
            }
        }

        return result;
    }
}
