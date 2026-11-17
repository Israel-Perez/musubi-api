using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OutputCaching;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Caching;

namespace Musubi.Api.Modules.Listings;

/// <summary>Una publicacion, como la ve un administrador al revisar un reporte (HU-39).</summary>
/// <param name="Id">Publicacion.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="SellerId">Quien vende.</param>
/// <param name="Title">Nombre.</param>
/// <param name="Photo">URL de la portada, o <c>null</c>.</param>
/// <param name="Status">Estado (<c>active</c>, <c>soldOut</c>, <c>hidden</c>, <c>sold</c> o <c>removed</c>).</param>
internal sealed record ModeratedProduct(string Id, string CommunityId, string SellerId, string Title, string? Photo, string Status);

/// <summary>Contrato publico de M-03 para M-14 (Modules/README: un modulo no lee las colecciones de otro).</summary>
internal interface IProductModeration
{
    /// <summary>Una publicacion en cualquier estado.</summary>
    /// <param name="productId">Publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion o <c>null</c>.</returns>
    Task<ModeratedProduct?> FindAsync(string productId, CancellationToken cancellationToken);

    /// <summary>Varias publicaciones en una consulta, en cualquier estado.</summary>
    /// <param name="productIds">Publicaciones.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Id → publicacion; la que ya no existe no aparece.</returns>
    Task<IReadOnlyDictionary<string, ModeratedProduct>> FindManyAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken);

    /// <summary>"Retirar publicacion" (HU-39): queda <c>removed</c> y deja de verse.</summary>
    /// <param name="productId">Publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion retirada, o <c>null</c> si no existe o ya estaba retirada.</returns>
    Task<ModeratedProduct?> RemoveAsync(string productId, CancellationToken cancellationToken);

    /// <summary>Pausa lo disponible de quien vende al suspender su cuenta (HU-40).</summary>
    /// <param name="sellerId">Quien vende.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se pausaron.</returns>
    Task<long> HideAllAsync(string sellerId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de publicaciones.</summary>
internal sealed class ProductModeration(IProductRepository products, IOutputCacheStore outputCache, TimeProvider time) : IProductModeration
{
    /// <inheritdoc />
    public async Task<ModeratedProduct?> FindAsync(string productId, CancellationToken cancellationToken)
    {
        var product = await products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);
        return product is null ? null : ToModerated(product);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ModeratedProduct>> FindManyAsync(IReadOnlyCollection<string> productIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        IReadOnlyList<ProductDocument> found = productIds.Count == 0 ? [] : await products.FindByIdsAsync(productIds, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(p => p.Id, ToModerated, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<ModeratedProduct?> RemoveAsync(string productId, CancellationToken cancellationToken)
    {
        var removed = await products.RemoveAsync(productId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (removed is null)
        {
            return null;
        }

        // La vitrina publica se cachea (02-02): sin esto la publicacion se veria unos segundos mas.
        await outputCache.EvictByTagAsync(CachingServiceCollectionExtensions.ShowcaseTag, cancellationToken).ConfigureAwait(false);
        return ToModerated(removed);
    }

    /// <inheritdoc />
    public async Task<long> HideAllAsync(string sellerId, CancellationToken cancellationToken)
    {
        var hidden = await products.HideBySellerAsync(sellerId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (hidden > 0)
        {
            await outputCache.EvictByTagAsync(CachingServiceCollectionExtensions.ShowcaseTag, cancellationToken).ConfigureAwait(false);
        }

        return hidden;
    }

    private static ModeratedProduct ToModerated(ProductDocument product) => new(
        product.Id,
        product.CommunityId,
        product.SellerId,
        product.Title,
        product.Photos.Select(ListingService.PhotoUrl).FirstOrDefault(),
        product.Status);
}
