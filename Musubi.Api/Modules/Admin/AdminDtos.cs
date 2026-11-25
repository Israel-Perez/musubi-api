using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Musubi.Api.Modules.Admin;

// Contrato de /api/v1/admin (filas en docs/endpoints.md; formas en ContractTest/Fixtures/admin-*.json). Solo para
// el rol admin de su campus. Nunca lleva correos ni credenciales (R10).

/// <summary>La bandeja de reportes (HU-39).</summary>
/// <param name="Items">Los reportes, del mas nuevo al mas viejo (hasta 50).</param>
public sealed record AdminReportsResponse(IReadOnlyList<AdminReportResponse> Items);

/// <summary>Un reporte con lo reportado y la persona responsable.</summary>
/// <param name="Id">Reporte.</param>
/// <param name="Reason">Motivo de la maqueta (<c>payment</c>, <c>not_as_described</c>, <c>no_show</c>, <c>off_platform</c>, <c>other</c>).</param>
/// <param name="Details">"Cuentanos que paso", o <c>null</c>.</param>
/// <param name="Status"><c>open</c>, <c>dismissed</c> o <c>upheld</c>.</param>
/// <param name="CreatedAt">Cuando llego (UTC).</param>
/// <param name="Reporter">Quien reporto, o <c>null</c> si borro su cuenta.</param>
/// <param name="Target">Lo reportado.</param>
/// <param name="Subject">La persona responsable, o <c>null</c> en un problema general o si ya no existe.</param>
/// <param name="ConversationId">Conversacion donde paso, si se reporto desde el chat.</param>
/// <param name="Review">La revision, o <c>null</c> si sigue abierto.</param>
public sealed record AdminReportResponse(
    string Id,
    string Reason,
    string? Details,
    string Status,
    DateTime CreatedAt,
    AdminPerson? Reporter,
    AdminReportTarget Target,
    AdminSubject? Subject,
    string? ConversationId,
    AdminReview? Review);

/// <summary>Una persona.</summary>
/// <param name="Id">Usuario.</param>
/// <param name="FullName">Nombre completo.</param>
public sealed record AdminPerson(string Id, string FullName);

/// <summary>Lo reportado.</summary>
/// <param name="Type"><c>general</c>, <c>user</c> o <c>product</c>.</param>
/// <param name="Id">La persona o la publicacion; <c>null</c> en un problema general.</param>
/// <param name="Name">Nombre de la publicacion o de la persona; <c>null</c> si ya no existe.</param>
/// <param name="Photo">Portada de la publicacion, o <c>null</c>.</param>
/// <param name="Status">Estado de la publicacion o de la cuenta; <c>null</c> si ya no existe.</param>
public sealed record AdminReportTarget(string Type, string? Id, string? Name, string? Photo, string? Status);

/// <summary>La persona responsable de lo reportado (RN-27).</summary>
/// <param name="Id">Usuario.</param>
/// <param name="FullName">Nombre completo.</param>
/// <param name="Status"><c>active</c>, <c>suspended</c> o <c>pendingVerification</c>.</param>
/// <param name="UpheldReports">Reportes procedentes en su contra.</param>
/// <param name="SuspensionSuggested">Con 3 o mas procedentes se sugiere suspenderla (RN-27).</param>
public sealed record AdminSubject(string Id, string FullName, string Status, long UpheldReports, bool SuspensionSuggested);

/// <summary>La revision de un reporte.</summary>
/// <param name="At">Cuando (UTC).</param>
/// <param name="Reviewer">Administrador, o <c>null</c> si ya no existe.</param>
/// <param name="Note">Nota interna, o <c>null</c>.</param>
public sealed record AdminReview(DateTime At, AdminPerson? Reviewer, string? Note);

/// <summary>"Descartar" o "Procedente" (HU-39).</summary>
/// <param name="Action">
/// <c>dismiss</c>: no hay nada que retirar. <c>uphold</c>: procedente; con una publicacion, la retira.
/// </param>
/// <param name="Note">Nota interna (hasta 500 caracteres), opcional.</param>
public sealed record ResolveReportRequest([Required, MaxLength(20)] string Action, [MaxLength(2000)] string? Note);

/// <summary>Suspender una cuenta (HU-40).</summary>
/// <param name="Reason">Motivo (hasta 500 caracteres): queda en la bitacora.</param>
public sealed record SuspendAccountRequest([MaxLength(2000)] string? Reason);

/// <summary>Las cuentas suspendidas de un campus (HU-40).</summary>
/// <param name="Items">Las cuentas, de la suspension mas reciente a la mas vieja (hasta 50).</param>
public sealed record AdminAccountsResponse(IReadOnlyList<AdminAccountSummary> Items);

/// <summary>Una cuenta en la lista.</summary>
/// <param name="Id">Usuario.</param>
/// <param name="FullName">Nombre completo.</param>
/// <param name="Status">Estado.</param>
/// <param name="UpdatedAt">Ultimo cambio (UTC): cuando se suspendio.</param>
public sealed record AdminAccountSummary(string Id, string FullName, string Status, DateTime UpdatedAt);

/// <summary>Una cuenta con sus reportes procedentes y su historial de moderacion (HU-40).</summary>
/// <param name="Id">Usuario.</param>
/// <param name="FullName">Nombre completo.</param>
/// <param name="Status"><c>active</c>, <c>suspended</c> o <c>pendingVerification</c>.</param>
/// <param name="IsAdmin">Es administradora (no se puede suspender).</param>
/// <param name="MemberSince">Alta (UTC).</param>
/// <param name="UpheldReports">Reportes procedentes en su contra.</param>
/// <param name="SuspensionSuggested">Con 3 o mas procedentes se sugiere suspenderla (RN-27).</param>
/// <param name="History">Lo que se ha hecho con ella o sus publicaciones (hasta 20, de lo mas nuevo a lo mas viejo).</param>
public sealed record AdminAccountResponse(
    string Id,
    string FullName,
    string Status,
    bool IsAdmin,
    DateTime MemberSince,
    long UpheldReports,
    bool SuspensionSuggested,
    IReadOnlyList<AdminLogEntry> History);

/// <summary>Una accion de la bitacora (RNF-04).</summary>
/// <param name="Action">
/// <c>report_dismissed</c>, <c>report_upheld</c>, <c>product_removed</c>, <c>account_suspended</c> o <c>account_reactivated</c>.
/// </param>
/// <param name="At">Cuando (UTC).</param>
/// <param name="Actor">Administrador, o <c>null</c> si ya no existe.</param>
/// <param name="Reason">Motivo o nota, o <c>null</c>.</param>
public sealed record AdminLogEntry(string Action, DateTime At, AdminPerson? Actor, string? Reason);

/// <summary>Las categorias del campus, tambien las desactivadas (HU-41).</summary>
/// <param name="Items">Las categorias, en el orden de la app.</param>
public sealed record AdminCategoriesResponse(IReadOnlyList<AdminCategoryResponse> Items);

/// <summary>Una categoria.</summary>
/// <param name="Slug">Identificador estable (no cambia al renombrarla).</param>
/// <param name="Name">Nombre visible.</param>
/// <param name="Active">Se puede elegir al publicar y en los filtros.</param>
/// <param name="Order">Orden en la app.</param>
public sealed record AdminCategoryResponse(string Slug, string Name, bool Active, int Order);

/// <summary>"Agregar categoria".</summary>
/// <param name="Name">Nombre (2 a 40 caracteres).</param>
public sealed record CreateCategoryRequest([MaxLength(200)] string? Name);

/// <summary>Renombrar, desactivar o volver a activar una categoria; lo que viene en <c>null</c> no cambia.</summary>
/// <param name="Name">Nombre nuevo (2 a 40 caracteres).</param>
/// <param name="Active">Estado nuevo.</param>
public sealed record UpdateCategoryRequest([MaxLength(200)] string? Name, bool? Active);

/// <summary>"Indicadores de uso" (HU-42): los del apartado 19 de la propuesta, por area.</summary>
/// <param name="Period"><c>week</c> o <c>month</c>.</param>
/// <param name="From">Inicio del periodo (UTC).</param>
/// <param name="To">Fin del periodo (UTC, ahora).</param>
/// <param name="Areas">Adopcion, oferta, demanda, conexion, operaciones, chat, IA, vendedores y confianza, en ese orden.</param>
public sealed record UsageIndicatorsResponse(string Period, DateTime From, DateTime To, IReadOnlyList<IndicatorArea> Areas);

/// <summary>Un area de indicadores.</summary>
/// <param name="Key">Identificador estable (<c>adoption</c>, <c>supply</c>…).</param>
/// <param name="Title">Titulo visible.</param>
/// <param name="Items">Sus indicadores.</param>
public sealed record IndicatorArea(string Key, string Title, IReadOnlyList<Indicator> Items);

/// <summary>Un indicador.</summary>
/// <param name="Key">Identificador estable.</param>
/// <param name="Label">Nombre visible.</param>
/// <param name="Value">El valor, o <c>null</c> si no hay datos para calcularlo (un porcentaje sin base).</param>
/// <param name="Unit"><c>count</c>, <c>percent</c> (0 a 100), <c>average</c>, <c>hours</c> o <c>stars</c>.</param>
/// <param name="Detail">Como se lee ("3 validados · 1 nuevo en el periodo").</param>
/// <param name="Breakdown">Desglose por categoria, cuando aplica.</param>
public sealed record Indicator(string Key, string Label, double? Value, string Unit, string Detail, IReadOnlyList<IndicatorBreakdown>? Breakdown);

/// <summary>Una parte del desglose.</summary>
/// <param name="Label">Categoria.</param>
/// <param name="Value">Cuantas.</param>
public sealed record IndicatorBreakdown(string Label, long Value);
