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

    /// <summary>Borra todas las publicaciones de un vendedor, en cualquier estado (HU-46, R11).</summary>
    /// <param name="sellerId">Vendedor.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se borraron.</returns>
    Task<long> DeleteBySellerAsync(string sellerId, CancellationToken cancellationToken);
}
