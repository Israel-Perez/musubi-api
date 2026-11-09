using System;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.Reputation.Data;

// Documentos de persistencia de M-13 (HU-28 reportes, HU-57 bloqueos). La validacion del servidor vive en
// db/008-reportes-bloqueos.js; fila canonica en docs/modelo-datos.md.

/// <summary>Reporte de una persona, una publicacion o un problema general (HU-28). Lo revisa el equipo (HU-39).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class ReportDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Quien reporta.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ReporterId { get; init; } = string.Empty;

    /// <summary>Lo reportado.</summary>
    public ReportTarget Target { get; init; } = new();

    /// <summary>Conversacion donde paso, si se reporta desde el chat (solo con <c>target.type = user</c>).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? ConversationId { get; init; }

    /// <summary>Motivo (<see cref="ReportReasons"/>).</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>"Cuentanos que paso" (hasta 1000 caracteres); se omite si viene vacio.</summary>
    [BsonIgnoreIfNull]
    public string? Details { get; init; }

    /// <summary><c>open</c> al crearse; HU-39 lo cierra.</summary>
    public string Status { get; init; } = ReportStatuses.Open;

    /// <summary>Creacion (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Lo reportado: tipo y, salvo un problema general, su id.</summary>
/// <remarks><c>[BsonNoId]</c>: sin el, el driver guardaria <see cref="Id"/> como <c>_id</c> del subdocumento.</remarks>
[BsonNoId]
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class ReportTarget
{
    /// <summary>Tipo (<see cref="ReportTargetTypes"/>).</summary>
    public string Type { get; init; } = ReportTargetTypes.General;

    /// <summary>Id del usuario o de la publicacion; se omite en un problema general.</summary>
    [BsonElement("id")]
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? Id { get; init; }
}

/// <summary>Bloqueo de una persona por otra (HU-57). Uno por pareja (indice <c>ux_blocker_blocked</c>).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class BlockDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Quien bloquea.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string BlockerId { get; init; } = string.Empty;

    /// <summary>A quien bloquea.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string BlockedId { get; init; } = string.Empty;

    /// <summary>Copia del nombre de la persona bloqueada (R8: el nombre no se edita), para la lista de Ajustes.</summary>
    public string BlockedName { get; init; } = string.Empty;

    /// <summary>Creacion (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Tipos de lo reportado.</summary>
internal static class ReportTargetTypes
{
    /// <summary>"Reportar un problema" desde Ajustes, sin algo concreto.</summary>
    public const string General = "general";

    /// <summary>Una persona (desde el chat).</summary>
    public const string User = "user";

    /// <summary>Una publicacion.</summary>
    public const string Product = "product";
}

/// <summary>Motivos de la maqueta (<c>REPORT_REASONS</c>), en su orden.</summary>
internal static class ReportReasons
{
    /// <summary>"El pago no llego o no coincide".</summary>
    public const string Payment = "payment";

    /// <summary>"El producto no es como se describio".</summary>
    public const string NotAsDescribed = "not_as_described";

    /// <summary>"La otra persona no llego a la entrega".</summary>
    public const string NoShow = "no_show";

    /// <summary>"Me pidio pagar fuera de Musubi".</summary>
    public const string OffPlatform = "off_platform";

    /// <summary>"Otro problema".</summary>
    public const string Other = "other";

    /// <summary>Todos, en el orden de la maqueta.</summary>
    public static readonly string[] All = [Payment, NotAsDescribed, NoShow, OffPlatform, Other];
}

/// <summary>Estados de un reporte.</summary>
internal static class ReportStatuses
{
    /// <summary>Recien enviado; lo revisa el equipo (HU-39).</summary>
    public const string Open = "open";
}
