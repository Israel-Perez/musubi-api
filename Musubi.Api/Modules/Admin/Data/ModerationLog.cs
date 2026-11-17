using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Admin.Data;

// Persistencia de M-14 (HU-39/40/41). Validacion del servidor en db/030-moderacion.js y db/031-categorias.js; fila en docs/modelo-datos.md.

/// <summary>
/// Una accion de un administrador (RNF-04): descartar o confirmar un reporte, retirar una publicacion, suspender o
/// reactivar una cuenta y agregar o cambiar una categoria (HU-41). No se edita ni se borra (tampoco al borrar una cuenta: es la evidencia de la moderacion).
/// </summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class ModerationLogDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Campus (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Administrador que actuo.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string ActorId { get; init; } = string.Empty;

    /// <summary>Que hizo (<see cref="ModerationActions"/>).</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Sobre que: <c>general</c>, <c>user</c>, <c>product</c> o <c>category</c>.</summary>
    public string TargetType { get; init; } = string.Empty;

    /// <summary>La persona, la publicacion o la categoria; se omite en un problema general.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? TargetId { get; init; }

    /// <summary>La persona responsable (para el historial de su cuenta); se omite si no hay.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? SubjectId { get; init; }

    /// <summary>El reporte que lo origino, si hubo.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    [BsonIgnoreIfNull]
    public string? ReportId { get; init; }

    /// <summary>Motivo o nota del administrador (hasta 500 caracteres).</summary>
    [BsonIgnoreIfNull]
    public string? Reason { get; init; }

    /// <summary>Cuando (UTC).</summary>
    public DateTime At { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Acciones de la bitacora.</summary>
internal static class ModerationActions
{
    /// <summary>Reporte descartado.</summary>
    public const string ReportDismissed = "report_dismissed";

    /// <summary>Reporte procedente.</summary>
    public const string ReportUpheld = "report_upheld";

    /// <summary>Publicacion retirada por un reporte procedente.</summary>
    public const string ProductRemoved = "product_removed";

    /// <summary>Cuenta suspendida (HU-40).</summary>
    public const string AccountSuspended = "account_suspended";

    /// <summary>Cuenta reactivada (HU-40).</summary>
    public const string AccountReactivated = "account_reactivated";

    /// <summary>Categoria agregada (HU-41); el motivo guarda su nombre.</summary>
    public const string CategoryCreated = "category_created";

    /// <summary>Categoria renombrada (HU-41); el motivo guarda el nombre anterior.</summary>
    public const string CategoryRenamed = "category_renamed";

    /// <summary>Categoria desactivada (HU-41).</summary>
    public const string CategoryDeactivated = "category_deactivated";

    /// <summary>Categoria activada de nuevo (HU-41).</summary>
    public const string CategoryReactivated = "category_reactivated";
}

/// <summary>Sobre que actuo un administrador cuando no es un reporte (los de un reporte usan <c>ReportTargetTypes</c>).</summary>
internal static class ModerationTargets
{
    /// <summary>Una categoria (HU-41).</summary>
    public const string Category = "category";
}

/// <summary>Bitacora de moderacion. Mongo* e InMemory* pasan la misma bateria (TestShared/Admin).</summary>
internal interface IModerationLogRepository
{
    /// <summary>Agrega una accion.</summary>
    /// <param name="entry">La accion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task InsertAsync(ModerationLogDocument entry, CancellationToken cancellationToken);

    /// <summary>Lo que se hizo con una persona o sus publicaciones, de lo mas nuevo a lo mas viejo.</summary>
    /// <param name="subjectId">La persona.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las acciones.</returns>
    Task<IReadOnlyList<ModerationLogDocument>> FindBySubjectAsync(string subjectId, int limit, CancellationToken cancellationToken);
}

/// <summary>Bitacora en memoria.</summary>
internal sealed class InMemoryModerationLogRepository : IModerationLogRepository
{
    private readonly Lock gate = new();
    private readonly List<ModerationLogDocument> entries = [];

    /// <inheritdoc />
    public Task InsertAsync(ModerationLogDocument entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (gate)
        {
            entries.Add(entry);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ModerationLogDocument>> FindBySubjectAsync(string subjectId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<ModerationLogDocument>>(entries
                .Where(e => string.Equals(e.SubjectId, subjectId, StringComparison.Ordinal))
                .OrderByDescending(e => e.At)
                .ThenByDescending(e => e.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList());
        }
    }
}

/// <summary>Bitacora en MongoDB (indices <c>ix_subject_at</c> e <c>ix_actor_at</c>).</summary>
internal sealed class MongoModerationLogRepository(IMongoDatabase database) : IModerationLogRepository
{
    private readonly IMongoCollection<ModerationLogDocument> entries = database.GetCollection<ModerationLogDocument>(MongoSchema.ModerationLog);

    /// <inheritdoc />
    public Task InsertAsync(ModerationLogDocument entry, CancellationToken cancellationToken) =>
        entries.InsertOneAsync(entry, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModerationLogDocument>> FindBySubjectAsync(string subjectId, int limit, CancellationToken cancellationToken) =>
        ObjectId.TryParse(subjectId, out _)
            ? await entries.Find(e => e.SubjectId == subjectId)
                .SortByDescending(e => e.At)
                .ThenByDescending(e => e.Id)
                .Limit(limit)
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];
}
