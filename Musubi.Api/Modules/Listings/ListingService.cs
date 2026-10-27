using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Media;

namespace Musubi.Api.Modules.Listings;

/// <summary>Configuracion de la vitrina (seccion <c>Catalog</c>).</summary>
internal sealed class CatalogOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Catalog";

    /// <summary>Campus que ve un visitante si no pide otro (HU-48; el piloto es la UTTT).</summary>
    public string DefaultCampus { get; set; } = "uttt";
}

/// <summary>
/// Publicar productos (HU-06, HU-50) y leer la vitrina (HU-48 fase 2). Reglas: RN-03 (nombre, precio,
/// categoria, estado, cantidad y al menos una foto), RN-04 (precio mayor que cero y al menos una pieza).
/// </summary>
internal sealed partial class ListingService(
    IProductRepository products,
    ICategoryRepository categories,
    IMemberDirectory members,
    IMediaStorage media,
    IOptions<CatalogOptions> catalog,
    TimeProvider time)
{
    /// <summary>Fotos por publicacion (maqueta: "Puedes subir hasta 8").</summary>
    public const int MaxPhotos = 8;

    /// <summary>Piezas maximas al publicar (maqueta <c>Vender-4</c>).</summary>
    public const int MaxStock = 10;

    /// <summary>Precio maximo: $999,999 (6 digitos en la maqueta).</summary>
    public const long MaxPriceCents = 99_999_900;

    /// <summary>Tamano de pagina por defecto y maximo.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tamano de pagina maximo.</summary>
    public const int MaxPageSize = 50;

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

        var title = request.Title.Trim();
        if (title.Length is < 3 or > 80)
        {
            return ListingsErrors.InvalidTitle;
        }

        var description = (request.Description ?? string.Empty).Trim();
        if (description.Length > 500)
        {
            return ListingsErrors.InvalidDescription;
        }

        if (request.PriceCents is not { } price || price <= 0 || price > MaxPriceCents)
        {
            return ListingsErrors.InvalidPrice;
        }

        if (request.Stock is not { } stock || stock is < 1 or > MaxStock)
        {
            return ListingsErrors.InvalidStock;
        }

        if (!ValidCondition(request.Condition, request.ConditionDetail))
        {
            return ListingsErrors.InvalidCondition;
        }

        var category = (await categories.ListActiveAsync(member.CommunityId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(c => string.Equals(c.Slug, request.Category, StringComparison.Ordinal));
        if (category is null)
        {
            return ListingsErrors.InvalidCategory;
        }

        if (!await ValidPhotosAsync(userId, request.Photos, cancellationToken).ConfigureAwait(false))
        {
            return ListingsErrors.InvalidPhotos;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var product = new ProductDocument
        {
            Id = MongoConventions.NewId(),
            CommunityId = member.CommunityId,
            SellerId = member.Id,
            Seller = new ProductSeller { Name = ShortName(member.FullName) },
            Title = title,
            Description = description,
            CategorySlug = category.Slug,
            PriceCents = price,
            Condition = request.Condition,
            ConditionDetail = request.Condition == ProductConditions.Used ? request.ConditionDetail : null,
            Stock = stock,
            Status = ProductStatuses.Active,
            Photos = request.Photos.ToList(),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await products.InsertAsync(product, cancellationToken).ConfigureAwait(false);
        // NOTE (HU-18): avisar en segundo plano a las solicitudes "Se busca" que coincidan (RF-23, RNF-11).
        return Result<ProductResponse>.Ok(ToResponse(product, includeSeller: true));
    }

    /// <summary>Vitrina: lo recien publicado del campus, con filtros de la hoja "Filtrar".</summary>
    /// <param name="userCommunityId">Comunidad del token, si hay sesion (entonces se ve el vendedor).</param>
    /// <param name="campus">Campus pedido por un visitante.</param>
    /// <param name="category">Slug de la categoria, o todas.</param>
    /// <param name="hideSoldOut">Ocultar agotados.</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina o el error.</returns>
    public async Task<Result<ProductPage>> ShowcaseAsync(
        string? userCommunityId,
        string? campus,
        string? category,
        bool hideSoldOut,
        string? cursor,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (cursor is not null && !ObjectId.TryParse(cursor, out _))
        {
            return ListingsErrors.InvalidCursor;
        }

        var communityId = userCommunityId ?? await FindCampusAsync(campus, cancellationToken).ConfigureAwait(false);
        if (communityId is null)
        {
            return ListingsErrors.UnknownCampus;
        }

        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var query = new ProductQuery(communityId, string.IsNullOrWhiteSpace(category) ? null : category, !hideSoldOut, cursor, size);
        var page = await products.FindPageAsync(query, cancellationToken).ConfigureAwait(false);
        var total = await products.CountAsync(query, cancellationToken).ConfigureAwait(false);
        var includeSeller = userCommunityId is not null;
        return Result<ProductPage>.Ok(new ProductPage(
            page.Select(p => ToResponse(p, includeSeller)).ToList(),
            page.Count == size ? page[^1].Id : null,
            total));
    }

    /// <summary>Publicaciones del usuario ("Ver mis productos").</summary>
    /// <param name="userId">Vendedor (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Sus publicaciones.</returns>
    public async Task<ProductPage> MineAsync(string userId, CancellationToken cancellationToken)
    {
        var mine = await products.FindBySellerAsync(userId, MaxPageSize, cancellationToken).ConfigureAwait(false);
        // NOTE (HU-08): paginar "Mis productos" cuando alguien pase de 50 publicaciones.
        return new ProductPage(mine.Select(p => ToResponse(p, includeSeller: true)).ToList(), null, mine.Count);
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

    /// <summary>Llave de una foto nueva del usuario.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <returns>La llave.</returns>
    public static string NewPhotoKey(string userId) =>
        string.Create(CultureInfo.InvariantCulture, $"media/products/{userId}/{Guid.NewGuid():N}.webp");

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

    private static bool ValidCondition(string condition, string? detail) => condition switch
    {
        ProductConditions.New => string.IsNullOrEmpty(detail),
        ProductConditions.Used => detail is not null && ProductConditions.UsedDetails.Contains(detail),
        _ => false,
    };

    // Interpolacion lineal entre los dos precios vecinos (como PERCENTILE.INC de Excel).
    private static long Percentile(List<long> sorted, double p)
    {
        var position = (sorted.Count - 1) * p;
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Count - 1);
        return (long)Math.Round(sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower)), MidpointRounding.AwayFromZero);
    }

    private static ProductResponse ToResponse(ProductDocument p, bool includeSeller) => new(
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
        p.CreatedAt);

    [GeneratedRegex("^media/products/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PhotoKeyPattern();

    private async Task<bool> ValidPhotosAsync(string userId, IReadOnlyList<string> photos, CancellationToken cancellationToken)
    {
        if (photos is null || photos.Count is < 1 or > MaxPhotos || photos.Distinct(StringComparer.Ordinal).Count() != photos.Count)
        {
            return false;
        }

        // Solo fotos que el mismo usuario subio por POST /products/photos (OWASP API1): la llave lleva su id.
        var prefix = "media/products/" + userId + "/";
        foreach (var key in photos)
        {
            if (key is null || !PhotoKeyPattern().IsMatch(key) || !key.StartsWith(prefix, StringComparison.Ordinal)
                || !await media.ExistsAsync(key, cancellationToken).ConfigureAwait(false))
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
