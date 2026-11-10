using System;
using System.Collections.Generic;

namespace Musubi.Api.Modules.Orders;

// DTOs de los pedidos (HU-23). Filas canonicas en docs/endpoints.md (M-08).

/// <summary>"Crear pedido" desde el resumen del chat (maqueta <c>Acuerdo-confirmado</c>).</summary>
/// <param name="ConversationId">La conversacion de la que sale (RN-21).</param>
/// <param name="Quantity">Piezas (1 a 10, RN-17).</param>
/// <param name="MeetingPoint">Slug del punto de entrega.</param>
/// <param name="MeetingDetail">Precision libre del lugar (hasta 120 caracteres), o <c>null</c>.</param>
/// <param name="DeliveryAt">Fecha y hora de la entrega (de 30 minutos a 14 dias, de 7:00 a 21:00 del campus).</param>
public sealed record CreateOrderRequest(
    string? ConversationId,
    int? Quantity,
    string? MeetingPoint,
    string? MeetingDetail,
    DateTime? DeliveryAt);

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
    DateTime CreatedAt);

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

/// <summary>El pago elegido.</summary>
/// <param name="Method"><c>cash</c> o <c>transfer</c>.</param>
/// <param name="ChosenAt">Cuando se eligio (UTC).</param>
public sealed record OrderPaymentResponse(string Method, DateTime ChosenAt);

/// <summary>Los pedidos de una conversacion, del mas nuevo al mas viejo.</summary>
/// <param name="Items">Los pedidos.</param>
public sealed record OrderListResponse(IReadOnlyList<OrderResponse> Items);

/// <summary>Un punto de entrega del campus.</summary>
/// <param name="Slug">Identificador.</param>
/// <param name="Name">Nombre ("Biblioteca").</param>
/// <param name="Suggested">Es el edificio de la carrera de quien consulta.</param>
public sealed record MeetingPointResponse(string Slug, string Name, bool Suggested);

/// <summary>Los puntos de entrega del campus, en su orden.</summary>
/// <param name="Items">Los puntos.</param>
public sealed record MeetingPointsResponse(IReadOnlyList<MeetingPointResponse> Items);
