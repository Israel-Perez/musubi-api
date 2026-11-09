using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>Errores de las ofertas (HU-16; catalogo canonico: docs/errores.md).</summary>
internal static class OfferErrors
{
    /// <summary>RN-06: no se responde a una solicitud propia.</summary>
    public static readonly ApiError OwnRequest = new(
        "offers.own_request", StatusCodes.Status409Conflict, "No puedes ofrecer en tu propia solicitud.");

    /// <summary>La publicacion no es tuya, no esta disponible o es de otro campus.</summary>
    public static readonly ApiError ProductUnavailable = new(
        "offers.product_unavailable", StatusCodes.Status400BadRequest, "Elige una de tus publicaciones disponibles.");

    /// <summary>Descripcion de la oferta directa vacia, de menos de 3 o de mas de 300 caracteres.</summary>
    public static readonly ApiError InvalidDescription = new(
        "offers.invalid_description", StatusCodes.Status400BadRequest, "Describe lo que ofreces, de 3 a 300 caracteres.");

    /// <summary>Precio fuera de $1 a $999,999.</summary>
    public static readonly ApiError InvalidPrice = new(
        "offers.invalid_price", StatusCodes.Status400BadRequest, "Escribe tu precio, de $1 a $999,999.");

    /// <summary>Estado que no es <c>new</c> ni <c>used</c>.</summary>
    public static readonly ApiError InvalidCondition = new(
        "offers.invalid_condition", StatusCodes.Status400BadRequest, "Elige si es nuevo o usado.");

    /// <summary>Foto que no subio esa persona o que no existe (OWASP API1).</summary>
    public static readonly ApiError InvalidPhoto = new(
        "offers.invalid_photo", StatusCodes.Status400BadRequest, "Vuelve a agregar la foto.");

    /// <summary>Mensaje de mas de 300 caracteres.</summary>
    public static readonly ApiError InvalidMessage = new(
        "offers.invalid_message", StatusCodes.Status400BadRequest, "El mensaje puede tener hasta 300 caracteres.");

    /// <summary>Aun no hay oferta tuya en esa solicitud.</summary>
    public static readonly ApiError NotFound = new(
        "offers.not_found", StatusCodes.Status404NotFound, "Todavía no has ofrecido nada en esta solicitud.");
}
