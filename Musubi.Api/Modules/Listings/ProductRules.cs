using System;
using System.Collections.Generic;
using System.Linq;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Listings;

/// <summary>Lo que se valida igual al publicar (HU-06) y al editar (HU-07): ya limpio y listo para guardar.</summary>
/// <param name="Title">Nombre sin espacios en los extremos.</param>
/// <param name="Description">Descripcion sin espacios en los extremos (vacia si no hay).</param>
/// <param name="CategorySlug">Categoria activa de la comunidad.</param>
/// <param name="PriceCents">Precio en centavos.</param>
/// <param name="Condition"><c>new</c> o <c>used</c>.</param>
/// <param name="ConditionDetail">Detalle de un usado; <c>null</c> en uno nuevo.</param>
internal sealed record ProductDetails(
    string Title,
    string Description,
    string CategorySlug,
    long PriceCents,
    string Condition,
    string? ConditionDetail);

/// <summary>Reglas RN-03 y RN-04 del nombre, la descripcion, el precio, el estado y la categoria.</summary>
internal static class ProductRules
{
    /// <summary>Precio maximo: $999,999 (6 digitos en la maqueta).</summary>
    public const long MaxPriceCents = 99_999_900;

    /// <summary>Valida y limpia los datos de una publicacion.</summary>
    /// <param name="title">Nombre (3 a 80).</param>
    /// <param name="description">Descripcion (hasta 500; opcional).</param>
    /// <param name="priceCents">Precio en centavos (mayor que cero).</param>
    /// <param name="condition"><c>new</c> o <c>used</c>.</param>
    /// <param name="conditionDetail">Solo usado: <c>likeNew</c>, <c>good</c> o <c>fair</c>.</param>
    /// <param name="category">Slug de la categoria.</param>
    /// <param name="activeCategories">Categorias activas de la comunidad del vendedor.</param>
    /// <returns>Los datos limpios o el primer error.</returns>
    public static Result<ProductDetails> Check(
        string? title,
        string? description,
        long? priceCents,
        string? condition,
        string? conditionDetail,
        string? category,
        IReadOnlyList<CategoryDocument> activeCategories)
    {
        ArgumentNullException.ThrowIfNull(activeCategories);
        var cleanTitle = (title ?? string.Empty).Trim();
        if (cleanTitle.Length is < 3 or > 80)
        {
            return ListingsErrors.InvalidTitle;
        }

        var cleanDescription = (description ?? string.Empty).Trim();
        if (cleanDescription.Length > 500)
        {
            return ListingsErrors.InvalidDescription;
        }

        if (priceCents is not { } price || price <= 0 || price > MaxPriceCents)
        {
            return ListingsErrors.InvalidPrice;
        }

        if (!ValidCondition(condition, conditionDetail))
        {
            return ListingsErrors.InvalidCondition;
        }

        var match = activeCategories.FirstOrDefault(c => string.Equals(c.Slug, category, StringComparison.Ordinal));
        if (match is null)
        {
            return ListingsErrors.InvalidCategory;
        }

        return Result<ProductDetails>.Ok(new ProductDetails(
            cleanTitle,
            cleanDescription,
            match.Slug,
            price,
            condition!,
            condition == ProductConditions.Used ? conditionDetail : null));
    }

    private static bool ValidCondition(string? condition, string? detail) => condition switch
    {
        ProductConditions.New => string.IsNullOrEmpty(detail),
        ProductConditions.Used => detail is not null && ProductConditions.UsedDetails.Contains(detail),
        _ => false,
    };
}
