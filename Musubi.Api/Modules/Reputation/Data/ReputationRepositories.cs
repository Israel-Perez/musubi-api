using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Musubi.Api.Modules.Reputation.Data;

// Contratos de persistencia de M-13 (HU-28, HU-57). Mongo* e InMemory* pasan la misma bateria de pruebas
// (TestShared/Reputation).

/// <summary>Reportes (HU-28).</summary>
internal interface IReportRepository
{
    /// <summary>Guarda un reporte nuevo.</summary>
    /// <param name="report">El reporte.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task InsertAsync(ReportDocument report, CancellationToken cancellationToken);

    /// <summary>Reportes que hizo una persona, del mas nuevo al mas viejo.</summary>
    /// <param name="reporterId">Quien reporto.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los reportes.</returns>
    Task<IReadOnlyList<ReportDocument>> FindByReporterAsync(string reporterId, CancellationToken cancellationToken);

    /// <summary>Borra los reportes que hizo una persona (HU-46, R11).</summary>
    /// <param name="reporterId">Quien reporto.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se borraron.</returns>
    Task<long> DeleteByReporterAsync(string reporterId, CancellationToken cancellationToken);

    /// <summary>Un reporte por id.</summary>
    /// <param name="id">Id del reporte.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El reporte o <c>null</c>.</returns>
    Task<ReportDocument?> FindByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>La bandeja de un campus (HU-39): los de esos estados, del mas nuevo al mas viejo.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="statuses">Estados.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los reportes.</returns>
    Task<IReadOnlyList<ReportDocument>> FindByStatusAsync(string communityId, IReadOnlyCollection<string> statuses, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Cierra un reporte abierto (HU-39). Atomico: con dos administradores a la vez, solo uno lo cierra.
    /// </summary>
    /// <param name="review">La revision.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El reporte cerrado, o <c>null</c> si no existe o ya estaba cerrado.</returns>
    Task<ReportDocument?> CloseAsync(ReportReview review, CancellationToken cancellationToken);

    /// <summary>Reportes procedentes contra una persona (RN-27, HU-40).</summary>
    /// <param name="subjectId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos.</returns>
    Task<long> CountUpheldAsync(string subjectId, CancellationToken cancellationToken);
}

/// <summary>La revision de un reporte (HU-39).</summary>
/// <param name="ReportId">Reporte.</param>
/// <param name="Status"><see cref="ReportStatuses.Dismissed"/> o <see cref="ReportStatuses.Upheld"/>.</param>
/// <param name="ReviewerId">Administrador.</param>
/// <param name="SubjectId">Persona responsable, o <c>null</c> en un problema general.</param>
/// <param name="Note">Nota interna, o <c>null</c>.</param>
/// <param name="At">Momento (UTC).</param>
internal sealed record ReportReview(string ReportId, string Status, string ReviewerId, string? SubjectId, string? Note, DateTime At);

/// <summary>Bloqueos (HU-57). Uno por pareja quien bloquea / bloqueado.</summary>
internal interface IBlockRepository
{
    /// <summary>Guarda el bloqueo si no existia (idempotente: dos toques no duplican).</summary>
    /// <param name="block">El bloqueo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si se creo.</returns>
    Task<bool> InsertIfAbsentAsync(BlockDocument block, CancellationToken cancellationToken);

    /// <summary>Quita el bloqueo (idempotente).</summary>
    /// <param name="blockerId">Quien bloqueo.</param>
    /// <param name="blockedId">A quien.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si existia.</returns>
    Task<bool> DeleteAsync(string blockerId, string blockedId, CancellationToken cancellationToken);

    /// <summary>Las personas que bloqueo alguien, de la mas reciente a la mas vieja.</summary>
    /// <param name="blockerId">Quien bloqueo.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los bloqueos.</returns>
    Task<IReadOnlyList<BlockDocument>> FindByBlockerAsync(string blockerId, int limit, CancellationToken cancellationToken);

    /// <summary>Ids de las personas con un bloqueo con alguien, en cualquier direccion.</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los ids, sin repetir.</returns>
    Task<IReadOnlyCollection<string>> FindRelatedAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Indica si hay un bloqueo entre dos personas, en cualquier direccion.</summary>
    /// <param name="userId">Una persona.</param>
    /// <param name="otherId">La otra.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si alguno bloqueo al otro.</returns>
    Task<bool> ExistsBetweenAsync(string userId, string otherId, CancellationToken cancellationToken);

    /// <summary>Borra los bloqueos de una persona en las dos direcciones (HU-46, R11).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se borraron.</returns>
    Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken);
}
