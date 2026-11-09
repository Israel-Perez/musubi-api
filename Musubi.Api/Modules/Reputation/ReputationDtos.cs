using System;
using System.Collections.Generic;

namespace Musubi.Api.Modules.Reputation;

// Contratos HTTP de M-13 (HU-28 reportes, HU-57 bloqueos). Filas canonicas en docs/endpoints.md.

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
