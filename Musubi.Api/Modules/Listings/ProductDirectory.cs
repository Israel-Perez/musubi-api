using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings.Data;

namespace Musubi.Api.Modules.Listings;

/// <summary>Lo que otro modulo necesita saber de una publicacion (sin leer la coleccion <c>products</c>).</summary>
/// <param name="Id">Id de la publicacion.</param>
/// <param name="CommunityId">Comunidad.</param>
/// <param name="SellerId">Vendedor.</param>
/// <param name="Title">Nombre del producto.</param>
/// <param name="PriceCents">Precio en centavos.</param>
/// <param name="Condition"><c>new</c> o <c>used</c>.</param>
/// <param name="ConditionDetail">Detalle de un usado.</param>
/// <param name="Photo">Ruta de la portada (<c>/media/...</c>).</param>
/// <param name="Visible">Se puede ver en la vitrina: disponible o agotada (no pausada ni quitada).</param>
internal sealed record ProductSummary(
    string Id,
    string CommunityId,
    string SellerId,
    string Title,
    long PriceCents,
    string Condition,
    string? ConditionDetail,
    string? Photo,
    bool Visible);

/// <summary>
/// Contrato publico de M-03 para los demas modulos (Modules/README: un modulo no lee las colecciones de
/// otro). Lo usa el chat (HU-20) para abrir una conversacion desde una publicacion.
/// </summary>
internal interface IProductDirectory
{
    /// <summary>Una publicacion por id.</summary>
    /// <param name="productId">Id de la publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El resumen o <c>null</c> si no existe.</returns>
    Task<ProductSummary?> FindAsync(string productId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de M-03.</summary>
internal sealed class ProductDirectory(IProductRepository products) : IProductDirectory
{
    /// <inheritdoc />
    public async Task<ProductSummary?> FindAsync(string productId, CancellationToken cancellationToken)
    {
        var product = await products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);
        return product is null
            ? null
            : new ProductSummary(
                product.Id,
                product.CommunityId,
                product.SellerId,
                product.Title,
                product.PriceCents,
                product.Condition,
                product.ConditionDetail,
                product.Photos.Select(ListingService.PhotoUrl).FirstOrDefault(),
                product.Status is ProductStatuses.Active or ProductStatuses.SoldOut);
    }
}
