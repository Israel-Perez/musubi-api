using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Orders;

/// <summary>Errores de los pedidos (HU-23; catalogo canonico: docs/errores.md).</summary>
internal static class OrdersErrors
{
    /// <summary>El pedido no existe o no es de quien consulta (RNF-03, OWASP API1: no revela cual).</summary>
    public static readonly ApiError NotFound = new(
        "orders.not_found", StatusCodes.Status404NotFound, "No encontramos este pedido.");

    /// <summary>La conversacion no existe o no es de quien consulta (RN-22).</summary>
    public static readonly ApiError ConversationNotFound = new(
        "orders.conversation_not_found", StatusCodes.Status404NotFound, "No encontramos esta conversación.");

    /// <summary>Quien vende en esa conversacion no crea el pedido: lo acepta (HU-24).</summary>
    public static readonly ApiError OnlyBuyer = new(
        "orders.only_buyer", StatusCodes.Status403Forbidden, "El pedido lo crea quien compra. Tú lo aceptarás cuando llegue.");

    /// <summary>RN-06: no se compra lo propio.</summary>
    public static readonly ApiError OwnProduct = new(
        "orders.own_product", StatusCodes.Status409Conflict, "No puedes comprar tu propia publicación.");

    /// <summary>La publicacion esta pausada, agotada, vendida o quitada, o es de otro campus.</summary>
    public static readonly ApiError ProductUnavailable = new(
        "orders.product_unavailable", StatusCodes.Status409Conflict, "Esta publicación ya no está disponible.");

    /// <summary>La oferta todavia no se acepta (HU-17).</summary>
    public static readonly ApiError OfferNotAccepted = new(
        "orders.offer_not_accepted", StatusCodes.Status409Conflict, "Primero acepta la oferta para crear el pedido.");

    /// <summary>HU-57: bloqueo en cualquier direccion (no revela quien bloqueo).</summary>
    public static readonly ApiError Blocked = new(
        "orders.blocked", StatusCodes.Status403Forbidden, "No puedes hacer un pedido con esta persona.");

    /// <summary>Cantidad fuera de 1 a 10.</summary>
    public static readonly ApiError InvalidQuantity = new(
        "orders.invalid_quantity", StatusCodes.Status400BadRequest, "Elige una cantidad de 1 a 10 piezas.");

    /// <summary>RN-17: mas piezas de las disponibles (o de las que pidio la solicitud).</summary>
    public static readonly ApiError NotEnoughStock = new(
        "orders.not_enough_stock", StatusCodes.Status409Conflict, "No hay tantas piezas disponibles. Elige una cantidad menor.");

    /// <summary>Punto que no es de los activos del campus.</summary>
    public static readonly ApiError InvalidMeetingPoint = new(
        "orders.invalid_meeting_point", StatusCodes.Status400BadRequest, "Elige un punto de entrega del campus.");

    /// <summary>Detalle del lugar de mas de 120 caracteres.</summary>
    public static readonly ApiError InvalidDetail = new(
        "orders.invalid_detail", StatusCodes.Status400BadRequest, "El detalle del lugar puede tener hasta 120 caracteres.");

    /// <summary>Fecha y hora antes de 30 minutos, despues de 14 dias o fuera de 7:00 a 21:00 (UTC-6).</summary>
    public static readonly ApiError InvalidDeliveryTime = new(
        "orders.invalid_delivery_time", StatusCodes.Status400BadRequest, "Elige una fecha de hoy a 14 días, entre las 7:00 y las 21:00.");

    /// <summary>La conversacion ya tiene un pedido pendiente o confirmado.</summary>
    public static readonly ApiError AlreadyOpen = new(
        "orders.already_open", StatusCodes.Status409Conflict, "Ya hay un pedido abierto en esta conversación.");

    /// <summary>El pedido ya no esta pendiente: el pago ya no se cambia.</summary>
    public static readonly ApiError NotPending = new(
        "orders.not_pending", StatusCodes.Status409Conflict, "Este pedido ya no está pendiente.");

    /// <summary>Forma de pago que no es <c>cash</c> ni <c>transfer</c>.</summary>
    public static readonly ApiError InvalidPayment = new(
        "orders.invalid_payment", StatusCodes.Status400BadRequest, "Elige cómo quieres pagar.");

    /// <summary>D-01: la transferencia llega con HU-55 (nadie tiene todavia una cuenta registrada).</summary>
    public static readonly ApiError PaymentUnavailable = new(
        "orders.payment_unavailable", StatusCodes.Status409Conflict, "Quien vende aún no registra su cuenta. Por ahora paga en efectivo al recibir.");

    /// <summary>Falta la conversacion en la consulta.</summary>
    public static readonly ApiError InvalidConversation = new(
        "orders.invalid_conversation", StatusCodes.Status400BadRequest, "No pudimos cargar el pedido. Vuelve a abrir el chat.");
}
