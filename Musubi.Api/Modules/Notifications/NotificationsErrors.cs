using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Notifications;

/// <summary>Errores de los avisos (HU-31; catalogo canonico: docs/errores.md).</summary>
internal static class NotificationsErrors
{
    /// <summary>Token vacio, con espacios o de mas de 512 caracteres.</summary>
    public static readonly ApiError InvalidToken = new(
        "notifications.invalid_token", StatusCodes.Status400BadRequest, "No pudimos activar los avisos en este dispositivo.");

    /// <summary>Pestana que no existe.</summary>
    public static readonly ApiError InvalidCategory = new(
        "notifications.invalid_category", StatusCodes.Status400BadRequest, "Elige una pestaña de avisos.");

    /// <summary>Cursor que no es un id.</summary>
    public static readonly ApiError InvalidCursor = new(
        "notifications.invalid_cursor", StatusCodes.Status400BadRequest, "No pudimos cargar más avisos. Vuelve a abrir la pantalla.");

    /// <summary>El aviso no existe o no es de quien consulta.</summary>
    public static readonly ApiError NotFound = new(
        "notifications.not_found", StatusCodes.Status404NotFound, "No encontramos este aviso.");
}
