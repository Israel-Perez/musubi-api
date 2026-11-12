using System;
using System.Collections.Generic;

namespace Musubi.Api.Modules.Reputation;

// Contratos HTTP de M-13 (HU-28 reportes, HU-57 bloqueos, HU-27/HU-45 calificaciones). Filas canonicas en docs/endpoints.md.

/// <summary>Enviar un reporte (maqueta <c>reportSheet</c>: motivo y "Cuentanos que paso").</summary>
/// <param name="TargetType"><c>general</c> (desde Ajustes), <c>user</c> (desde el chat) o <c>product</c>.</param>
/// <param name="TargetId">Id de la persona o de la publicacion; vacio en <c>general</c>.</param>
/// <param name="ConversationId">Conversacion donde paso (opcional, solo con <c>user</c>).</param>
/// <param name="Reason"><c>payment</c>, <c>not_as_described</c>, <c>no_show</c>, <c>off_platform</c> u <c>other</c>.</param>
/// <param name="Details">Que paso (opcional, hasta 1000 caracteres).</param>
public sealed record CreateReportRequest(string TargetType, string? TargetId, string? ConversationId, string Reason, string? Details);

/// <summary>Reporte recibido.</summary>
/// <param name="Id">Id del reporte (para darle seguimiento con el equipo).</param>
/// <param name="CreatedAt">Recepcion (UTC).</param>
public sealed record ReportResponse(string Id, DateTime CreatedAt);

/// <summary>Bloquear a una persona (maqueta <c>chatMoreSheet</c> → "Bloquear a …").</summary>
/// <param name="UserId">Id de la persona.</param>
public sealed record BlockRequest(string UserId);

/// <summary>Una persona bloqueada (maqueta <c>aj-bloqueados</c>: avatar, nombre y "Desbloquear").</summary>
/// <param name="Id">Id de la persona.</param>
/// <param name="Name">Nombre completo.</param>
/// <param name="Photo">Ruta de su foto de perfil (<c>/media/avatars/...</c>), o <c>null</c> (se ven sus iniciales).</param>
/// <param name="BlockedAt">Cuando la bloqueaste (UTC).</param>
public sealed record BlockedUserResponse(string Id, string Name, string? Photo, DateTime BlockedAt);

/// <summary>"Usuarios bloqueados", del bloqueo mas reciente al mas viejo.</summary>
/// <param name="Items">Las personas.</param>
public sealed record BlockedUsersResponse(IReadOnlyList<BlockedUserResponse> Items);

/// <summary>"Enviar calificacion" (maqueta <c>Calificar</c>, HU-27 y HU-45).</summary>
/// <param name="Stars">De 1 a 5 (RN-25).</param>
/// <param name="Tags">"¿Que destacas?": codigos de la lista de quien recibe la calificacion (opcional).</param>
/// <param name="Comment">"Cuentale a la comunidad" (opcional, hasta 280 caracteres, se modera).</param>
public sealed record RateOrderRequest(int? Stars, IReadOnlyList<string>? Tags, string? Comment);

/// <summary>La calificacion que acabas de enviar.</summary>
/// <param name="Stars">De 1 a 5.</param>
/// <param name="Tags">Lo que destacaste.</param>
/// <param name="Comment">El comentario, o <c>null</c>.</param>
/// <param name="CreatedAt">Cuando (UTC).</param>
public sealed record RatingResponse(int Stars, IReadOnlyList<string> Tags, string? Comment, DateTime CreatedAt);

/// <summary>Una opinion del perfil (maqueta <c>Perfil-vendedor</c> → "Lo que dicen de Valeria").</summary>
/// <param name="Id">Id de la calificacion (cursor).</param>
/// <param name="Stars">De 1 a 5.</param>
/// <param name="Tags">Lo que destaco quien califico.</param>
/// <param name="Comment">El comentario, o <c>null</c>.</param>
/// <param name="RaterName">Nombre corto de quien califico ("Luis M.").</param>
/// <param name="Role">Como participo la persona del perfil: <c>seller</c> o <c>buyer</c>.</param>
/// <param name="CreatedAt">Cuando (UTC).</param>
public sealed record ProfileRatingResponse(
    string Id,
    int Stars,
    IReadOnlyList<string> Tags,
    string? Comment,
    string RaterName,
    string Role,
    DateTime CreatedAt);

/// <summary>Una pagina de opiniones, de la mas nueva a la mas vieja.</summary>
/// <param name="Items">Las opiniones.</param>
/// <param name="NextCursor">Cursor de la siguiente pagina, o <c>null</c> si no hay mas.</param>
public sealed record ProfileRatingPage(IReadOnlyList<ProfileRatingResponse> Items, string? NextCursor);

