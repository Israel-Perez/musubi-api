using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Media;

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
/// <param name="Available">Disponible: se puede comprar o vincular a una oferta (HU-16).</param>
/// <param name="PhotoKey">Llave de la portada (<c>media/products/...</c>), o <c>null</c>.</param>
/// <param name="Stock">Piezas disponibles (HU-23, RN-17).</param>
internal sealed record ProductSummary(
    string Id,
    string CommunityId,
    string SellerId,
    string Title,
    long PriceCents,
    string Condition,
    string? ConditionDetail,
    string? Photo,
    bool Visible,
    bool Available = false,
    string? PhotoKey = null,
    int Stock = 0);

/// <summary>
/// Contrato publico de M-03 para los demas modulos (Modules/README: un modulo no lee las colecciones de
/// otro). Lo usan el chat (HU-20) para abrir una conversacion desde una publicacion, las ofertas (HU-16) para
/// vincular una publicacion propia y los pedidos (HU-23) para comprarla.
/// </summary>
internal interface IProductDirectory
{
    /// <summary>Una publicacion por id.</summary>
    /// <param name="productId">Id de la publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El resumen o <c>null</c> si no existe.</returns>
    Task<ProductSummary?> FindAsync(string productId, CancellationToken cancellationToken);
}

/// <summary>Una publicacion candidata para una solicitud (HU-19).</summary>
/// <param name="Id">Publicacion.</param>
/// <param name="SellerId">Quien la vende.</param>
/// <param name="Title">Nombre.</param>
/// <param name="PriceCents">Precio.</param>
/// <param name="Words">Palabras normalizadas de su nombre y su descripcion.</param>
internal sealed record ProductCandidate(string Id, string SellerId, string Title, long PriceCents, IReadOnlyList<string> Words);

/// <summary>Una publicacion recien publicada (HU-29).</summary>
/// <param name="Id">Publicacion.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="SellerId">Quien la vende.</param>
/// <param name="Title">Nombre.</param>
/// <param name="CategorySlug">Categoria.</param>
/// <param name="PriceCents">Precio.</param>
/// <param name="Words">Palabras normalizadas de su nombre y su descripcion.</param>
/// <param name="Condition"><c>new</c> o <c>used</c> (HU-12: filtro de la busqueda guardada).</param>
/// <param name="SellerName">Nombre corto de quien vende ("Luis M.").</param>
internal sealed record ProductPublished(
    string Id,
    string CommunityId,
    string SellerId,
    string Title,
    string CategorySlug,
    long PriceCents,
    IReadOnlyList<string> Words,
    string Condition = ProductConditions.Used,
    string SellerName = "");

/// <summary>
/// Contrato de M-03 hacia M-06 (HU-29): al publicar, las solicitudes "Se busca" que coincidan se revisan en segundo
/// plano (RNF-11). Nunca falla hacia quien publica.
/// </summary>
internal interface IProductPublications
{
    /// <summary>Encola la revision sin esperar.</summary>
    /// <param name="product">La publicacion.</param>
    /// <returns><c>true</c> si entro a la cola.</returns>
    bool Published(ProductPublished product);
}

/// <summary>Contrato de M-03 para M-06 (HU-19): las publicaciones que podrian servirle a una solicitud.</summary>
internal interface IProductMatches
{
    /// <summary>Las candidatas (ver <see cref="IProductRepository.FindMatchCandidatesAsync"/>).</summary>
    /// <param name="query">Lo que se busca.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las candidatas.</returns>
    Task<IReadOnlyList<ProductCandidate>> FindCandidatesAsync(ProductMatchQuery query, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de M-03.</summary>
internal sealed class ProductMatches(IProductRepository products) : IProductMatches
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductCandidate>> FindCandidatesAsync(ProductMatchQuery query, CancellationToken cancellationToken) =>
        [.. (await products.FindMatchCandidatesAsync(query, cancellationToken).ConfigureAwait(false))
            .Select(p => new ProductCandidate(p.Id, p.SellerId, p.Title, p.PriceCents, p.SearchWords))];
}

/// <summary>Implementacion sobre el repositorio de M-03.</summary>
internal sealed class ProductDirectory(IProductRepository products) : IProductDirectory
{
    /// <inheritdoc />
    public async Task<ProductSummary?> FindAsync(string productId, CancellationToken cancellationToken)
    {
        var product = await products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return null;
        }

        var cover = product.Photos.Count > 0 ? product.Photos[0] : null;
        return new ProductSummary(
            product.Id,
            product.CommunityId,
            product.SellerId,
            product.Title,
            product.PriceCents,
            product.Condition,
            product.ConditionDetail,
            product.Photos.Select(ListingService.PhotoUrl).FirstOrDefault(),
            product.Status is ProductStatuses.Active or ProductStatuses.SoldOut,
            product.Status == ProductStatuses.Active,
            cover,
            product.Stock);
    }
}

/// <summary>Fotos que una persona subio por <c>POST /products/photos</c> (HU-16: la foto de una oferta directa).</summary>
internal interface IListingPhotos
{
    /// <summary>La llave es de una foto que ya subio esa persona (OWASP API1).</summary>
    /// <param name="userId">Quien la usa.</param>
    /// <param name="key">Llave (<c>media/products/{userId}/{hex}.webp</c>).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si la llave es suya y el archivo existe.</returns>
    Task<bool> OwnsAsync(string userId, string key, CancellationToken cancellationToken);
}

/// <summary>Implementacion con la misma regla que las fotos de una publicacion.</summary>
internal sealed class ListingPhotos(IMediaStorage media) : IListingPhotos
{
    /// <inheritdoc />
    public async Task<bool> OwnsAsync(string userId, string key, CancellationToken cancellationToken) =>
        ListingService.IsOwnPhotoKey(userId, key) && await media.ExistsAsync(key, cancellationToken).ConfigureAwait(false);
}
