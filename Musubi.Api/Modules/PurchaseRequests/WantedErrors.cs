using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>Errores de negocio de M-05 (catalogo canonico: docs/errores.md).</summary>
internal static class WantedErrors
{
    /// <summary>"Producto que buscas" vacio, de menos de 3 o de mas de 80 caracteres.</summary>
    public static readonly ApiError InvalidTitle = new(
        "wanted.invalid_title", StatusCodes.Status400BadRequest, "Escribe qué buscas, de 3 a 80 caracteres.");

    /// <summary>Categoria que no es del campus ni <c>otro</c>.</summary>
    public static readonly ApiError InvalidCategory = new(
        "wanted.invalid_category", StatusCodes.Status400BadRequest, "Elige una categoría.");

    /// <summary>Descripcion de mas de 300 caracteres.</summary>
    public static readonly ApiError InvalidDescription = new(
        "wanted.invalid_description", StatusCodes.Status400BadRequest, "La descripción puede tener hasta 300 caracteres.");

    /// <summary>Mas de 5 caracteristicas, o alguna vacia o de mas de 40 caracteres.</summary>
    public static readonly ApiError InvalidFeatures = new(
        "wanted.invalid_features", StatusCodes.Status400BadRequest, "Agrega hasta 5 características de hasta 40 caracteres.");

    /// <summary>Presupuesto fuera de $1 a $999,999.</summary>
    public static readonly ApiError InvalidBudget = new(
        "wanted.invalid_budget", StatusCodes.Status400BadRequest, "Indica tu presupuesto máximo, de $1 a $999,999.");

    /// <summary>Cantidad fuera de 1 a 10.</summary>
    public static readonly ApiError InvalidQuantity = new(
        "wanted.invalid_quantity", StatusCodes.Status400BadRequest, "La cantidad va de 1 a 10 piezas.");

    /// <summary>Fecha pasada o a mas de 60 dias.</summary>
    public static readonly ApiError InvalidDate = new(
        "wanted.invalid_date", StatusCodes.Status400BadRequest, "Elige una fecha de hoy a 60 días.");

    /// <summary>Ya tiene el maximo de solicitudes activas (RN-12).</summary>
    public static readonly ApiError TooManyActive = new(
        "wanted.too_many_active", StatusCodes.Status409Conflict, "Ya tienes 5 solicitudes activas. Espera a que alguna termine para publicar otra.");

    /// <summary>Rango de presupuesto del tablero fuera de 0 a $999,999 o con el minimo arriba del maximo (HU-15).</summary>
    public static readonly ApiError InvalidBudgetRange = new(
        "wanted.invalid_budget_range", StatusCodes.Status400BadRequest, "Revisa el rango de presupuesto: debe ir de 0 a $999,999 y el mínimo no puede superar al máximo.");

    /// <summary>"Fecha limite" del tablero fuera de 1 a 60 dias (HU-15).</summary>
    public static readonly ApiError InvalidDeadline = new(
        "wanted.invalid_deadline", StatusCodes.Status400BadRequest, "Elige una fecha límite de 1 a 60 días.");

    /// <summary>Busqueda del tablero de mas de 80 caracteres o sin letras ni digitos (HU-15).</summary>
    public static readonly ApiError InvalidQuery = new(
        "wanted.invalid_query", StatusCodes.Status400BadRequest, "Escribe lo que quieres buscar.");

    /// <summary>Cursor del tablero que no es el de una pagina anterior (HU-15).</summary>
    public static readonly ApiError InvalidCursor = new(
        "wanted.invalid_cursor", StatusCodes.Status400BadRequest, "No pudimos cargar más solicitudes. Actualiza la lista.");

    /// <summary>Editar, cerrar o cancelar una solicitud que ya no esta activa: cerrada, cancelada o vencida (HU-14, RN-11).</summary>
    public static readonly ApiError NotActive = new(
        "wanted.not_active", StatusCodes.Status409Conflict, "Esta solicitud ya no está activa, así que no se puede cambiar.");

    /// <summary>Cambiar el producto o la categoria de una solicitud que ya recibio ofertas (HU-14).</summary>
    public static readonly ApiError HasOffers = new(
        "wanted.has_offers", StatusCodes.Status409Conflict, "Ya recibiste ofertas: el producto y la categoría ya no se pueden cambiar.");

    /// <summary>
    /// La solicitud no existe, es de otro campus, ya no esta activa (para quien no la publico) o hay un bloqueo
    /// entre las dos personas: la misma respuesta en todos los casos (OWASP API1).
    /// </summary>
    public static readonly ApiError NotFound = new(
        "wanted.not_found", StatusCodes.Status404NotFound, "No encontramos esta solicitud.");
}
