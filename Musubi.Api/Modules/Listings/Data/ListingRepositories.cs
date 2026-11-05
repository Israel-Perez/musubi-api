using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Musubi.Api.Modules.Listings.Data;

// Contratos de persistencia de M-03 (HU-06). Mongo* (nube y local) e InMemory* (UseInMemoryDatabase=true)
// pasan la misma bateria de pruebas (TestShared/Listings).

/// <summary>Consulta de la vitrina: lo mas nuevo primero, paginado por cursor (R2).</summary>
/// <param name="CommunityId">Comunidad (R1).</param>
/// <param name="CategorySlug">Solo esa categoria, o todas.</param>
/// <param name="IncludeSoldOut">Incluir los agotados ("Ocultar agotados" apagado).</param>
/// <param name="BeforeId">Cursor: solo ids menores que este.</param>
/// <param name="Limit">Maximo de resultados.</param>
internal sealed record ProductQuery(string CommunityId, string? CategorySlug, bool IncludeSoldOut, string? BeforeId, int Limit);

/// <summary>Categorias por comunidad.</summary>
internal interface ICategoryRepository
{
    /// <summary>Categorias activas de la comunidad, en su orden.</summary>
    /// <param name="communityId">Comunidad.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las categorias.</returns>
    Task<IReadOnlyList<CategoryDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken);
}

/// <summary>Publicaciones.</summary>
internal interface IProductRepository
{
    /// <summary>Inserta una publicacion nueva.</summary>
    /// <param name="product">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task InsertAsync(ProductDocument product, CancellationToken cancellationToken);

    /// <summary>Una publicacion por id, en cualquier estado.</summary>
    /// <param name="id">Id (ObjectId en texto).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion o <c>null</c> (tambien si el id no es un ObjectId).</returns>
    Task<ProductDocument?> FindByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Pagina de la vitrina (activas y, si se pide, agotadas), de la mas nueva a la mas vieja.</summary>
    /// <param name="query">La consulta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones.</returns>
    Task<IReadOnlyList<ProductDocument>> FindPageAsync(ProductQuery query, CancellationToken cancellationToken);

    /// <summary>Cuantas publicaciones devuelve la consulta sin paginar ("48 productos").</summary>
    /// <param name="query">La consulta (se ignoran cursor y limite).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El total.</returns>
    Task<long> CountAsync(ProductQuery query, CancellationToken cancellationToken);

    /// <summary>Publicaciones de un vendedor (todas menos las quitadas), de la mas nueva a la mas vieja.</summary>
    /// <param name="sellerId">Vendedor.</param>
    /// <param name="limit">Maximo de resultados.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones.</returns>
    Task<IReadOnlyList<ProductDocument>> FindBySellerAsync(string sellerId, int limit, CancellationToken cancellationToken);

    /// <summary>Precios (centavos) de las publicaciones activas de una categoria, hasta <paramref name="limit"/> recientes.</summary>
    /// <param name="communityId">Comunidad.</param>
    /// <param name="categorySlug">Categoria.</param>
    /// <param name="limit">Maximo de precios.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los precios, sin orden.</returns>
    Task<IReadOnlyList<long>> FindActivePricesAsync(string communityId, string categorySlug, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Cambia los datos editables de una publicacion (HU-07). El dueno va en el filtro (OWASP API1): con otro
    /// vendedor, un id que no existe o una publicacion quitada no cambia nada.
    /// </summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="sellerId">Vendedor (del token).</param>
    /// <param name="details">Datos ya validados.</param>
    /// <param name="now">Hora del cambio (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion ya cambiada, o <c>null</c> si no es suya, no existe o esta quitada.</returns>
    Task<ProductDocument?> UpdateDetailsAsync(string id, string sellerId, ProductDetails details, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Pausa (<c>hidden</c>) o reactiva una publicacion del vendedor (HU-07). Reactivar la deja en
    /// <c>active</c>, o en <c>soldOut</c> si no tiene piezas, en la misma operacion. Es idempotente.
    /// </summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="sellerId">Vendedor (del token).</param>
    /// <param name="paused"><c>true</c> para pausar; <c>false</c> para reactivar.</param>
    /// <param name="now">Hora del cambio (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion ya cambiada, o <c>null</c> si no es suya, no existe o esta quitada.</returns>
    Task<ProductDocument?> SetPausedAsync(string id, string sellerId, bool paused, DateTime now, CancellationToken cancellationToken);

    /// <summary>Borra una publicacion del vendedor (HU-07, borrado real). Una quitada por moderacion no se borra aqui.</summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="sellerId">Vendedor (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion borrada (para borrar sus fotos), o <c>null</c> si no es suya, no existe o esta quitada.</returns>
    Task<ProductDocument?> DeleteAsync(string id, string sellerId, CancellationToken cancellationToken);

    /// <summary>Borra todas las publicaciones de un vendedor, en cualquier estado (HU-46, R11).</summary>
    /// <param name="sellerId">Vendedor.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se borraron.</returns>
    Task<long> DeleteBySellerAsync(string sellerId, CancellationToken cancellationToken);
}

/// <summary>
/// Categorias con cache-aside (02-02): cambian poco y se leen en cada "Filtrar" y "Vender-3". Se guardan por
/// comunidad durante <see cref="Platform.Caching.CachingServiceCollectionExtensions.CatalogTtl"/>.
/// NOTE (HU-41): borrar la entrada al editar una categoria.
/// </summary>
internal sealed class CachedCategoryRepository(ICategoryRepository inner, Platform.Caching.AppCache cache) : ICategoryRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CategoryDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            "categories:" + communityId,
            Platform.Caching.CachingServiceCollectionExtensions.CatalogTtl,
            ct => inner.ListActiveAsync(communityId, ct),
            cancellationToken);
}
