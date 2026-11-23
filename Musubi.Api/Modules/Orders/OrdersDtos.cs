using System;
using System.Collections.Generic;

namespace Musubi.Api.Modules.Orders;

// DTOs de los pedidos (HU-23 y HU-24). Filas canonicas en docs/endpoints.md (M-08).

/// <summary>"Crear pedido" desde el resumen del chat (maqueta <c>Acuerdo-confirmado</c>).</summary>
/// <param name="ConversationId">La conversacion de la que sale (RN-21).</param>
/// <param name="Quantity">Piezas (1 a 10, RN-17).</param>
/// <param name="MeetingPoint">Slug del punto de entrega.</param>
/// <param name="MeetingDetail">Precision libre del lugar (hasta 120 caracteres), o <c>null</c>.</param>
/// <param name="DeliveryAt">Fecha y hora de la entrega (de 30 minutos a 14 dias, de 7:00 a 21:00 del campus).</param>
/// <param name="AgreementId">
/// HU-34 (RF-47): el acuerdo con IA que confirmaron los dos. Con el, los demas campos se toman del acuerdo (incluido el
/// precio acordado) y se ignoran los del cuerpo; al crear el pedido el acuerdo se borra.
/// </param>
public sealed record CreateOrderRequest(
    string? ConversationId,
    int? Quantity,
    string? MeetingPoint,
    string? MeetingDetail,
    DateTime? DeliveryAt,
    string? AgreementId = null);

/// <summary>"¿Como quieres pagar?" (maqueta <c>Pedido-pago</c>).</summary>
/// <param name="Method"><c>cash</c> (efectivo al entregar) o <c>transfer</c> (HU-55).</param>
public sealed record SetPaymentRequest(string? Method);

/// <summary>Un pedido, visto por una de sus partes.</summary>
/// <param name="Id">Id.</param>
/// <param name="Number">Numero visible (<c>MS-1001</c>).</param>
/// <param name="Status"><c>pending</c>, <c>confirmed</c>, <c>completed</c> o <c>cancelled</c>.</param>
/// <param name="Role">Quien consulta: <c>buyer</c> o <c>seller</c>.</param>
/// <param name="ConversationId">La conversacion de la que salio.</param>
/// <param name="Item">Lo que se compra.</param>
/// <param name="Buyer">Quien compra.</param>
/// <param name="Seller">Quien vende.</param>
/// <param name="Delivery">Donde y cuando.</param>
/// <param name="Payment">Como se paga, o <c>null</c> mientras no se elige.</param>
/// <param name="TransferAvailable">Si quien vende ya registro su cuenta (HU-55; hoy siempre <c>false</c>).</param>
/// <param name="CreatedAt">Alta (UTC).</param>
/// <param name="Handover">Quien ya confirmo la entrega (HU-24, RN-19).</param>
/// <param name="Cancellation">Quien cancelo y por que (HU-24, RN-20), o <c>null</c>.</param>
/// <param name="UpdatedAt">Ultimo cambio (UTC).</param>
/// <param name="RatedByMe">Quien consulta ya califico el pedido (HU-27, HU-45).</param>
/// <param name="RateUntil">Hasta cuando se puede calificar (7 dias despues de completado), o <c>null</c> si no esta completado.</param>
public sealed record OrderResponse(
    string Id,
    string Number,
    string Status,
    string Role,
    string ConversationId,
    OrderItemResponse Item,
    OrderPartyResponse Buyer,
    OrderPartyResponse Seller,
    OrderDeliveryResponse Delivery,
    OrderPaymentResponse? Payment,
    bool TransferAvailable,
    DateTime CreatedAt,
    OrderHandoverResponse Handover,
    OrderCancellationResponse? Cancellation,
    DateTime UpdatedAt,
    bool RatedByMe,
    DateTime? RateUntil);

/// <summary>"Rechazar" o "Cancelar pedido" (HU-24, RN-20).</summary>
/// <param name="Reason">
/// Un codigo de la lista: <c>changed_mind</c> (solo quien compra), <c>unavailable</c> (solo quien vende),
/// <c>no_agreement</c>, <c>schedule</c>, <c>no_show</c> u <c>other</c>.
/// </param>
public sealed record CancelOrderRequest(string? Reason);

/// <summary>La entrega confirmada por cada parte (HU-24, RN-19) y quien ya llego al punto (HU-62).</summary>
/// <param name="BuyerConfirmedAt">Cuando quien compra marco "Recibi" (UTC), o <c>null</c>.</param>
/// <param name="SellerConfirmedAt">Cuando quien vende marco "Entregue" (UTC), o <c>null</c>.</param>
/// <param name="BuyerArrivedAt">Cuando quien compra aviso "Ya llegue" (UTC), o <c>null</c>.</param>
/// <param name="SellerArrivedAt">Cuando quien vende aviso "Ya llegue" (UTC), o <c>null</c>.</param>
public sealed record OrderHandoverResponse(DateTime? BuyerConfirmedAt, DateTime? SellerConfirmedAt, DateTime? BuyerArrivedAt, DateTime? SellerArrivedAt);

/// <summary>La cancelacion (HU-24, RN-20).</summary>
/// <param name="By"><c>buyer</c>, <c>seller</c> o <c>system</c> (una cuenta borrada).</param>
/// <param name="Reason">El codigo del motivo (incluye <c>account_deleted</c>).</param>
/// <param name="At">Cuando (UTC).</param>
public sealed record OrderCancellationResponse(string By, string Reason, DateTime At);

/// <summary>Lo que se compra (copia del momento del pedido).</summary>
/// <param name="Source"><c>product</c> u <c>offer</c>.</param>
/// <param name="SourceId">La publicacion o la oferta.</param>
/// <param name="ProductId">La publicacion, o <c>null</c> en una oferta directa.</param>
/// <param name="Title">Nombre del producto.</param>
/// <param name="Photo">Ruta de la foto (<c>/media/...</c>), o <c>null</c>.</param>
/// <param name="PriceCents">Precio unitario en centavos.</param>
/// <param name="Quantity">Piezas.</param>
/// <param name="TotalCents">Total en centavos.</param>
public sealed record OrderItemResponse(
    string Source,
    string SourceId,
    string? ProductId,
    string Title,
    string? Photo,
    long PriceCents,
    int Quantity,
    long TotalCents);

/// <summary>Una de las partes.</summary>
/// <param name="Id">Id del usuario.</param>
/// <param name="Name">Nombre corto.</param>
public sealed record OrderPartyResponse(string Id, string Name);

/// <summary>La entrega.</summary>
/// <param name="Point">Slug del punto.</param>
/// <param name="PointName">Nombre del punto.</param>
/// <param name="Detail">Precision del lugar, o <c>null</c>.</param>
/// <param name="At">Fecha y hora (UTC).</param>
public sealed record OrderDeliveryResponse(string Point, string PointName, string? Detail, DateTime At);

/// <summary>El pago elegido (HU-23) y, desde HU-55, la transferencia o el codigo de entrega.</summary>
/// <param name="Method"><c>cash</c> o <c>transfer</c>.</param>
/// <param name="ChosenAt">Cuando se eligio (UTC).</param>
/// <param name="Reference">Transferencia: la referencia que capturo quien compra, o <c>null</c>.</param>
/// <param name="ReportedAt">Transferencia: cuando quien compra aviso "Ya transferi" (UTC), o <c>null</c>.</param>
/// <param name="NotReceivedAt">Transferencia: cuando quien vende dijo "Todavia no me llega" (UTC), o <c>null</c>.</param>
/// <param name="ValidatedAt">Transferencia: cuando quien vende confirmo que le llego (UTC), o <c>null</c>.</param>
/// <param name="DeliveryCode">Efectivo: el codigo de 4 digitos; solo a quien compra y con el pedido confirmado.</param>
/// <param name="CodeAttemptsLeft">Efectivo: intentos que le quedan a quien vende; solo a quien vende, confirmado.</param>
public sealed record OrderPaymentResponse(
    string Method,
    DateTime ChosenAt,
    string? Reference = null,
    DateTime? ReportedAt = null,
    DateTime? NotReceivedAt = null,
    DateTime? ValidatedAt = null,
    string? DeliveryCode = null,
    int? CodeAttemptsLeft = null);

/// <summary>
/// "Paga por transferencia" (maqueta <c>Pago-transferencia</c>) y "Búscalo en la app de tu banco" (<c>Pago-vendedor</c>).
/// </summary>
/// <param name="AmountCents">"Transfiere exactamente $500.00".</param>
/// <param name="BankName">Banco receptor.</param>
/// <param name="HolderName">Titular de la cuenta.</param>
/// <param name="Clabe">
/// Para quien compra, los 18 digitos ("012 180 0154 2378 9134"); para quien vende, enmascarada ("•••• 9134").
/// </param>
/// <param name="ClabeLast4">Ultimos 4 digitos.</param>
/// <param name="Concept">"Concepto · escríbelo tal cual": el numero del pedido (<c>MS-2481</c>).</param>
/// <param name="Reference">La referencia que capturo quien compra, o <c>null</c>.</param>
/// <param name="ReportedAt">Cuando aviso "Ya transferi" (UTC), o <c>null</c>.</param>
/// <param name="NotReceivedAt">Cuando quien vende dijo "Todavia no me llega" (UTC), o <c>null</c>.</param>
/// <param name="ValidatedAt">Cuando quien vende confirmo que le llego (UTC), o <c>null</c>.</param>
public sealed record TransferDetailsResponse(
    long AmountCents,
    string BankName,
    string HolderName,
    string Clabe,
    string ClabeLast4,
    string Concept,
    string? Reference,
    DateTime? ReportedAt,
    DateTime? NotReceivedAt,
    DateTime? ValidatedAt);

/// <summary>"Captura la referencia" y "Enviar referencia a Diego" (HU-55).</summary>
/// <param name="Reference">La referencia o clave de rastreo del comprobante (4 a 30 letras o numeros).</param>
public sealed record ReportTransferRequest(string? Reference);

/// <summary>El codigo de entrega que dicta quien compra (HU-55, pago en efectivo).</summary>
/// <param name="Code">Cuatro digitos.</param>
public sealed record DeliveryCodeRequest(string? Code);

/// <summary>Un banco de la CLABE.</summary>
/// <param name="Code">Clave de 3 digitos (<c>012</c>).</param>
/// <param name="Name">Nombre ("BBVA México").</param>
public sealed record BankResponse(string Code, string Name);

/// <summary>Los bancos que se reconocen en una CLABE (participantes de SPEI).</summary>
/// <param name="Items">En el orden en que se muestran.</param>
public sealed record BankListResponse(IReadOnlyList<BankResponse> Items);

/// <summary>"Cuenta para recibir pagos" (maqueta <c>Cuenta-pagos</c>): nunca la CLABE completa.</summary>
/// <param name="HolderName">Titular.</param>
/// <param name="BankCode">Clave del banco.</param>
/// <param name="BankName">Banco.</param>
/// <param name="ClabeLast4">Ultimos 4 digitos ("•••• 4821").</param>
/// <param name="UpdatedAt">Ultimo cambio (UTC).</param>
public sealed record PayoutAccountResponse(string HolderName, string BankCode, string BankName, string ClabeLast4, DateTime UpdatedAt);

/// <summary>Mi cuenta para recibir pagos.</summary>
/// <param name="Available">Si se pueden guardar cuentas (sin llave de cifrado, <c>false</c>: RNF-15).</param>
/// <param name="Account">La cuenta, o <c>null</c> si no tengo una.</param>
public sealed record PayoutAccountStatus(bool Available, PayoutAccountResponse? Account);

/// <summary>"Guardar cuenta" (maqueta <c>Cuenta-pagos</c>).</summary>
/// <param name="HolderName">Titular: debe coincidir con mi nombre verificado.</param>
/// <param name="Clabe">La CLABE (18 digitos; se aceptan espacios y guiones).</param>
public sealed record SavePayoutAccountRequest(string? HolderName, string? Clabe);

/// <summary>Los pedidos de una conversacion, del mas nuevo al mas viejo.</summary>
/// <param name="Items">Los pedidos.</param>
public sealed record OrderListResponse(IReadOnlyList<OrderResponse> Items);

/// <summary>"Vendidos" de <c>Mis-productos</c> (HU-26): mis ventas completadas, con el numero y el total real.</summary>
/// <param name="Items">Las ventas (cada una es un pedido con <c>role: seller</c>), de la mas nueva a la mas vieja.</param>
/// <param name="NextCursor">Cursor de la pagina siguiente, o <c>null</c> si no hay mas.</param>
/// <param name="Count">Cuantas ventas completadas hay en total.</param>
/// <param name="TotalCents">Lo que suman, en centavos.</param>
public sealed record SalesPage(IReadOnlyList<OrderResponse> Items, string? NextCursor, long Count, long TotalCents);

/// <summary>Los numeros de las pestanas de "Mis pedidos" (HU-25).</summary>
/// <param name="Active">Pendientes y confirmados.</param>
/// <param name="Completed">Completados.</param>
/// <param name="Cancelled">Cancelados.</param>
public sealed record MyOrderCounts(long Active, long Completed, long Cancelled);

/// <summary>"Mis pedidos" (HU-25): mis compras de la pestana pedida, con los numeros de las tres.</summary>
/// <param name="Items">Los pedidos, del mas nuevo al mas viejo.</param>
/// <param name="NextCursor">Cursor de la pagina siguiente, o <c>null</c> si no hay mas.</param>
/// <param name="Total">Cuantos hay en la pestana (sin pestana, todos).</param>
/// <param name="Counts">Los numeros de las tres pestanas.</param>
public sealed record MyOrderPage(IReadOnlyList<OrderResponse> Items, string? NextCursor, long Total, MyOrderCounts Counts);

/// <summary>Un punto de entrega del campus.</summary>
/// <param name="Slug">Identificador.</param>
/// <param name="Name">Nombre ("Biblioteca").</param>
/// <param name="Suggested">Es el edificio de la carrera de quien consulta.</param>
public sealed record MeetingPointResponse(string Slug, string Name, bool Suggested);

/// <summary>Los puntos de entrega del campus, en su orden.</summary>
/// <param name="Items">Los puntos.</param>
public sealed record MeetingPointsResponse(IReadOnlyList<MeetingPointResponse> Items);
