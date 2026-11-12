using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace Musubi.Api.Modules.Listings.Data;

// Contratos de persistencia de M-03 (HU-06). Mongo* (nube y local) e InMemory* (UseInMemoryDatabase=true)
// pasan la misma bateria de pruebas (TestShared/Listings).

/// <summary>Consulta de la vitrina, con los filtros de HU-10, paginada por cursor (R2).</summary>
/// <param name="CommunityId">Comunidad (R1).</param>
/// <param name="CategorySlugs">Categorias del filtro, en OR, ya recortadas y sin vacios; <c>null</c> para todas.</param>
/// <param name="IncludeSoldOut">Incluir los agotados ("Ocultar agotados" apagado).</param>
/// <param name="After">Cursor ya validado: la pagina siguiente empieza despues de el.</param>
/// <param name="Limit">Maximo de resultados.</param>
/// <param name="Search">Palabra clave (HU-09).</param>
/// <param name="MinPriceCents">Precio minimo, inclusivo; <c>null</c> si no hay piso (HU-10).</param>
/// <param name="MaxPriceCents">Precio maximo, inclusivo; <c>null</c> si no hay techo (HU-10).</param>
/// <param name="Condition"><c>new</c> o <c>used</c>; <c>null</c> para todas (HU-10).</param>
/// <param name="Sort">Ver <see cref="ProductSorts"/> (HU-10).</param>
/// <param name="ExcludeSellers">Vendedores que no se muestran: bloqueos de quien consulta en las dos direcciones (HU-57).</param>
/// <param name="Page">Pagina pedida (HU-10, base 1); con ella se ignora <paramref name="After"/>.</param>
/// <param name="SellerId">Solo las publicaciones de esta persona (HU-05: perfil publico); <c>null</c> para todas.</param>
internal sealed record ProductQuery(
    string CommunityId,
    IReadOnlyList<string>? CategorySlugs,
    bool IncludeSoldOut,
    ProductCursor? After,
    int Limit,
    string? Search = null,
    long? MinPriceCents = null,
    long? MaxPriceCents = null,
    string? Condition = null,
    string Sort = ProductSorts.Newest,
    IReadOnlyCollection<string>? ExcludeSellers = null,
    int? Page = null,
    string? SellerId = null);

/// <summary>Ordenes de la vitrina (HU-10 R5).</summary>
internal static class ProductSorts
{
    /// <summary>Relevancia: hoy se comporta como <see cref="Newest"/> (no hay puntaje).</summary>
    public const string Relevance = "relevance";
    // NOTE (HU-10): no hay puntaje de relevancia; un orden real queda para una HU posterior.

    /// <summary>Lo mas nuevo primero: <c>_id</c> descendente.</summary>
    public const string Newest = "newest";

    /// <summary>Lo mas viejo primero: <c>_id</c> ascendente.</summary>
    public const string Oldest = "oldest";

    /// <summary>Precio ascendente y, en empate, <c>_id</c> descendente.</summary>
    public const string PriceAsc = "priceAsc";

    /// <summary>Precio descendente y, en empate, <c>_id</c> descendente.</summary>
    public const string PriceDesc = "priceDesc";

    /// <summary>Comprueba que el valor sea uno de los ordenes publicos.</summary>
    /// <param name="value">Valor del query.</param>
    /// <returns><c>true</c> si es un orden conocido.</returns>
    public static bool IsDefined(string? value) =>
        value is Relevance or Newest or Oldest or PriceAsc or PriceDesc;

    /// <summary>El orden usa el precio como primera clave.</summary>
    /// <param name="value">Valor del query.</param>
    /// <returns><c>true</c> para <see cref="PriceAsc"/> y <see cref="PriceDesc"/>.</returns>
    public static bool IsPriceBased(string? value) => value is PriceAsc or PriceDesc;
}

/// <summary>Cursor de la vitrina (HU-10): el orden y la clave del ultimo resultado de la pagina.</summary>
/// <param name="Sort">Orden con el que se emitio (ver <see cref="ProductSorts"/>).</param>
/// <param name="PriceCents">Precio de la ultima clave cuando el orden es por precio; si no, <c>null</c>.</param>
/// <param name="Id">Id del ultimo resultado.</param>
internal sealed record ProductCursor(string Sort, long? PriceCents, string Id);

/// <summary>Token opaco del cursor (HU-10): codifica y valida <c>sort</c>, precio e id. No se firma; solo pagina.</summary>
internal static class ProductCursorCodec
{
    /// <summary>Token de la pagina siguiente.</summary>
    /// <param name="cursor">Orden y clave del ultimo resultado.</param>
    /// <returns>El token opaco.</returns>
    public static string Encode(ProductCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        return cursor.PriceCents is { } price
            ? $"{cursor.Sort}:{price.ToString(CultureInfo.InvariantCulture)}:{cursor.Id}"
            : $"{cursor.Sort}:{cursor.Id}";
    }

    /// <summary>Lee el token y comprueba que sea del orden pedido y que sus claves sean validas.</summary>
    /// <param name="token">Lo que manda el cliente.</param>
    /// <param name="sort">Orden de la pagina pedida.</param>
    /// <param name="maxPriceCents">Techo de precio para validar el token.</param>
    /// <param name="cursor">El cursor leido, cuando el token es valido.</param>
    /// <returns><c>true</c> si el token sirve para ese orden.</returns>
    public static bool TryDecode(string token, string sort, long maxPriceCents, out ProductCursor cursor)
    {
        cursor = null!;
        if (string.IsNullOrWhiteSpace(token) || !ProductSorts.IsDefined(sort))
        {
            return false;
        }

        // Compatibilidad con HU-09: el cursor crudo de ObjectId solo vale para el orden por _id descendente.
        if (!token.Contains(':', StringComparison.Ordinal))
        {
            if (sort is not (ProductSorts.Newest or ProductSorts.Relevance) || !ObjectId.TryParse(token, out var legacy))
            {
                return false;
            }

            cursor = new ProductCursor(sort, null, legacy.ToString());
            return true;
        }

        var parts = token.Split(':');
        if (!string.Equals(parts[0], sort, StringComparison.Ordinal))
        {
            return false;
        }

        if (ProductSorts.IsPriceBased(sort))
        {
            if (parts.Length != 3
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var price)
                || price < 0
                || price > maxPriceCents
                || !ObjectId.TryParse(parts[2], out var pricedId))
            {
                return false;
            }

            cursor = new ProductCursor(sort, price, pricedId.ToString());
            return true;
        }

        if (parts.Length != 2 || !ObjectId.TryParse(parts[1], out var id))
        {
            return false;
        }

        cursor = new ProductCursor(sort, null, id.ToString());
        return true;
    }
}

/// <summary>Normalizacion de una palabra de busqueda (HU-09): minusculas, sin acentos y sin el plural final.</summary>
internal static class SearchWord
{
    /// <summary>Forma comparable: <c>Calculadoras</c> y <c>calculadora</c> quedan iguales.</summary>
    /// <param name="word">Palabra escrita por la persona o leida del documento.</param>
    /// <returns>La palabra normalizada.</returns>
    public static string Normalize(string word)
    {
        var plain = string.Concat(word.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
            .ToLowerInvariant();
        if (plain.Length > 3 && plain.EndsWith("es", StringComparison.Ordinal))
        {
            return plain[..^2];
        }

        return plain.Length > 2 && plain.EndsWith('s') ? plain[..^1] : plain;
    }

    /// <summary>Palabras de un texto, normalizadas y sin repetir, para el indice de busqueda (HU-09).</summary>
    /// <param name="text">Titulo, descripcion o consulta.</param>
    /// <returns>Las palabras comparables.</returns>
    public static IReadOnlyList<string> Words(string text) =>
        [.. text.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Select(Normalize).Distinct()];

    private static readonly char[] Separators =
        [' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '(', ')', '-', '/', '"', '\''];
}

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

    /// <summary>Varias publicaciones por id, en cualquier estado (HU-53: lista de favoritos en lote).</summary>
    /// <param name="ids">Ids (ObjectId en texto); los invalidos se ignoran.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones que existan, sin orden garantizado.</returns>
    Task<IReadOnlyList<ProductDocument>> FindByIdsAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken);

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

    /// <summary>Publicaciones de un vendedor, de la mas nueva a la mas vieja (HU-08: por pestana y con cursor).</summary>
    /// <param name="sellerId">Vendedor.</param>
    /// <param name="statuses">Solo estos estados; <c>null</c> para todas menos las quitadas.</param>
    /// <param name="beforeId">Cursor: solo ids menores que este (ya validado), o <c>null</c>.</param>
    /// <param name="limit">Maximo de resultados.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las publicaciones.</returns>
    Task<IReadOnlyList<ProductDocument>> FindBySellerAsync(
        string sellerId,
        IReadOnlyList<string>? statuses,
        string? beforeId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Cuantas publicaciones tiene el vendedor en cada estado (sin las quitadas), en una sola consulta (HU-08).</summary>
    /// <param name="sellerId">Vendedor.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Estado → cuantas; los estados sin publicaciones no aparecen.</returns>
    Task<IReadOnlyDictionary<string, long>> CountBySellerAsync(string sellerId, CancellationToken cancellationToken);

    /// <summary>
    /// Marca vendida una publicacion del vendedor (HU-08) con la fecha de la venta. El dueno va en el filtro; una
    /// quitada o una ya vendida no cambia (la fecha de la primera venta se conserva).
    /// </summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="sellerId">Vendedor (del token).</param>
    /// <param name="now">Hora de la venta (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion vendida, o <c>null</c> si no cambio.</returns>
    Task<ProductDocument?> MarkSoldAsync(string id, string sellerId, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Aparta piezas de una publicacion activa al aceptar un pedido (HU-24, RF-34): resta solo si alcanzan y, si queda
    /// en cero, la pasa a agotada. Una sola escritura (dos pedidos a la vez no apartan de mas).
    /// </summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="quantity">Piezas (1 o mas).</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si se apartaron.</returns>
    Task<bool> TryReserveStockAsync(string id, int quantity, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Regresa las piezas de un pedido cancelado (HU-24, RN-20). Una agotada vuelve a estar disponible; una pausada,
    /// vendida o quitada conserva su estado.
    /// </summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="quantity">Piezas (1 o mas).</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si la publicacion existe.</returns>
    Task<bool> ReleaseStockAsync(string id, int quantity, DateTime now, CancellationToken cancellationToken);

    /// <summary>Una agotada sin piezas pasa a vendida con su fecha (HU-24, RN-05). Otra publicacion no cambia.</summary>
    /// <param name="id">Id de la publicacion.</param>
    /// <param name="now">Hora de la venta (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si paso a vendida.</returns>
    Task<bool> MarkSoldIfEmptyAsync(string id, DateTime now, CancellationToken cancellationToken);

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
    /// <returns>La publicacion ya cambiada, o <c>null</c> si no es suya, no existe, esta quitada o vendida (HU-08).</returns>
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
    /// <returns>La publicacion ya cambiada, o <c>null</c> si no es suya, no existe, esta quitada o vendida (HU-08).</returns>
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

/// <summary>Favoritos de una persona (HU-53).</summary>
internal interface IFavoriteRepository
{
    /// <summary>Guarda el favorito si no existia (idempotente: dos toques no duplican).</summary>
    /// <param name="favorite">El favorito.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si se creo.</returns>
    Task<bool> InsertIfAbsentAsync(FavoriteDocument favorite, CancellationToken cancellationToken);

    /// <summary>Favoritos de una persona, del mas reciente al mas viejo.</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="limit">Maximo de resultados.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los favoritos.</returns>
    Task<IReadOnlyList<FavoriteDocument>> FindByUserAsync(string userId, int limit, CancellationToken cancellationToken);

    /// <summary>Quita un favorito (idempotente).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="productId">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si existia.</returns>
    Task<bool> DeleteAsync(string userId, string productId, CancellationToken cancellationToken);

    /// <summary>Borra los favoritos de una cuenta que se elimina (HU-46, R11).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se borraron.</returns>
    Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken);
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
