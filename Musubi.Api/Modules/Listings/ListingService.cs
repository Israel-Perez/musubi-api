using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Orders;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Caching;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.Listings;

/// <summary>Configuracion de la vitrina (seccion <c>Catalog</c>).</summary>
internal sealed class CatalogOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Catalog";

    /// <summary>Campus que ve un visitante si no pide otro (HU-48; el piloto es la UTTT).</summary>
    public string DefaultCampus { get; set; } = "uttt";
}

/// <summary>Quien mira la vitrina: con sesion, su usuario y comunidad; un visitante, ninguno.</summary>
/// <param name="UserId">Usuario del token (para ocultar a quien bloqueo o lo bloqueo, HU-57), o <c>null</c>.</param>
/// <param name="CommunityId">Comunidad del token (entonces se ve el vendedor), o <c>null</c>.</param>
internal sealed record ShowcaseViewer(string? UserId, string? CommunityId);

/// <summary>
/// Publicar productos (HU-06, HU-50), leer la vitrina (HU-48 fase 2) y el detalle (HU-11). Reglas: RN-03 (nombre, precio,
/// categoria, estado, cantidad y al menos una foto), RN-04 (precio mayor que cero y al menos una pieza).
/// </summary>
internal sealed partial class ListingService(
    IProductRepository products,
    IProductStatsRepository productStats,
    ICategoryRepository categories,
    IMemberDirectory members,
    IMediaStorage media,
    IContentModerator moderator,
    IBlockDirectory blocks,
    IOutputCacheStore outputCache,
    IOptions<CatalogOptions> catalog,
    IOpenOrders orders,
    TimeProvider time)
{
    /// <summary>Fotos por publicacion (maqueta: "Puedes subir hasta 8").</summary>
    public const int MaxPhotos = 8;

    /// <summary>Piezas maximas al publicar (maqueta <c>Vender-4</c>).</summary>
    public const int MaxStock = 10;

    /// <summary>Precio maximo: $999,999 (6 digitos en la maqueta).</summary>
    public const long MaxPriceCents = ProductRules.MaxPriceCents;

    /// <summary>Tamano de pagina por defecto y maximo.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tamano de pagina maximo.</summary>
    public const int MaxPageSize = 50;

    /// <summary>Pagina maxima: 50 con 20 por defecto son 1 000 resultados; acota el costo de `skip` (OWASP API4).</summary>
    public const int MaxPage = 50;

    /// <summary>Caracteres maximos del texto de busqueda (HU-09).</summary>
    public const int MaxSearchLength = 80;

    /// <summary>Terminos que se buscan como maximo; el resto de <c>q</c> se ignora (HU-09, RNF-09).</summary>
    public const int MaxSearchTerms = 5;

    /// <summary>Publicaciones minimas para mostrar un rango de precios honesto.</summary>
    public const int MinPricesForRange = 3;

    /// <summary>Categorias del campus: el del usuario con sesion, o el pedido (o el del piloto) para visitantes.</summary>
    /// <param name="userCommunityId">Comunidad del token, si hay sesion.</param>
    /// <param name="campus">Campus pedido por un visitante.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las categorias o el error.</returns>
    public async Task<Result<IReadOnlyList<CategoryResponse>>> CategoriesAsync(string? userCommunityId, string? campus, CancellationToken cancellationToken)
    {
        var communityId = userCommunityId ?? await FindCampusAsync(campus, cancellationToken).ConfigureAwait(false);
        if (communityId is null)
        {
            return ListingsErrors.UnknownCampus;
        }

        var list = await categories.ListActiveAsync(communityId, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<CategoryResponse>>.Ok(list.Select(c => new CategoryResponse(c.Slug, c.Name)).ToList());
    }

    /// <summary>Publica un producto en estado Disponible.</summary>
    /// <param name="userId">Vendedor (del token).</param>
    /// <param name="request">Datos de la maqueta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La publicacion o el error.</returns>
    public async Task<Result<ProductResponse>> PublishAsync(string userId, CreateProductRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var categoriesOfCampus = await categories.ListActiveAsync(member.CommunityId, cancellationToken).ConfigureAwait(false);
        var checkedDetails = ProductRules.Check(
            request.Title, request.Description, request.PriceCents, request.Condition, request.ConditionDetail, request.Category, categoriesOfCampus);
        if (checkedDetails.Error is { } invalid)
        {
            return invalid;
        }

        var details = checkedDetails.Value!;
        if (request.Stock is not { } stock || stock is < 1 or > MaxStock)
        {
            return ListingsErrors.InvalidStock;
        }

        if (!await ValidPhotosAsync(userId, request.Photos, cancellationToken).ConfigureAwait(false))
        {
            return ListingsErrors.InvalidPhotos;
        }

        // 02-07: nombre y descripcion se moderan antes de guardar (las fotos, al subirlas).
        var verdicts = await moderator.CheckTextsAsync([details.Title, details.Description], cancellationToken).ConfigureAwait(false);
        if (ModerationErrors.For(verdicts[0], ModerationErrors.TextRejected("el nombre del producto")) is { } badTitle)
        {
            return badTitle;
        }

        if (ModerationErrors.For(verdicts[1], ModerationErrors.TextRejected("la descripción")) is { } badDescription)
        {
            return badDescription;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var product = new ProductDocument
        {
            Id = MongoConventions.NewId(),
            CommunityId = member.CommunityId,
            SellerId = member.Id,
            Seller = new ProductSeller { Name = ShortName(member.FullName) },
            Title = details.Title,
            Description = details.Description,
            CategorySlug = details.CategorySlug,
            PriceCents = details.PriceCents,
            Condition = details.Condition,
            ConditionDetail = details.ConditionDetail,
            Stock = stock,
            Status = ProductStatuses.Active,
            Photos = request.Photos.ToList(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await products.InsertAsync(product, cancellationToken).ConfigureAwait(false);
        // La vitrina del visitante (cache de salida, 02-02) la muestra en la siguiente consulta.
        await outputCache.EvictByTagAsync(CachingServiceCollectionExtensions.ShowcaseTag, cancellationToken).ConfigureAwait(false);
        // NOTE (HU-18): avisar en segundo plano a las solicitudes "Se busca" que coincidan (RF-23, RNF-11).
        return Result<ProductResponse>.Ok(ToResponse(product, includeSeller: true));
    }

    /// <summary>
    /// Vitrina: lo recien publicado del campus, con los filtros de "Filtrar" y "Busqueda-filtros" (HU-09, HU-10).
    /// Con sesion no salen las publicaciones de quien bloqueaste o te bloqueo (HU-57).
    /// </summary>
    /// <param name="viewer">Quien consulta: usuario y comunidad del token, o vacios sin sesion (entonces no se ve el vendedor).</param>
    /// <param name="campus">Campus pedido por un visitante.</param>
    /// <param name="category">Slugs de las categorias (se pueden repetir), o todas.</param>
    /// <param name="minPriceCents">Precio minimo, inclusivo.</param>
    /// <param name="maxPriceCents">Precio maximo, inclusivo.</param>
    /// <param name="condition"><c>new</c>, <c>used</c> o todas.</param>
    /// <param name="sort">Orden de <see cref="ProductSorts"/>; <c>null</c> usa el mas nuevo.</param>
    /// <param name="q">Palabra clave: con ella solo salen las disponibles.</param>
    /// <param name="hideSoldOut">Ocultar agotados; sin el, la busqueda los oculta y la vitrina no.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina.</param>
    /// <param name="page">Pagina pedida (base 1); ignora <paramref name="cursor"/>.</param>
    /// <param name="sellerId">Solo las publicaciones de esta persona (HU-05: perfil publico); <c>null</c> para todas.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina o el error.</returns>
    public async Task<Result<ProductPage>> ShowcaseAsync(
        ShowcaseViewer viewer,
        string? campus,
        string[]? category,
        long? minPriceCents,
        long? maxPriceCents,
        string? condition,
        string? sort,
        string? q,
        bool? hideSoldOut,
        string? cursor,
        int? limit,
        int? page,
        string? sellerId,
        CancellationToken cancellationToken)
    {
        // HU-10: la pagina empieza en 1 y tiene tope para que skip no crezca sin control.
        if (page is < 1 or > MaxPage)
        {
            return ListingsErrors.InvalidPage;
        }

        // HU-05: el perfil solo usa ids reales; un valor mal formado no consulta ni ensucia la cache.
        if (string.IsNullOrWhiteSpace(sellerId))
        {
            sellerId = null;
        }
        else if (!ObjectId.TryParse(sellerId, out _))
        {
            return Result<ProductPage>.Ok(new ProductPage([], null, 0));
        }

        // HU-10 R3-R5: rango, condicion y orden tienen lista blanca antes de tocar la base.
        if (minPriceCents is < 0 or > MaxPriceCents
            || maxPriceCents is < 0 or > MaxPriceCents
            || (minPriceCents is { } min && maxPriceCents is { } max && min > max))
        {
            return ListingsErrors.InvalidPriceRange;
        }

        var conditionValue = string.IsNullOrWhiteSpace(condition) ? null : condition.Trim();
        if (conditionValue is not null && conditionValue is not (ProductConditions.New or ProductConditions.Used))
        {
            return ListingsErrors.InvalidCondition;
        }

        var sortValue = string.IsNullOrWhiteSpace(sort) ? ProductSorts.Newest : sort.Trim();
        if (!ProductSorts.IsDefined(sortValue))
        {
            return ListingsErrors.InvalidSort;
        }

        // NOTE (HU-10): page y cursor son excluyentes; la app usa page y el scroll continuo usa cursor.
        var byPage = page is not null;
        ProductCursor? after = null;
        if (!byPage && !string.IsNullOrWhiteSpace(cursor))
        {
            if (!ProductCursorCodec.TryDecode(cursor, sortValue, MaxPriceCents, out var decoded))
            {
                return ListingsErrors.InvalidCursor;
            }

            after = decoded;
        }

        // HU-09 R1: q vacio equivale a no buscar; con texto, hasta 80 caracteres y al menos una letra o digito.
        var trimmed = q?.Trim();
        string? search = null;
        if (!string.IsNullOrEmpty(trimmed))
        {
            if (trimmed.Length > MaxSearchLength || !trimmed.Any(char.IsLetterOrDigit))
            {
                return ListingsErrors.InvalidQuery;
            }

            search = SearchTerms(trimmed);
        }

        ArgumentNullException.ThrowIfNull(viewer);
        var communityId = viewer.CommunityId ?? await FindCampusAsync(campus, cancellationToken).ConfigureAwait(false);
        if (communityId is null)
        {
            return ListingsErrors.UnknownCampus;
        }

        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        // HU-57: el bloqueo se resuelve una vez para la pagina y el total.
        var hidden = viewer.UserId is null ? null : await blocks.HiddenForAsync(viewer.UserId, cancellationToken).ConfigureAwait(false);
        // HU-10 R1: varias categorias, recortadas y sin vacios; un solo valor sigue funcionando.
        // NOTE (HU-10): sin tope propio de categorias; la linea de peticion lo acota. Si se vuelve abuso, tope + error.
        var slugs = (category ?? [])
            .Select(slug => slug.Trim())
            .Where(slug => slug.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        // HU-10: sin el parametro, la busqueda oculta los agotados (HU-09) y la vitrina los muestra.
        var includeSoldOut = hideSoldOut is null ? search is null : !hideSoldOut.Value;
        var query = new ProductQuery(
            communityId,
            slugs.Length == 0 ? null : slugs,
            includeSoldOut,
            byPage ? null : after,
            size,
            search,
            minPriceCents,
            maxPriceCents,
            conditionValue,
            sortValue,
            hidden,
            page,
            sellerId);
        var items = await products.FindPageAsync(query, cancellationToken).ConfigureAwait(false);
        var total = await products.CountAsync(query, cancellationToken).ConfigureAwait(false);
        var includeSeller = viewer.CommunityId is not null;
        string? nextCursor = null;
        if (!byPage && items.Count == size)
        {
            var last = items[^1];
            nextCursor = ProductCursorCodec.Encode(new ProductCursor(
                sortValue,
                ProductSorts.IsPriceBased(sortValue) ? last.PriceCents : null,
                last.Id));
        }

        return Result<ProductPage>.Ok(new ProductPage(
            items.Select(p => ToResponse(p, includeSeller)).ToList(),
            nextCursor,
            total));
    }

    /// <summary>
    /// Terminos seguros para el patron de prefijo: solo letras, digitos y espacios, y como maximo
    /// <see cref="MaxSearchTerms"/>. Nada de lo escrito se vuelve sintaxis y el costo queda acotado (OWASP API3, API4).
    /// </summary>
    private static string SearchTerms(string query) =>
        string.Join(' ', query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Concat(word.Where(char.IsLetterOrDigit)))
            .Where(word => word.Length > 0)
            .Take(MaxSearchTerms));

    /// <summary>
    /// Detalle de una publicacion (HU-11). Visitantes: solo disponibles o agotadas, sin el vendedor (RNF-05).
    /// Con sesion: tambien su campus; su dueno la ve aunque este pausada.
    /// </summary>
    /// <param name="userId">Usuario del token, o <c>null</c> sin sesion.</param>
    /// <param name="userCommunityId">Comunidad del token, o <c>null</c>.</param>
    /// <param name="productId">Publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El detalle o <c>listings.product_not_found</c>.</returns>
    public async Task<Result<ProductDetailResponse>> DetailAsync(string? userId, string? userCommunityId, string productId, CancellationToken cancellationToken)
    {
        var product = string.IsNullOrEmpty(productId) ? null : await products.FindByIdAsync(productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return ListingsErrors.ProductNotFound;
        }

        var owner = userId is not null && string.Equals(product.SellerId, userId, StringComparison.Ordinal);
        // Pausada y vendida (HU-08) solo para su dueno; para los demas es como si no existiera.
        var visible = product.Status is ProductStatuses.Active or ProductStatuses.SoldOut
            || (owner && product.Status is ProductStatuses.Hidden or ProductStatuses.Sold);
        // Con sesion, otro campus es como si no existiera (RNF-21, OWASP API1).
        var otherCampus = userCommunityId is not null && !string.Equals(product.CommunityId, userCommunityId, StringComparison.Ordinal);
        if (!visible || otherCampus)
        {
            return ListingsErrors.ProductNotFound;
        }

        // HU-57: con un bloqueo entre quien consulta y quien vende, tampoco existe para el.
        if (!owner && userId is not null && await blocks.BetweenAsync(userId, product.SellerId, cancellationToken).ConfigureAwait(false))
        {
            return ListingsErrors.ProductNotFound;
        }

        var category = (await categories.ListActiveAsync(product.CommunityId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(c => string.Equals(c.Slug, product.CategorySlug, StringComparison.Ordinal));
        if (!owner)
        {
            var utcDay = time.GetUtcNow().UtcDateTime.Date;
            await productStats.IncrementViewAsync(product.CommunityId, product.Id, utcDay, cancellationToken).ConfigureAwait(false);
        }
        return Result<ProductDetailResponse>.Ok(new ProductDetailResponse(
            product.Id,
            product.Title,
            product.Description,
            new CategoryResponse(product.CategorySlug, category?.Name ?? product.CategorySlug),
            product.PriceCents,
            product.Condition,
            product.ConditionDetail,
            product.Stock,
            product.Status,
            product.Photos.Select(PhotoUrl).ToList(),
            userCommunityId is null ? null : new ProductSellerResponse(product.SellerId, product.Seller.Name),
            product.CreatedAt));
    }

    /// <summary>Ranking por vistas agregadas, aislado por comunidad (HU-49).</summary>
    public async Task<Result<PopularProductPage>> PopularAsync(ShowcaseViewer viewer, string? campus, string? period, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        if (period is not ("today" or "week"))
        {
            return ListingsErrors.InvalidPopularPeriod;
        }
        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        PopularProductCursor? after = null;
        if (!string.IsNullOrWhiteSpace(cursor) && !TryDecodePopularCursor(cursor, out after))
        {
            return ListingsErrors.InvalidCursor;
        }
        var communityId = viewer.CommunityId ?? await FindCampusAsync(campus, cancellationToken).ConfigureAwait(false);
        if (communityId is null)
        {
            return ListingsErrors.UnknownCampus;
        }
        var today = time.GetUtcNow().UtcDateTime.Date;
        var from = period == "today" ? today : today.AddDays(-6);
        var page = await productStats.PopularAsync(communityId, from, today, after, size, cancellationToken).ConfigureAwait(false);
        var next = page.HasMore && page.Items.Count > 0
            ? EncodePopularCursor(new PopularProductCursor(page.Items[^1].Views, page.Items[^1].Product.CreatedAt, page.Items[^1].Product.Id))
            : null;
        return Result<PopularProductPage>.Ok(new PopularProductPage(
            page.Items.Select(row => new PopularProductResponse(ToResponse(row.Product, viewer.CommunityId is not null), row.Views)).ToList(),
            next,
            page.Total));
    }

    private static string EncodePopularCursor(PopularProductCursor cursor)
    {
        var payload = string.Create(CultureInfo.InvariantCulture, $"{cursor.Views}|{cursor.CreatedAt.Ticks}|{cursor.ProductId}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodePopularCursor(string value, out PopularProductCursor? cursor)
    {
        cursor = null;
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(normalized)).Split('|');
            if (parts.Length != 3
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var views)
                || views < 0
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTime.MinValue.Ticks
                || ticks > DateTime.MaxValue.Ticks
                || !ObjectId.TryParse(parts[2], out _))
            {
                return false;
            }

            cursor = new PopularProductCursor(views, new DateTime(ticks, DateTimeKind.Utc), parts[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>"Mis productos" por pestana, con los conteos de las tres (HU-08, maqueta <c>Mis-productos</c>).</summary>
    /// <param name="userId">Vendedor (del token).</param>
    /// <param name="tab">Ver <see cref="MyProductTabs"/>; sin pestana, todas menos las quitadas (lo de HU-06).</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina (1 a 50; 50 por defecto).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina o el error.</returns>
    public async Task<Result<MyProductPage>> MineAsync(string userId, string? tab, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? statuses = null;
        if (!string.IsNullOrEmpty(tab) && (statuses = MyProductTabs.Statuses(tab)) is null)
        {
            return ListingsErrors.InvalidTab;
        }

        if (cursor is not null && !ObjectId.TryParse(cursor, out _))
        {
            return ListingsErrors.InvalidCursor;
        }

        var size = Math.Clamp(limit ?? MaxPageSize, 1, MaxPageSize);
        // HU-26: las vendidas por un pedido completado ya salen como venta en "Vendidos" (GET /orders/sales).
        var soldByOrder = await orders.SoldProductIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        var onlySold = statuses is [ProductStatuses.Sold];
        // Se pide una de mas para saber si hay otra pagina: si la ultima llega justo llena, no se da un cursor
        // que lleve a una pagina vacia (revision del PR #61).
        var fetched = onlySold
            ? await products.FindBySellerExceptAsync(userId, statuses!, soldByOrder, cursor, size + 1, cancellationToken).ConfigureAwait(false)
            : await products.FindBySellerAsync(userId, statuses, cursor, size + 1, cancellationToken).ConfigureAwait(false);
        var hasMore = fetched.Count > size;
        var page = hasMore ? fetched.Take(size).ToList() : fetched;
        var byStatus = await products.CountBySellerAsync(userId, cancellationToken).ConfigureAwait(false);
        var soldByOrderCount = await products.CountAmongAsync(userId, soldByOrder, ProductStatuses.Sold, cancellationToken).ConfigureAwait(false);
        long Count(string status) =>
            (byStatus.TryGetValue(status, out var n) ? n : 0) - (status == ProductStatuses.Sold ? soldByOrderCount : 0);
        var counts = new MyProductCounts(
            Count(ProductStatuses.Active) + Count(ProductStatuses.Hidden),
            Count(ProductStatuses.Sold),
            Count(ProductStatuses.SoldOut));
        var total = statuses is null ? byStatus.Values.Sum() : statuses.Sum(Count);
        return Result<MyProductPage>.Ok(new MyProductPage(
            page.Select(p => ToResponse(p, includeSeller: true)).ToList(),
            hasMore ? page[^1].Id : null,
            total,
            counts));
    }

    /// <summary>"Productos parecidos se venden entre": percentiles 25 y 75 de la categoria.</summary>
    /// <param name="communityId">Comunidad del token.</param>
    /// <param name="category">Slug de la categoria.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El rango, o <c>null</c> si no hay suficientes publicaciones para que sea honesto.</returns>
    public async Task<PriceRangeResponse?> PriceRangeAsync(string communityId, string category, CancellationToken cancellationToken)
    {
        var prices = (await products.FindActivePricesAsync(communityId, category, 200, cancellationToken).ConfigureAwait(false))
            .Order()
            .ToList();
        if (prices.Count < MinPricesForRange)
        {
            return null;
        }

        return new PriceRangeResponse(Percentile(prices, 0.25), Percentile(prices, 0.75), prices.Count);
    }

    /// <summary>Prefijo de las fotos de un usuario: la llave lleva su id (OWASP API1) y se borran juntas (HU-46).</summary>
    /// <param name="userId">Usuario.</param>
    /// <returns>El prefijo, terminado en <c>/</c>.</returns>
    public static string PhotoPrefix(string userId) => "media/products/" + userId + "/";

    /// <summary>Llave de una foto nueva del usuario.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <returns>La llave.</returns>
    public static string NewPhotoKey(string userId) =>
        string.Create(CultureInfo.InvariantCulture, $"{PhotoPrefix(userId)}{Guid.NewGuid():N}.webp");

    /// <summary>Ruta publica de una llave.</summary>
    /// <param name="key">Llave.</param>
    /// <returns>La ruta (<c>/media/...</c>).</returns>
    public static string PhotoUrl(string key) => "/" + key;

    /// <summary>"Ana Sofía Ramírez" → "Ana R." (lo que la maqueta muestra del vendedor).</summary>
    /// <param name="fullName">Nombre completo.</param>
    /// <returns>Nombre corto.</returns>
    public static string ShortName(string fullName)
    {
        var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => string.Empty,
            1 => parts[0],
            _ => string.Create(CultureInfo.InvariantCulture, $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}."),
        };
    }

    // Interpolacion lineal entre los dos precios vecinos (como PERCENTILE.INC de Excel).
    private static long Percentile(List<long> sorted, double p)
    {
        var position = (sorted.Count - 1) * p;
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Count - 1);
        return (long)Math.Round(sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower)), MidpointRounding.AwayFromZero);
    }

    /// <summary>Forma publica de una publicacion (tambien la usan editar, pausar y reactivar, HU-07).</summary>
    /// <param name="p">La publicacion.</param>
    /// <param name="includeSeller">Mostrar el vendedor (solo con sesion; RNF-05).</param>
    /// <returns>La respuesta.</returns>
    internal static ProductResponse ToResponse(ProductDocument p, bool includeSeller) => new(
        p.Id,
        p.Title,
        p.Description,
        p.CategorySlug,
        p.PriceCents,
        p.Condition,
        p.ConditionDetail,
        p.Stock,
        p.Status,
        p.Photos.Select(PhotoUrl).ToList(),
        includeSeller ? new SellerResponse(p.Seller.Name) : null,
        p.CreatedAt,
        p.SoldAt);

    [GeneratedRegex("^media/products/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PhotoKeyPattern();

    /// <summary>La llave tiene la forma de una foto subida por esa persona (sin revisar que el archivo exista).</summary>
    /// <param name="userId">Quien la usa.</param>
    /// <param name="key">Llave.</param>
    /// <returns><c>true</c> si la forma y el prefijo son suyos.</returns>
    public static bool IsOwnPhotoKey(string userId, string? key) =>
        key is not null && PhotoKeyPattern().IsMatch(key) && key.StartsWith(PhotoPrefix(userId), StringComparison.Ordinal);

    private async Task<bool> ValidPhotosAsync(string userId, IReadOnlyList<string> photos, CancellationToken cancellationToken)
    {
        if (photos is null || photos.Count is < 1 or > MaxPhotos || photos.Distinct(StringComparer.Ordinal).Count() != photos.Count)
        {
            return false;
        }

        // Solo fotos que el mismo usuario subio por POST /products/photos (OWASP API1): la llave lleva su id.
        foreach (var key in photos)
        {
            if (!IsOwnPhotoKey(userId, key) || !await media.ExistsAsync(key, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<string?> FindCampusAsync(string? campus, CancellationToken cancellationToken)
    {
        var slug = string.IsNullOrWhiteSpace(campus) ? catalog.Value.DefaultCampus : campus.Trim().ToLowerInvariant();
        return await members.FindCommunityIdAsync(slug, cancellationToken).ConfigureAwait(false);
    }
}
