using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.Api.Modules.Listings;

/// <summary>Lo que el modelo de recomendaciones necesita de una publicacion (HU-35).</summary>
/// <param name="Id">Publicacion.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="SellerId">Quien la vende.</param>
/// <param name="Title">Nombre.</param>
/// <param name="Description">Descripcion.</param>
/// <param name="CategorySlug">Categoria.</param>
/// <param name="PriceCents">Precio.</param>
/// <param name="PublishedAt">Publicacion (UTC).</param>
internal sealed record CatalogItem(
    string Id,
    string CommunityId,
    string SellerId,
    string Title,
    string Description,
    string CategorySlug,
    long PriceCents,
    DateTime PublishedAt);

/// <summary>Contrato de M-03 para M-11 (HU-35): el catalogo que se recomienda y sus tarjetas.</summary>
internal interface IRecommendationCatalog
{
    /// <summary>Las disponibles de un campus, de la mas nueva a la mas vieja.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El catalogo.</returns>
    Task<IReadOnlyList<CatalogItem>> AvailableAsync(string communityId, int limit, CancellationToken cancellationToken);

    /// <summary>La publicacion si quien consulta la puede ver, con las reglas del detalle (HU-11); si no, <c>null</c>.</summary>
    /// <param name="viewerId">Quien consulta, o <c>null</c> sin sesion.</param>
    /// <param name="viewerCommunityId">Su campus, o <c>null</c> sin sesion.</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion o <c>null</c>.</returns>
    Task<CatalogItem?> FindVisibleAsync(string? viewerId, string? viewerCommunityId, string productId, CancellationToken cancellationToken);

    /// <summary>Las tarjetas de esas publicaciones, en el mismo orden, solo las que siguen disponibles.</summary>
    /// <param name="ids">Publicaciones.</param>
    /// <param name="includeSeller">Mostrar el vendedor (solo con sesion; RNF-05).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las tarjetas.</returns>
    Task<IReadOnlyList<ProductResponse>> CardsAsync(IReadOnlyList<string> ids, bool includeSeller, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio y las reglas de M-03.</summary>
internal sealed class RecommendationCatalog(IProductRepository products, ListingService listings) : IRecommendationCatalog
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogItem>> AvailableAsync(string communityId, int limit, CancellationToken cancellationToken) =>
        [.. (await products.FindAvailableAsync(communityId, limit, cancellationToken).ConfigureAwait(false)).Select(ToItem)];

    /// <inheritdoc />
    public async Task<CatalogItem?> FindVisibleAsync(string? viewerId, string? viewerCommunityId, string productId, CancellationToken cancellationToken) =>
        await listings.FindVisibleAsync(viewerId, viewerCommunityId, productId, cancellationToken).ConfigureAwait(false) is { } product
            ? ToItem(product)
            : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductResponse>> CardsAsync(IReadOnlyList<string> ids, bool includeSeller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        var found = (await products.FindByIdsAsync(ids, cancellationToken).ConfigureAwait(false))
            .Where(p => p.Status == ProductStatuses.Active)
            .ToDictionary(p => p.Id, StringComparer.Ordinal);
        return [.. ids.Where(found.ContainsKey).Select(id => ListingService.ToResponse(found[id], includeSeller))];
    }

    private static CatalogItem ToItem(ProductDocument p) =>
        new(p.Id, p.CommunityId, p.SellerId, p.Title, p.Description, p.CategorySlug, p.PriceCents, p.CreatedAt);
}
