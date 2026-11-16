using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Listings;

/// <summary>Quien vende una opcion del comparador (HU-51).</summary>
/// <param name="Id">Su id (abre su perfil, HU-05).</param>
/// <param name="Name">Nombre corto ("Valeria C.").</param>
/// <param name="RatingAvg">Promedio de calificaciones, o <c>null</c> sin calificaciones (RN-26).</param>
/// <param name="RatingCount">Opiniones.</param>
/// <param name="Sales">Ventas, o <c>null</c> si las oculta (HU-04).</param>
public sealed record ComparisonSellerResponse(string Id, string Name, double? RatingAvg, int RatingCount, int? Sales);

/// <summary>Una opcion de "Compara antes de comprar".</summary>
/// <param name="Product">La publicacion.</param>
/// <param name="Seller">Quien la vende.</param>
/// <param name="SameModel">Comparte el codigo del modelo con la que se ve ("Mismo modelo").</param>
public sealed record ComparisonItemResponse(ProductResponse Product, ComparisonSellerResponse Seller, bool SameModel);

/// <summary>"Compara antes de comprar" (maqueta <c>Producto</c>, HU-51).</summary>
/// <param name="Items">Hasta 5: el mismo modelo primero y luego el precio mas cercano.</param>
public sealed record ProductComparisonResponse(IReadOnlyList<ComparisonItemResponse> Items);

/// <summary>
/// "Mismo modelo" (HU-51): dos nombres comparten un codigo como "fx-991ES", "TI-84" o "A515". Un codigo es una palabra
/// del nombre, sin acentos ni guiones, de al menos 4 caracteres, con letras y digitos, y que no es una medida ("16GB").
/// </summary>
internal static partial class ModelCodes
{
    private static readonly char[] Separators = [' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '(', ')', '/', '"', '\'', '«', '»'];

    /// <summary>Los codigos de modelo de un nombre.</summary>
    /// <param name="title">El nombre de la publicacion.</param>
    /// <returns>Los codigos, normalizados ("fx991es").</returns>
    public static IReadOnlySet<string> Of(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var plain = string.Concat(title.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
            .ToLowerInvariant();
        return plain.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Concat(word.Where(char.IsLetterOrDigit)))
            .Where(word => word.Length >= 4 && word.Any(char.IsDigit) && word.Any(char.IsLetter) && !Measure().IsMatch(word))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Si dos nombres son del mismo modelo.</summary>
    /// <param name="codes">Los codigos de uno (<see cref="Of"/>).</param>
    /// <param name="title">El nombre del otro.</param>
    /// <returns><c>true</c> si comparten un codigo.</returns>
    public static bool Shares(IReadOnlySet<string> codes, string title) => codes.Count > 0 && codes.Overlaps(Of(title));

    [GeneratedRegex("^[0-9]+(gb|tb|mb|mm|cm|ml|kg|mah|hz|ghz|mp|px|pulg|in|w|v)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Measure();
}

/// <summary>
/// "Compara antes de comprar" (maqueta <c>Producto</c>, HU-51; decision de la usuaria del 16-Nov-2026: la regla de la
/// maqueta). De la misma categoria y campus, disponibles, sin la misma, sin las mias y sin bloqueos (HU-57): primero el
/// mismo modelo, luego el precio mas cercano y luego la mas nueva. El puntaje "Para ti" lo calcula la app.
/// </summary>
internal sealed class ProductComparisonService(
    ListingService listings,
    IProductRepository products,
    IBlockDirectory blocks,
    IProfileDirectory profiles)
{
    /// <summary>Opciones como maximo (la maqueta muestra 5).</summary>
    public const int MaxOptions = 5;

    /// <summary>Candidatas que se leen (las mas nuevas de la categoria; OWASP API4).</summary>
    public const int MaxCandidates = 50;

    /// <summary>Las opciones para comparar con una publicacion.</summary>
    /// <param name="userId">Quien compara (del token).</param>
    /// <param name="communityId">Su campus (del token).</param>
    /// <param name="productId">La publicacion que esta viendo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las opciones (puede no haber), o <c>listings.product_not_found</c>.</returns>
    public async Task<Result<ProductComparisonResponse>> CompareAsync(string userId, string? communityId, string productId, CancellationToken cancellationToken)
    {
        if (await listings.FindVisibleAsync(userId, communityId, productId, cancellationToken).ConfigureAwait(false) is not { } product)
        {
            return ListingsErrors.ProductNotFound;
        }

        List<string> excluded = [userId, .. await blocks.HiddenForAsync(userId, cancellationToken).ConfigureAwait(false)];
        var candidates = await products.FindPageAsync(
            new ProductQuery(product.CommunityId, [product.CategorySlug], IncludeSoldOut: false, After: null, MaxCandidates, ExcludeSellers: excluded),
            cancellationToken).ConfigureAwait(false);

        var codes = ModelCodes.Of(product.Title);
        var chosen = candidates
            .Where(c => c.Id != product.Id && c.Status == ProductStatuses.Active && c.Stock > 0)
            .Select(c => (Product: c, SameModel: ModelCodes.Shares(codes, c.Title)))
            .OrderByDescending(c => c.SameModel)
            .ThenBy(c => Math.Abs(c.Product.PriceCents - product.PriceCents))
            .ThenByDescending(c => c.Product.Id, StringComparer.Ordinal)
            .Take(MaxOptions)
            .ToList();
        if (chosen.Count == 0)
        {
            return Result<ProductComparisonResponse>.Ok(new ProductComparisonResponse([]));
        }

        var cards = await profiles.FindCardsAsync(
            product.CommunityId,
            [.. chosen.Select(c => c.Product.SellerId).Distinct(StringComparer.Ordinal)],
            cancellationToken).ConfigureAwait(false);
        return Result<ProductComparisonResponse>.Ok(new ProductComparisonResponse(
        [
            .. chosen.Select(c =>
            {
                var card = cards.GetValueOrDefault(c.Product.SellerId);
                return new ComparisonItemResponse(
                    ListingService.ToResponse(c.Product, includeSeller: true),
                    new ComparisonSellerResponse(c.Product.SellerId, c.Product.Seller.Name, card?.RatingAvg, card?.RatingCount ?? 0, card is null ? 0 : card.Sales),
                    c.SameModel);
            }),
        ]));
    }
}
