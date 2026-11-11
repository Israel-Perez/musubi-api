using System;
using System.Collections.Generic;

namespace Musubi.Api.Modules.GenAi;

// Contratos HTTP de M-10 (HU-32/33/34; maquetas Chat, Acuerdo-esperando y Acuerdo-confirmado). Filas canonicas en
// docs/endpoints.md.

/// <summary>"Resumir acuerdo": pedirle a la IA la propuesta de una conversacion propia.</summary>
/// <param name="ConversationId">La conversacion.</param>
public sealed record RequestAgreementRequest(string? ConversationId);

/// <summary>
/// Corregir la propuesta ("Editar producto", "Editar precio"…). Lo que no se manda no cambia; un texto vacio en el
/// detalle o en las condiciones lo quita. Cualquier cambio borra las dos confirmaciones.
/// </summary>
/// <param name="Revision">La version que se esta viendo (si cambio antes: <c>agreements.changed</c>).</param>
/// <param name="Title">Producto (1 a 80).</param>
/// <param name="PriceCents">Precio por pieza, en centavos.</param>
/// <param name="Quantity">Piezas (1 a lo disponible).</param>
/// <param name="MeetingPoint">Punto del campus (<c>slug</c> de <c>GET /orders/meeting-points</c>).</param>
/// <param name="MeetingDetail">Precision del lugar (hasta 120).</param>
/// <param name="DeliveryAt">Fecha y hora (de 30 minutos a 14 dias, de 7:00 a 21:00 del campus).</param>
/// <param name="Conditions">Otras condiciones (hasta 200).</param>
public sealed record UpdateAgreementRequest(
    int? Revision,
    string? Title,
    long? PriceCents,
    int? Quantity,
    string? MeetingPoint,
    string? MeetingDetail,
    DateTime? DeliveryAt,
    string? Conditions);

/// <summary>"Confirmar acuerdo" sobre la version que se esta viendo (RN-41).</summary>
/// <param name="Revision">La version.</param>
public sealed record ConfirmAgreementRequest(int? Revision);

/// <summary>El acuerdo de una conversacion, para una de sus partes.</summary>
/// <param name="Id">Id.</param>
/// <param name="ConversationId">La conversacion.</param>
/// <param name="Revision">Version (sube con cada cambio).</param>
/// <param name="Status"><c>proposed</c> ("Esperando confirmacion") o <c>confirmed</c> ("Acuerdo confirmado").</param>
/// <param name="Role">Papel de quien consulta: <c>buyer</c> o <c>seller</c>.</param>
/// <param name="RequestedBy">Quien pidio el resumen: <c>buyer</c> o <c>seller</c>.</param>
/// <param name="Buyer">Quien compra.</param>
/// <param name="Seller">Quien vende.</param>
/// <param name="Proposal">Los datos; <c>null</c> es "No identificado" (RN-42).</param>
/// <param name="NotIdentified">Lo que la IA no encontro (si ya tiene valor, la app lo marca "Agregada").</param>
/// <param name="Missing">Lo que falta para poder confirmar.</param>
/// <param name="MaxQuantity">Las piezas que se pueden pedir.</param>
/// <param name="BuyerConfirmedAt">Confirmacion de quien compra (UTC), o <c>null</c>.</param>
/// <param name="SellerConfirmedAt">Confirmacion de quien vende (UTC), o <c>null</c>.</param>
/// <param name="CreatedAt">Creacion (UTC).</param>
/// <param name="UpdatedAt">Ultimo cambio (UTC).</param>
/// <param name="ExpiresAt">Cuando se borra solo (UTC, 30 dias; RN-44).</param>
public sealed record AgreementResponse(
    string Id,
    string ConversationId,
    int Revision,
    string Status,
    string Role,
    string RequestedBy,
    AgreementPartyResponse Buyer,
    AgreementPartyResponse Seller,
    AgreementProposalResponse Proposal,
    IReadOnlyList<string> NotIdentified,
    IReadOnlyList<string> Missing,
    int MaxQuantity,
    DateTime? BuyerConfirmedAt,
    DateTime? SellerConfirmedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime ExpiresAt);

/// <summary>Una parte del acuerdo.</summary>
/// <param name="Id">Id del usuario.</param>
/// <param name="Name">Nombre completo.</param>
public sealed record AgreementPartyResponse(string Id, string Name);

/// <summary>Los datos del acuerdo.</summary>
/// <param name="Title">Producto.</param>
/// <param name="PriceCents">Precio acordado por pieza, o <c>null</c>.</param>
/// <param name="Quantity">Piezas, o <c>null</c>.</param>
/// <param name="MeetingPoint">Slug del punto, o <c>null</c>.</param>
/// <param name="PointName">Nombre del punto, o <c>null</c>.</param>
/// <param name="MeetingDetail">Precision del lugar, o <c>null</c>.</param>
/// <param name="DeliveryAt">Fecha y hora (UTC), o <c>null</c>.</param>
/// <param name="Conditions">Otras condiciones, o <c>null</c>.</param>
public sealed record AgreementProposalResponse(
    string Title,
    long? PriceCents,
    int? Quantity,
    string? MeetingPoint,
    string? PointName,
    string? MeetingDetail,
    DateTime? DeliveryAt,
    string? Conditions);

/// <summary>El acuerdo vigente de una conversacion (cero o uno).</summary>
/// <param name="Items">Los acuerdos.</param>
public sealed record AgreementListResponse(IReadOnlyList<AgreementResponse> Items);
