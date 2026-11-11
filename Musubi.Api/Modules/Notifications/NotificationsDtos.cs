using System;
using System.Collections.Generic;

namespace Musubi.Api.Modules.Notifications;

// Contratos HTTP de M-09 (HU-31). Filas canonicas en docs/endpoints.md.

/// <summary>El token de FCM de este dispositivo.</summary>
/// <param name="Token">Token (1 a 512 caracteres, sin espacios).</param>
public sealed record DeviceRequest(string? Token);

/// <summary>Un aviso del centro de notificaciones.</summary>
/// <param name="Id">Id.</param>
/// <param name="Type">
/// <c>order_created</c>, <c>agreement_confirmed</c>, <c>offer_received</c>, <c>offer_accepted</c>,
/// <c>offer_rejected</c>, <c>request_expiring</c> o <c>request_expired</c>.
/// </param>
/// <param name="Category">Pestana: <c>orders</c>, <c>requests</c> o <c>matches</c>.</param>
/// <param name="Title">Titulo.</param>
/// <param name="Body">Detalle.</param>
/// <param name="Target">Lo que abre al tocarlo.</param>
/// <param name="Read">Si ya se leyo.</param>
/// <param name="CreatedAt">Cuando (UTC).</param>
public sealed record NotificationResponse(
    string Id,
    string Type,
    string Category,
    string Title,
    string Body,
    NotificationTargetResponse Target,
    bool Read,
    DateTime CreatedAt);

/// <summary>Lo que abre un aviso.</summary>
/// <param name="Type"><c>conversation</c>, <c>order</c> o <c>wanted</c>.</param>
/// <param name="Id">Su id.</param>
public sealed record NotificationTargetResponse(string Type, string Id);

/// <summary>Pagina de avisos, del mas nuevo al mas viejo.</summary>
/// <param name="Items">Los avisos.</param>
/// <param name="Unread">Cuantos sin leer tiene en total (el punto de la campana).</param>
/// <param name="NextCursor">Cursor para los anteriores, o <c>null</c>.</param>
public sealed record NotificationPage(IReadOnlyList<NotificationResponse> Items, long Unread, string? NextCursor);
