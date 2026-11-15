using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.Api.Modules.Listings;

/// <summary>Una publicacion de quien vende, con lo que necesita el apoyo a vendedores (M-12, HU-36/37/38).</summary>
/// <param name="Product">La publicacion, como la ve su dueno.</param>
/// <param name="Match">Lo que se compara con las solicitudes y las busquedas guardadas (M-06).</param>
internal sealed record SellerListing(ProductResponse Product, ProductPublished Match);

/// <summary>Contrato publico de M-03 para M-12 (Modules/README: un modulo no lee las colecciones de otro).</summary>
internal interface ISellerListings
{
    /// <summary>Las disponibles de quien vende publicadas hasta un momento, de la mas vieja a la mas nueva.</summary>
    /// <param name="sellerId">Quien vende.</param>
    /// <param name="listedBefore">Publicadas hasta (UTC).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones.</returns>
    Task<IReadOnlyList<SellerListing>> AvailableAsync(string sellerId, DateTime listedBefore, int limit, CancellationToken cancellationToken);

    /// <summary>Las disponibles de todos los campus publicadas entre dos momentos (el aviso de las 3 semanas).</summary>
    /// <param name="from">Desde (UTC).</param>
    /// <param name="to">Hasta (UTC).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones.</returns>
    Task<IReadOnlyList<SellerListing>> ListedBetweenAsync(DateTime from, DateTime to, int limit, CancellationToken cancellationToken);

    /// <summary>Una publicacion, solo para su dueno (RNF-03, OWASP API1).</summary>
    /// <param name="sellerId">Quien consulta (del token).</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion, o <c>null</c> si no existe, se quito o es de otra persona.</returns>
    Task<SellerListing?> FindOwnAsync(string sellerId, string productId, CancellationToken cancellationToken);

    /// <summary>Las vistas de cada publicacion desde un dia (HU-49).</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="productIds">Las publicaciones.</param>
    /// <param name="fromUtcDay">Primer dia (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las vistas por publicacion.</returns>
    Task<IReadOnlyDictionary<string, long>> ViewsSinceAsync(string communityId, IReadOnlyCollection<string> productIds, DateTime fromUtcDay, CancellationToken cancellationToken);

    /// <summary>"Productos parecidos se venden entre" (percentiles 25 y 75 de la categoria, HU-06).</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="category">Categoria.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El rango, o <c>null</c> con menos de 3 publicaciones.</returns>
    Task<PriceRangeResponse?> PriceRangeAsync(string communityId, string category, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre los repositorios de M-03.</summary>
internal sealed class SellerListings(IProductRepository products, IProductStatsRepository stats) : ISellerListings
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SellerListing>> AvailableAsync(string sellerId, DateTime listedBefore, int limit, CancellationToken cancellationToken) =>
        [.. (await products.FindAvailableListedAsync(sellerId, null, listedBefore, limit, cancellationToken).ConfigureAwait(false)).Select(ToListing)];

    /// <inheritdoc />
    public async Task<IReadOnlyList<SellerListing>> ListedBetweenAsync(DateTime from, DateTime to, int limit, CancellationToken cancellationToken) =>
        [.. (await products.FindAvailableListedAsync(null, from, to, limit, cancellationToken).ConfigureAwait(false)).Select(ToListing)];

    /// <inheritdoc />
    public async Task<SellerListing?> FindOwnAsync(string sellerId, string productId, CancellationToken cancellationToken)
    {
        var product = await products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);
        return product is null
            || !string.Equals(product.SellerId, sellerId, StringComparison.Ordinal)
            || product.Status == ProductStatuses.Removed
            ? null
            : ToListing(product);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, long>> ViewsSinceAsync(string communityId, IReadOnlyCollection<string> productIds, DateTime fromUtcDay, CancellationToken cancellationToken) =>
        stats.ViewsSinceAsync(communityId, productIds, fromUtcDay, cancellationToken);

    /// <inheritdoc />
    public Task<PriceRangeResponse?> PriceRangeAsync(string communityId, string category, CancellationToken cancellationToken) =>
        ListingService.PriceRangeAsync(products, communityId, category, cancellationToken);

    private static SellerListing ToListing(ProductDocument product) =>
        new(ListingService.ToResponse(product, includeSeller: true), ListingService.Publication(product));
}
