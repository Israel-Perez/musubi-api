using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Reputation;

/// <summary>Errores de negocio de M-13 (catalogo canonico: docs/errores.md).</summary>
internal static class ReputationErrors
{
    /// <summary>Motivo que no es de la lista de la maqueta.</summary>
    public static readonly ApiError InvalidReason = new(
        "reports.invalid_reason", StatusCodes.Status400BadRequest, "Elige el motivo del reporte.");

    /// <summary>"Cuentanos que paso" de mas de 1000 caracteres.</summary>
    public static readonly ApiError InvalidDetails = new(
        "reports.invalid_details", StatusCodes.Status400BadRequest, "Cuéntanos qué pasó en hasta 1000 caracteres.");

    /// <summary>Tipo desconocido, id donde no va o reportarse a uno mismo.</summary>
    public static readonly ApiError InvalidTarget = new(
        "reports.invalid_target", StatusCodes.Status400BadRequest, "No pudimos enviar este reporte. Revisa a quién o qué reportas.");

    /// <summary>
    /// La persona, la publicacion o la conversacion no existe, es de otro campus o quien reporta no participa:
    /// la misma respuesta en todos los casos, sin revelar que existe (OWASP API1).
    /// </summary>
    public static readonly ApiError TargetNotFound = new(
        "reports.target_not_found", StatusCodes.Status404NotFound, "No encontramos lo que quieres reportar.");

    /// <summary>Intento de bloquearse a uno mismo.</summary>
    public static readonly ApiError BlockSelf = new(
        "blocks.self", StatusCodes.Status400BadRequest, "No puedes bloquearte a ti mismo.");

    /// <summary>La persona no existe o es de otro campus (no se revela cual).</summary>
    public static readonly ApiError UserNotFound = new(
        "blocks.user_not_found", StatusCodes.Status404NotFound, "No encontramos a esta persona.");

    /// <summary>Estrellas fuera de 1 a 5 (RN-25).</summary>
    public static readonly ApiError InvalidStars = new(
        "ratings.invalid_stars", StatusCodes.Status400BadRequest, "Elige de 1 a 5 estrellas.");

    /// <summary>"¿Que destacas?" con algo que no es de la lista de esa parte.</summary>
    public static readonly ApiError InvalidTags = new(
        "ratings.invalid_tags", StatusCodes.Status400BadRequest, "Elige solo lo que aparece en la lista.");

    /// <summary>Comentario de mas de 280 caracteres.</summary>
    public static readonly ApiError InvalidComment = new(
        "ratings.invalid_comment", StatusCodes.Status400BadRequest, "El comentario puede tener hasta 280 caracteres.");

    /// <summary>El pedido aun no esta completado (RN-24).</summary>
    public static readonly ApiError NotCompleted = new(
        "ratings.not_completed", StatusCodes.Status409Conflict, "Podrás calificar cuando los dos confirmen la entrega.");

    /// <summary>Pasaron los 7 dias para calificar.</summary>
    public static readonly ApiError Expired = new(
        "ratings.expired", StatusCodes.Status409Conflict, "Ya pasaron 7 días desde la entrega: este pedido ya no se puede calificar.");

    /// <summary>Ya calificaste este pedido (una vez por pedido, RN-24).</summary>
    public static readonly ApiError AlreadyRated = new(
        "ratings.already_rated", StatusCodes.Status409Conflict, "Ya calificaste este pedido.");

    /// <summary>Cursor de las opiniones que no es de la lista.</summary>
    public static readonly ApiError InvalidCursor = new(
        "ratings.invalid_cursor", StatusCodes.Status400BadRequest, "No pudimos cargar más opiniones. Actualiza la lista.");
}
