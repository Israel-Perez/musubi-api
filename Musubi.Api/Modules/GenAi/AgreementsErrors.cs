using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.GenAi;

/// <summary>Errores del acuerdo con IA (HU-32/33/34; catalogo canonico: docs/errores.md).</summary>
internal static class AgreementsErrors
{
    /// <summary>RNF-15: sin proveedor, sin red, cuota excedida o respuesta con otra forma. El chat sigue igual.</summary>
    public static readonly ApiError Unavailable = new(
        "genai.unavailable", StatusCodes.Status503ServiceUnavailable, "La IA no está disponible ahora. Llena el resumen a mano o inténtalo más tarde.");

    /// <summary>El acuerdo no existe, vencio o no es de quien consulta (OWASP API1: no revela cual).</summary>
    public static readonly ApiError NotFound = new(
        "agreements.not_found", StatusCodes.Status404NotFound, "Este acuerdo ya no existe. Pide otro resumen en el chat.");

    /// <summary>La conversacion no existe o no es de quien consulta (RN-22).</summary>
    public static readonly ApiError ConversationNotFound = new(
        "agreements.conversation_not_found", StatusCodes.Status404NotFound, "No encontramos esta conversación.");

    /// <summary>Falta la conversacion o no es un id.</summary>
    public static readonly ApiError InvalidConversation = new(
        "agreements.invalid_conversation", StatusCodes.Status400BadRequest, "No pudimos leer la conversación. Vuelve a abrir el chat.");

    /// <summary>HU-57: bloqueo en cualquier direccion (no revela quien bloqueo).</summary>
    public static readonly ApiError Blocked = new(
        "agreements.blocked", StatusCodes.Status403Forbidden, "No puedes hacer un acuerdo con esta persona.");

    /// <summary>La conversacion ya tiene un pedido pendiente o confirmado.</summary>
    public static readonly ApiError OrderOpen = new(
        "agreements.order_open", StatusCodes.Status409Conflict, "Esta conversación ya tiene un pedido abierto.");

    /// <summary>La publicacion o la oferta ya no estan disponibles.</summary>
    public static readonly ApiError ProductUnavailable = new(
        "agreements.product_unavailable", StatusCodes.Status409Conflict, "Esta publicación ya no está disponible.");

    /// <summary>Todavia no hay mensajes de texto que leer.</summary>
    public static readonly ApiError NothingToSummarize = new(
        "agreements.nothing_to_summarize", StatusCodes.Status422UnprocessableEntity, "Todavía no hay mensajes que resumir. Platiquen primero el trato.");

    /// <summary>Otra persona cambio el acuerdo despues de que se abrio (RN-41: se confirma lo que se vio).</summary>
    public static readonly ApiError Changed = new(
        "agreements.changed", StatusCodes.Status409Conflict, "El acuerdo cambió. Revísalo de nuevo antes de confirmarlo.");

    /// <summary>Falta la version del acuerdo.</summary>
    public static readonly ApiError InvalidRevision = new(
        "agreements.invalid_revision", StatusCodes.Status400BadRequest, "No pudimos leer el acuerdo. Vuelve a abrirlo.");

    /// <summary>Faltan datos para confirmar (RN-42: lo "No identificado" se completa antes).</summary>
    public static readonly ApiError Incomplete = new(
        "agreements.incomplete", StatusCodes.Status422UnprocessableEntity, "Completa los datos que faltan antes de confirmar.");

    /// <summary>Producto vacio o de mas de 80 caracteres.</summary>
    public static readonly ApiError InvalidTitle = new(
        "agreements.invalid_title", StatusCodes.Status400BadRequest, "Escribe el producto en hasta 80 caracteres.");

    /// <summary>Precio fuera de $1 a $999,999.</summary>
    public static readonly ApiError InvalidPrice = new(
        "agreements.invalid_price", StatusCodes.Status400BadRequest, "Escribe un precio de $1 a $999,999.");

    /// <summary>Cantidad fuera de 1 a lo disponible.</summary>
    public static readonly ApiError InvalidQuantity = new(
        "agreements.invalid_quantity", StatusCodes.Status400BadRequest, "Elige una cantidad de 1 a lo disponible.");

    /// <summary>Punto que no es de los activos del campus.</summary>
    public static readonly ApiError InvalidMeetingPoint = new(
        "agreements.invalid_meeting_point", StatusCodes.Status400BadRequest, "Elige un punto de entrega del campus.");

    /// <summary>Detalle del lugar de mas de 120 caracteres.</summary>
    public static readonly ApiError InvalidDetail = new(
        "agreements.invalid_detail", StatusCodes.Status400BadRequest, "El detalle del lugar puede tener hasta 120 caracteres.");

    /// <summary>Fecha y hora fuera de la ventana del pedido.</summary>
    public static readonly ApiError InvalidDeliveryTime = new(
        "agreements.invalid_delivery_time", StatusCodes.Status400BadRequest, "Elige una fecha de hoy a 14 días, entre las 7:00 y las 21:00.");

    /// <summary>Condiciones de mas de 200 caracteres.</summary>
    public static readonly ApiError InvalidConditions = new(
        "agreements.invalid_conditions", StatusCodes.Status400BadRequest, "Las condiciones pueden tener hasta 200 caracteres.");
}
