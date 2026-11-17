using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Admin;

/// <summary>Errores de negocio de M-14 (catalogo canonico: docs/errores.md). Sin el rol: <c>auth.forbidden</c>.</summary>
internal static class AdminErrors
{
    /// <summary>Bandeja con un estado que no existe.</summary>
    public static readonly ApiError InvalidStatus = new(
        "admin.invalid_status", StatusCodes.Status400BadRequest, "Elige reportes abiertos o revisados.");

    /// <summary>Reporte inexistente o de otro campus (no revela cual, OWASP API1).</summary>
    public static readonly ApiError ReportNotFound = new(
        "admin.report_not_found", StatusCodes.Status404NotFound, "No encontramos ese reporte.");

    /// <summary>Reporte ya revisado (tambien si otro administrador lo cerro al mismo tiempo).</summary>
    public static readonly ApiError ReportClosed = new(
        "admin.report_closed", StatusCodes.Status409Conflict, "Este reporte ya se revisó.");

    /// <summary>Accion que no es descartar ni procedente.</summary>
    public static readonly ApiError InvalidAction = new(
        "admin.invalid_action", StatusCodes.Status400BadRequest, "Elige qué hacer con el reporte.");

    /// <summary>Nota o motivo vacio o de mas de 500 caracteres.</summary>
    public static readonly ApiError InvalidReason = new(
        "admin.invalid_reason", StatusCodes.Status400BadRequest, "Escribe el motivo en hasta 500 caracteres.");

    /// <summary>Cuenta inexistente o de otro campus.</summary>
    public static readonly ApiError AccountNotFound = new(
        "admin.account_not_found", StatusCodes.Status404NotFound, "No encontramos esa cuenta.");

    /// <summary>La cuenta propia o la de otro administrador.</summary>
    public static readonly ApiError InvalidAccount = new(
        "admin.invalid_account", StatusCodes.Status400BadRequest, "No puedes suspender tu cuenta ni la de otra persona administradora.");

    /// <summary>Suspender una cuenta que no esta activa.</summary>
    public static readonly ApiError AccountNotActive = new(
        "admin.account_not_active", StatusCodes.Status409Conflict, "Esta cuenta no está activa.");

    /// <summary>Reactivar una cuenta que no esta suspendida.</summary>
    public static readonly ApiError AccountNotSuspended = new(
        "admin.account_not_suspended", StatusCodes.Status409Conflict, "Esta cuenta no está suspendida.");
}
