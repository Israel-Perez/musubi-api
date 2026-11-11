using System;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.Notifications.Data;

// Documentos de persistencia de M-09 (HU-31). La validacion del servidor vive en db/017-avisos.js; fila canonica en
// docs/modelo-datos.md.

/// <summary>Un aviso del centro de notificaciones (maqueta <c>Notificaciones</c>, RF-43).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class NotificationDocument
{
    /// <summary>Id (ObjectId: ordenado por tiempo, sirve de cursor).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>A quien va.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; init; } = string.Empty;

    /// <summary>Ver <see cref="NotificationTypes"/>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Pestana de la maqueta: ver <see cref="NotificationCategories"/>.</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Titulo ("Tu solicitud vence manana").</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Detalle.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>Lo que abre al tocarlo.</summary>
    public NotificationTarget Target { get; init; } = new();

    /// <summary>
    /// Evita repetir el mismo aviso (<c>ux_user_dedupe</c>): por ejemplo, "vence manana" una sola vez por solicitud
    /// aunque las dos instancias revisen a la vez. <c>null</c> en los demas.
    /// </summary>
    public string? DedupeKey { get; init; }

    /// <summary>Cuando lo leyo (UTC), o <c>null</c>.</summary>
    public DateTime? ReadAt { get; init; }

    /// <summary>Creacion (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Cuando se borra solo (60 dias; indice TTL <c>ttl_expires_at</c>).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Lo que abre un aviso.</summary>
[BsonNoId]
internal sealed class NotificationTarget
{
    /// <summary><c>conversation</c>, <c>order</c> o <c>wanted</c>.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Id del elemento.</summary>
    [BsonElement("id")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;
}

/// <summary>El token de FCM de un dispositivo con sesion.</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class DeviceTokenDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Quien tiene la sesion en ese dispositivo.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; init; } = string.Empty;

    /// <summary>El token (unico: si otra cuenta inicia sesion en el mismo telefono, pasa a esa cuenta).</summary>
    public string Token { get; init; } = string.Empty;

    /// <summary>Alta (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Ultima vez que la app lo registro (UTC).</summary>
    public DateTime LastSeenAt { get; init; }

    /// <summary>Se borra solo si la app no lo vuelve a registrar en 60 dias (TTL).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}
