using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Chat;

/// <summary>Errores de negocio de M-07 (catalogo canonico: docs/errores.md).</summary>
internal static class ChatErrors
{
    /// <summary>La publicacion no existe, es de otro campus, esta pausada o se quito.</summary>
    public static readonly ApiError ProductUnavailable = new(
        "chat.product_unavailable", StatusCodes.Status404NotFound, "Esta publicación ya no está disponible.");

    /// <summary>El vendedor intenta escribirse sobre su propia publicacion.</summary>
    public static readonly ApiError OwnProduct = new(
        "chat.own_product", StatusCodes.Status400BadRequest, "Es tu publicación: aquí te escribirán los compradores.");

    /// <summary>
    /// La conversacion no existe o quien pregunta no participa en ella (RN-22). Es la misma respuesta en
    /// los dos casos: no se revela que existe (OWASP API1).
    /// </summary>
    public static readonly ApiError ConversationNotFound = new(
        "chat.conversation_not_found", StatusCodes.Status404NotFound, "No encontramos esta conversación.");

    /// <summary>Mensaje vacio o de mas de 1000 caracteres.</summary>
    public static readonly ApiError InvalidMessage = new(
        "chat.invalid_message", StatusCodes.Status400BadRequest, "Escribe un mensaje de hasta 1000 caracteres.");

    /// <summary>Cursor de paginacion que no se emitio aqui.</summary>
    public static readonly ApiError InvalidCursor = new(
        "chat.invalid_cursor", StatusCodes.Status400BadRequest, "No pudimos cargar mensajes anteriores. Vuelve a abrir la conversación.");
}
