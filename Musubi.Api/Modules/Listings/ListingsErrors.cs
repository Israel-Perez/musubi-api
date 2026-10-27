using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Listings;

/// <summary>Errores de negocio de M-03 (catalogo canonico: docs/errores.md). Textos tomados de la maqueta (<c>Vender-1…4</c>).</summary>
internal static class ListingsErrors
{
    /// <summary>Sin fotos, mas de 8, repetidas o que no son de quien publica (RN-03).</summary>
    public static readonly ApiError InvalidPhotos = new(
        "listings.invalid_photos", StatusCodes.Status400BadRequest, "Agrega de 1 a 8 fotos tuyas para continuar.");

    /// <summary>Nombre vacio o fuera de 3 a 80 caracteres.</summary>
    public static readonly ApiError InvalidTitle = new(
        "listings.invalid_title", StatusCodes.Status400BadRequest, "Escribe el nombre del producto.");

    /// <summary>Descripcion de mas de 500 caracteres.</summary>
    public static readonly ApiError InvalidDescription = new(
        "listings.invalid_description", StatusCodes.Status400BadRequest, "La descripción puede tener hasta 500 caracteres.");

    /// <summary>Precio de cero, negativo o mayor a $999,999 (RN-04).</summary>
    public static readonly ApiError InvalidPrice = new(
        "listings.invalid_price", StatusCodes.Status400BadRequest, "Ponle un precio a tu producto.");

    /// <summary>Categoria que no existe en la comunidad.</summary>
    public static readonly ApiError InvalidCategory = new(
        "listings.invalid_category", StatusCodes.Status400BadRequest, "Elige una categoría.");

    /// <summary>Estado que no es nuevo ni usado, o detalle que no corresponde.</summary>
    public static readonly ApiError InvalidCondition = new(
        "listings.invalid_condition", StatusCodes.Status400BadRequest, "Elige el estado del producto.");

    /// <summary>Piezas fuera de 1 a 10 (RN-04, HU-50).</summary>
    public static readonly ApiError InvalidStock = new(
        "listings.invalid_stock", StatusCodes.Status400BadRequest, "Las piezas disponibles van de 1 a 10.");

    /// <summary>Cursor de paginacion que no se emitio aqui.</summary>
    public static readonly ApiError InvalidCursor = new(
        "listings.invalid_cursor", StatusCodes.Status400BadRequest, "No pudimos cargar más publicaciones. Actualiza la lista.");

    /// <summary>Campus desconocido en la vitrina publica.</summary>
    public static readonly ApiError UnknownCampus = new(
        "listings.unknown_campus", StatusCodes.Status404NotFound, "No encontramos ese campus.");

    /// <summary>Archivo que no es una imagen que se pueda leer.</summary>
    public static readonly ApiError PhotoUnsupported = new(
        "listings.photo_unsupported", StatusCodes.Status415UnsupportedMediaType, "No pudimos leer esa foto. Prueba con otra en JPG, PNG o WebP.");

    /// <summary>Foto de menos de 200 px por lado.</summary>
    public static readonly ApiError PhotoTooSmall = new(
        "listings.photo_too_small", StatusCodes.Status422UnprocessableEntity, "La foto es muy pequeña. Usa una de al menos 200 px.");

    /// <summary>Foto de mas de 50 megapixeles.</summary>
    public static readonly ApiError PhotoTooLarge = new(
        "listings.photo_too_large", StatusCodes.Status413PayloadTooLarge, "La foto es demasiado grande. Prueba con otra.");
}
