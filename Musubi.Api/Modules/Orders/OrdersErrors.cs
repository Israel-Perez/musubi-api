using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Orders;

/// <summary>Errores de los pedidos (HU-23 y HU-24; catalogo canonico: docs/errores.md).</summary>
internal static class OrdersErrors
{
    /// <summary>El pedido no existe o no es de quien consulta (RNF-03, OWASP API1: no revela cual).</summary>
    public static readonly ApiError NotFound = new(
        "orders.not_found", StatusCodes.Status404NotFound, "No encontramos este pedido.");

    /// <summary>"Mis pedidos" con una pestana que no existe (HU-25).</summary>
    public static readonly ApiError InvalidTab = new(
        "orders.invalid_tab", StatusCodes.Status400BadRequest, "No pudimos cargar esa lista. Actualiza la pantalla.");

    /// <summary>"Mis pedidos" con un cursor que no es de esta lista (HU-25).</summary>
    public static readonly ApiError InvalidCursor = new(
        "orders.invalid_cursor", StatusCodes.Status400BadRequest, "No pudimos cargar más pedidos. Actualiza la lista.");

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

    /// <summary>HU-34: el acuerdo no existe, vencio, ya es pedido o no es de quien consulta.</summary>
    public static readonly ApiError AgreementNotFound = new(
        "orders.agreement_not_found", StatusCodes.Status404NotFound, "Este acuerdo ya no existe. Pide otro resumen en el chat.");

    /// <summary>HU-34 (RN-41): falta que alguno de los dos confirme.</summary>
    public static readonly ApiError AgreementNotConfirmed = new(
        "orders.agreement_not_confirmed", StatusCodes.Status409Conflict, "Los dos tienen que confirmar el acuerdo antes de crear el pedido.");

    /// <summary>HU-24: el pedido lo acepta quien vende.</summary>
    public static readonly ApiError OnlySeller = new(
        "orders.only_seller", StatusCodes.Status403Forbidden, "Solo quien vende puede aceptar el pedido.");

    /// <summary>HU-24: quien compra todavia no elige como pagar (decision de la usuaria, 11-Nov-2026).</summary>
    public static readonly ApiError PaymentMissing = new(
        "orders.payment_missing", StatusCodes.Status409Conflict, "Espera a que elijan cómo pagar para aceptar el pedido.");

    /// <summary>HU-24 (RN-20): el motivo no es de la lista o no corresponde a quien cancela.</summary>
    public static readonly ApiError InvalidReason = new(
        "orders.invalid_reason", StatusCodes.Status400BadRequest, "Elige el motivo de la cancelación.");

    /// <summary>HU-24: el pedido ya se completo o se cancelo.</summary>
    public static readonly ApiError Closed = new(
        "orders.closed", StatusCodes.Status409Conflict, "Este pedido ya terminó.");

    /// <summary>HU-24 (RN-19): la entrega solo se confirma en un pedido confirmado.</summary>
    public static readonly ApiError NotConfirmed = new(
        "orders.not_confirmed", StatusCodes.Status409Conflict, "Primero se tiene que aceptar el pedido.");

    /// <summary>Falta la conversacion en la consulta.</summary>
    public static readonly ApiError InvalidConversation = new(
        "orders.invalid_conversation", StatusCodes.Status400BadRequest, "No pudimos cargar el pedido. Vuelve a abrir el chat.");

    /// <summary>HU-55: la accion es de una transferencia y el pedido se paga de otra forma (o aun no se elige).</summary>
    public static readonly ApiError NotTransfer = new(
        "orders.not_transfer", StatusCodes.Status409Conflict, "Este pedido no se paga por transferencia.");

    /// <summary>HU-55: quien vende quito su cuenta para recibir pagos (o la transferencia esta apagada, RNF-15).</summary>
    public static readonly ApiError TransferUnavailable = new(
        "orders.transfer_unavailable", StatusCodes.Status409Conflict, "Quien vende ya no tiene una cuenta para recibir pagos. Escríbele o paga en efectivo.");

    /// <summary>HU-55: la referencia o clave de rastreo vacia, o fuera de 4 a 30 letras y numeros.</summary>
    public static readonly ApiError InvalidReference = new(
        "orders.invalid_reference", StatusCodes.Status400BadRequest, "Escribe la referencia de tu comprobante: de 4 a 30 letras o números.");

    /// <summary>HU-55: quien vende confirma o rechaza una transferencia que aun no le avisan.</summary>
    public static readonly ApiError TransferNotReported = new(
        "orders.transfer_not_reported", StatusCodes.Status409Conflict, "Todavía no te avisan que transfirieron.");

    /// <summary>HU-55: cambiar la forma de pago despues de avisar "Ya transferi".</summary>
    public static readonly ApiError TransferReported = new(
        "orders.transfer_reported", StatusCodes.Status409Conflict, "Ya avisaste que transferiste. Espera a que lo confirmen o escríbele a quien vende.");

    /// <summary>HU-55: el codigo de entrega es del pago en efectivo.</summary>
    public static readonly ApiError NotCash = new(
        "orders.not_cash", StatusCodes.Status409Conflict, "Este pedido no se paga en efectivo.");

    /// <summary>HU-55: el codigo no son 4 digitos (no gasta un intento).</summary>
    public static readonly ApiError InvalidCodeFormat = new(
        "orders.invalid_code_format", StatusCodes.Status400BadRequest, "Escribe los 4 dígitos del código.");

    /// <summary>HU-55: el codigo no coincide (gasta un intento; la app muestra cuantos quedan).</summary>
    public static readonly ApiError InvalidCode = new(
        "orders.invalid_code", StatusCodes.Status422UnprocessableEntity, "El código no coincide. Pídeselo de nuevo a quien compra.");

    /// <summary>HU-55: sin intentos; la entrega se confirma como en HU-24 (los dos desde el pedido).</summary>
    public static readonly ApiError CodeLocked = new(
        "orders.code_locked", StatusCodes.Status409Conflict, "Se acabaron los intentos del código. Confirmen la entrega los dos desde el pedido.");

    /// <summary>HU-62: "Ya llegue" se avisa desde 2 horas antes de la entrega.</summary>
    public static readonly ApiError ArrivalTooEarly = new(
        "orders.arrival_too_early", StatusCodes.Status409Conflict, "Todavía falta para la entrega. Avisa cuando ya estés en el punto.");
}

/// <summary>Errores de los apartados (HU-52; catalogo canonico: docs/errores.md).</summary>
internal static class ReservationsErrors
{
    /// <summary>Inexistente o ajeno (OWASP API1: no revela cual).</summary>
    public static readonly ApiError NotFound = new(
        "reservations.not_found", StatusCodes.Status404NotFound, "No encontramos este apartado.");

    /// <summary>La publicacion ya no esta disponible (pausada, vendida, agotada o de otro campus).</summary>
    public static readonly ApiError ProductUnavailable = new(
        "reservations.product_unavailable", StatusCodes.Status409Conflict, "Esta publicación ya no está disponible para apartar.");

    /// <summary>La publicacion es de quien quiere apartarla.</summary>
    public static readonly ApiError OwnProduct = new(
        "reservations.own_product", StatusCodes.Status409Conflict, "No puedes apartar tu propia publicación.");

    /// <summary>HU-57: bloqueo en cualquier direccion (no revela quien bloqueo).</summary>
    public static readonly ApiError Blocked = new(
        "reservations.blocked", StatusCodes.Status403Forbidden, "No puedes apartar con esta persona.");

    /// <summary>Uno activo a la vez (decision de la usuaria del 15-Nov-2026).</summary>
    public static readonly ApiError LimitReached = new(
        "reservations.limit_reached", StatusCodes.Status409Conflict, "Ya tienes un apartado activo. Recógelo o cancélalo para apartar otro.");

    /// <summary>El punto no es de los activos del campus.</summary>
    public static readonly ApiError InvalidMeetingPoint = new(
        "reservations.invalid_meeting_point", StatusCodes.Status400BadRequest, "Elige un punto de entrega del campus.");

    /// <summary>La hora no cae dentro de las 24 h del apartado, entre las 7:00 y las 21:00.</summary>
    public static readonly ApiError InvalidPickupTime = new(
        "reservations.invalid_pickup_time", StatusCodes.Status400BadRequest, "Elige una hora dentro de las próximas 24 horas, entre las 7:00 y las 21:00.");

    /// <summary>El apartado ya se recogio, se cancelo o vencio.</summary>
    public static readonly ApiError NotActive = new(
        "reservations.not_active", StatusCodes.Status409Conflict, "Este apartado ya no está activo.");

    /// <summary>Verificar y entregar es de quien vende.</summary>
    public static readonly ApiError OnlySeller = new(
        "reservations.only_seller", StatusCodes.Status403Forbidden, "Solo quien vende puede verificar el apartado.");

    /// <summary>El codigo no son 6 digitos (no gasta un intento).</summary>
    public static readonly ApiError InvalidCodeFormat = new(
        "reservations.invalid_code_format", StatusCodes.Status400BadRequest, "Escribe los 6 dígitos del código.");

    /// <summary>El codigo no coincide (gasta un intento).</summary>
    public static readonly ApiError InvalidCode = new(
        "reservations.invalid_code", StatusCodes.Status422UnprocessableEntity, "El código no coincide. Revisa que sea el apartado de esta persona.");

    /// <summary>Sin intentos: el apartado se cancela o vence; la venta se hace con un pedido (HU-23).</summary>
    public static readonly ApiError CodeLocked = new(
        "reservations.code_locked", StatusCodes.Status409Conflict, "Se acabaron los intentos del código. Escríbele a quien apartó.");

    /// <summary>"Confirmar entrega" antes de verificar el codigo.</summary>
    public static readonly ApiError NotVerified = new(
        "reservations.not_verified", StatusCodes.Status409Conflict, "Primero verifica el código de quien apartó.");
}

/// <summary>Errores de la cuenta para recibir pagos (HU-55; catalogo canonico: docs/errores.md).</summary>
internal static class PayoutsErrors
{
    /// <summary>RNF-15: sin llave de cifrado no se guardan cuentas (el efectivo sigue igual).</summary>
    public static readonly ApiError Unavailable = new(
        "payouts.unavailable", StatusCodes.Status503ServiceUnavailable, "Por ahora no podemos guardar cuentas. Tus compradores pueden pagarte en efectivo.");

    /// <summary>Titular vacio o de mas de 80 caracteres.</summary>
    public static readonly ApiError InvalidHolder = new(
        "payouts.invalid_holder", StatusCodes.Status400BadRequest, "Escribe el nombre del titular como aparece en tu banco.");

    /// <summary>El titular no es la persona verificada (la cuenta debe estar a su nombre).</summary>
    public static readonly ApiError HolderMismatch = new(
        "payouts.holder_mismatch", StatusCodes.Status422UnprocessableEntity, "El titular debe coincidir con tu nombre verificado. Usa una cuenta a tu nombre.");

    /// <summary>No son 18 digitos o el dígito verificador no coincide.</summary>
    public static readonly ApiError InvalidClabe = new(
        "payouts.invalid_clabe", StatusCodes.Status400BadRequest, "Revisa tu CLABE: son 18 dígitos y la encuentras en la app de tu banco.");

    /// <summary>La clave de banco (3 primeros digitos) no es de un participante de SPEI.</summary>
    public static readonly ApiError UnknownBank = new(
        "payouts.unknown_bank", StatusCodes.Status422UnprocessableEntity, "No reconocemos el banco de esta CLABE. Revisa los primeros 3 dígitos.");
}
