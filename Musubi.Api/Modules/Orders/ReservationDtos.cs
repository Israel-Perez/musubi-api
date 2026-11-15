using System;
using System.Collections.Generic;

namespace Musubi.Api.Modules.Orders;

// DTOs de los apartados (HU-52). Filas canonicas en docs/endpoints.md (M-08 · Apartados).

/// <summary>"Apartar" (maqueta <c>Apartar</c>): la publicacion, el punto y cuando pasa.</summary>
/// <param name="ProductId">La publicacion.</param>
/// <param name="MeetingPoint">El punto de entrega (<c>slug</c> de <c>GET /orders/meeting-points</c>).</param>
/// <param name="PickupAt">Cuando pasa a recogerlo (UTC): dentro de las 24 h del apartado, de 7:00 a 21:00 del campus.</param>
public sealed record CreateReservationRequest(string? ProductId, string? MeetingPoint, DateTime? PickupAt);

/// <summary>El codigo que escanea o escribe quien vende (<c>Verificar-comprador</c>).</summary>
/// <param name="Code">Los 6 digitos.</param>
public sealed record VerifyReservationRequest(string? Code);

/// <summary>Un apartado, visto por una de sus partes.</summary>
/// <param name="Id">Id.</param>
/// <param name="Number"><c>AP-1001</c>.</param>
/// <param name="Status"><c>active</c>, <c>collected</c>, <c>cancelled</c> o <c>expired</c>.</param>
/// <param name="Role"><c>buyer</c> o <c>seller</c> (quien consulta).</param>
/// <param name="ProductId">La publicacion.</param>
/// <param name="Title">Nombre (copia).</param>
/// <param name="Photo">Ruta de la portada, o <c>null</c>.</param>
/// <param name="PriceCents">Precio de la pieza.</param>
/// <param name="Buyer">Quien aparta.</param>
/// <param name="Seller">Quien vende.</param>
/// <param name="PointSlug">El punto de entrega.</param>
/// <param name="PointName">Su nombre.</param>
/// <param name="PickupAt">Cuando pasa (UTC).</param>
/// <param name="ExpiresAt">Vence (UTC): 24 h despues de apartar.</param>
/// <param name="CreatedAt">Alta (UTC).</param>
/// <param name="Code">Los 6 digitos; solo a quien aparta y mientras esta activo.</param>
/// <param name="QrPayload">Lo que lleva su QR; solo a quien aparta y mientras esta activo.</param>
/// <param name="CodeAttemptsLeft">Intentos que le quedan a quien vende; solo a quien vende y activo.</param>
/// <param name="VerifiedAt">Cuando quien vende verifico el codigo (UTC), o <c>null</c>.</param>
/// <param name="OrderId">El pedido completado que nacio al entregar, o <c>null</c>.</param>
/// <param name="ConversationId">La conversacion de los dos ("Mensaje").</param>
/// <param name="CancelledBy"><c>buyer</c>, <c>seller</c> o <c>system</c>, o <c>null</c>.</param>
public sealed record ReservationResponse(
    string Id,
    string Number,
    string Status,
    string Role,
    string ProductId,
    string Title,
    string? Photo,
    long PriceCents,
    OrderPartyResponse Buyer,
    OrderPartyResponse Seller,
    string PointSlug,
    string PointName,
    DateTime PickupAt,
    DateTime ExpiresAt,
    DateTime CreatedAt,
    string? Code,
    string? QrPayload,
    int? CodeAttemptsLeft,
    DateTime? VerifiedAt,
    string? OrderId,
    string ConversationId,
    string? CancelledBy);

/// <summary>Los numeros de las pestanas de "Mis apartados".</summary>
/// <param name="Bought">"Yo aparté".</param>
/// <param name="Sold">"Me apartaron".</param>
public sealed record ReservationCounts(long Bought, long Sold);

/// <summary>"Mis apartados" (maqueta <c>Mis-apartados</c>): una pestana y los numeros de las dos.</summary>
/// <param name="Items">Del mas nuevo al mas viejo (hasta 50).</param>
/// <param name="Counts">Los numeros.</param>
public sealed record ReservationPage(IReadOnlyList<ReservationResponse> Items, ReservationCounts Counts);
