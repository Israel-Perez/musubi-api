using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.Chat.Data;

// Documentos de persistencia de M-07 (HU-20, HU-22). La validacion del servidor vive en los scripts
// 005-chat y 006-chat-bandeja de db/;
// fila canonica en docs/modelo-datos.md.

/// <summary>Conversacion entre un comprador y un vendedor sobre un elemento (RN-21).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class ConversationDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Quien inicio la conversacion para comprar.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string BuyerId { get; init; } = string.Empty;

    /// <summary>Dueno de la publicacion.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string SellerId { get; init; } = string.Empty;

    /// <summary>Los dos participantes (comprador y vendedor): bandeja (HU-22) y borrado (HU-46).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public IReadOnlyList<string> Participants { get; init; } = [];

    /// <summary>Copia del nombre del comprador (R8: el nombre no se edita). Bandeja sin consultar usuarios (HU-22).</summary>
    public string? BuyerName { get; init; }

    /// <summary>Copia del nombre del vendedor (R8).</summary>
    public string? SellerName { get; init; }

    /// <summary>Lo que origino la conversacion.</summary>
    public ConversationContext Context { get; init; } = new();

    /// <summary>Mensajes que el comprador no ha leido (HU-22).</summary>
    public int BuyerUnread { get; init; }

    /// <summary>Mensajes que el vendedor no ha leido (HU-22).</summary>
    public int SellerUnread { get; init; }

    /// <summary>Copia del ultimo mensaje para la bandeja (R8); <c>null</c> mientras no haya mensajes.</summary>
    public LastMessage? LastMessage { get; init; }

    /// <summary>Ultima actividad (UTC): al crearla y con cada mensaje. Ordena la bandeja (HU-22).</summary>
    public DateTime LastMessageAt { get; init; }

    /// <summary>Creacion (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Elemento de contexto (RN-21). En HU-20 solo publicaciones; solicitudes y ofertas llegan en S3.</summary>
/// <remarks>
/// <c>[BsonNoId]</c>: sin el, el driver toma <see cref="Id"/> como id del subdocumento y lo guarda como
/// <c>_id</c>, y la validacion de db/005-chat.js (que pide <c>context.id</c>) rechaza la conversacion.
/// </remarks>
[BsonNoId]
internal sealed class ConversationContext
{
    /// <summary>Ver <see cref="ConversationContextTypes"/>.</summary>
    public string Type { get; init; } = ConversationContextTypes.Product;

    /// <summary>Id del elemento.</summary>
    [BsonElement("id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Copia del nombre (R8): se sigue viendo aunque la publicacion se quite.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Copia de la ruta de la portada (<c>/media/...</c>, R8) para la bandeja (HU-22).</summary>
    public string? Photo { get; init; }
}

/// <summary>Copia del ultimo mensaje.</summary>
internal sealed class LastMessage
{
    /// <summary>Quien lo envio.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string SenderId { get; init; } = string.Empty;

    /// <summary>Inicio del texto (hasta <see cref="ChatService.PreviewLength"/> caracteres).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Envio (UTC).</summary>
    public DateTime At { get; init; }
}

/// <summary>Mensaje de texto de una conversacion.</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class MessageDocument
{
    /// <summary>Id (ObjectId: ordenado por tiempo, sirve de cursor; R2).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Conversacion.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ConversationId { get; init; } = string.Empty;

    /// <summary>Comunidad (R1): prefijo de la clave de sharding prevista.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Quien lo envio (uno de los participantes).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string SenderId { get; init; } = string.Empty;

    /// <summary>Texto moderado (1 a <see cref="ChatService.MaxMessageLength"/> caracteres).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Envio (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Tipos de contexto de una conversacion (RN-21).</summary>
internal static class ConversationContextTypes
{
    /// <summary>Una publicacion (HU-20, Sprint 2).</summary>
    public const string Product = "product";

    /// <summary>
    /// Una oferta "Yo tengo este producto" (HU-17): la abre quien busca (comprador) con quien ofrece (vendedor).
    /// </summary>
    public const string Offer = "offer";
}
