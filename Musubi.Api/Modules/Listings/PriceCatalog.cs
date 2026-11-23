using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.Api.Modules.Listings;

/// <summary>Lo que el modelo de precios necesita de una publicacion (HU-58).</summary>
/// <param name="Id">Publicacion.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="Title">Nombre.</param>
/// <param name="CategorySlug">Categoria.</param>
/// <param name="Condition">Estado: <c>new</c>, o <c>used</c> con su detalle (<see cref="PriceCatalog.ConditionKey"/>).</param>
/// <param name="PriceCents">Precio publicado.</param>
internal sealed record PriceItem(string Id, string CommunityId, string Title, string CategorySlug, string Condition, long PriceCents);

/// <summary>Contrato de M-03 para M-11 (HU-58): las publicaciones con precio del campus.</summary>
internal interface IPriceCatalog
{
    /// <summary>Las publicaciones con un precio que sirve para estimar (disponibles, agotadas y vendidas).</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones.</returns>
    Task<IReadOnlyList<PriceItem>> PricedAsync(string communityId, int limit, CancellationToken cancellationToken);

    /// <summary>Esas publicaciones (cualquier estado), para saber la categoria y el estado de lo vendido.</summary>
    /// <param name="ids">Publicaciones.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las que existen.</returns>
    Task<IReadOnlyList<PriceItem>> ByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken);

    /// <summary>La publicacion si quien consulta la puede ver (reglas del detalle, HU-11); si no, <c>null</c>.</summary>
    /// <param name="viewerId">Quien consulta.</param>
    /// <param name="viewerCommunityId">Su campus.</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion o <c>null</c>.</returns>
    Task<PriceItem?> FindVisibleAsync(string viewerId, string viewerCommunityId, string productId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio y las reglas de M-03.</summary>
internal sealed class PriceCatalog(IProductRepository products, ListingService listings) : IPriceCatalog
{
    /// <summary>"new", "used:likeNew", "used:good" o "used:fair": el estado que ve quien compra.</summary>
    /// <param name="condition">Nuevo o usado.</param>
    /// <param name="detail">El detalle de un usado, o <c>null</c>.</param>
    /// <returns>La clave.</returns>
    public static string ConditionKey(string? condition, string? detail)
    {
        if (!string.Equals(condition, ProductConditions.Used, StringComparison.Ordinal))
        {
            return ProductConditions.New;
        }

        return $"{ProductConditions.Used}:{(string.IsNullOrWhiteSpace(detail) ? "good" : detail)}";
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceItem>> PricedAsync(string communityId, int limit, CancellationToken cancellationToken) =>
        [.. (await products.FindPricedAsync(communityId, limit, cancellationToken).ConfigureAwait(false)).Select(ToItem)];

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceItem>> ByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Count == 0 ? [] : [.. (await products.FindByIdsAsync(ids, cancellationToken).ConfigureAwait(false)).Select(ToItem)];
    }

    /// <inheritdoc />
    public async Task<PriceItem?> FindVisibleAsync(string viewerId, string viewerCommunityId, string productId, CancellationToken cancellationToken) =>
        await listings.FindVisibleAsync(viewerId, viewerCommunityId, productId, cancellationToken).ConfigureAwait(false) is { } product
            ? ToItem(product)
            : null;

    private static PriceItem ToItem(ProductDocument p) =>
        new(p.Id, p.CommunityId, p.Title, p.CategorySlug, ConditionKey(p.Condition, p.ConditionDetail), p.PriceCents);
}
