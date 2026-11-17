using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Reputation.Data;

namespace Musubi.Api.Modules.Reputation;

/// <summary>Un reporte, como lo revisa un administrador (HU-39).</summary>
/// <param name="Id">Reporte.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="ReporterId">Quien reporto.</param>
/// <param name="TargetType"><c>general</c>, <c>user</c> o <c>product</c>.</param>
/// <param name="TargetId">La persona o la publicacion; <c>null</c> en un problema general.</param>
/// <param name="SubjectId">La persona responsable, si se sabe.</param>
/// <param name="ConversationId">Conversacion donde paso, si se reporto desde el chat.</param>
/// <param name="Reason">Motivo de la maqueta.</param>
/// <param name="Details">"Cuentanos que paso".</param>
/// <param name="Status"><c>open</c>, <c>dismissed</c> o <c>upheld</c>.</param>
/// <param name="CreatedAt">Cuando llego (UTC).</param>
/// <param name="ReviewedBy">Administrador que lo reviso.</param>
/// <param name="ReviewedAt">Cuando se reviso (UTC).</param>
/// <param name="ReviewNote">Nota interna.</param>
internal sealed record InboxReport(
    string Id,
    string CommunityId,
    string ReporterId,
    string TargetType,
    string? TargetId,
    string? SubjectId,
    string? ConversationId,
    string Reason,
    string? Details,
    string Status,
    DateTime CreatedAt,
    string? ReviewedBy,
    DateTime? ReviewedAt,
    string? ReviewNote);

/// <summary>
/// Contrato publico de M-13 para M-14 (Modules/README: un modulo no lee las colecciones de otro). La bandeja de
/// reportes del administrador (HU-39) y los procedentes de cada cuenta (RN-27, HU-40).
/// </summary>
internal interface IReportInbox
{
    /// <summary>Los reportes de un campus, abiertos o revisados, del mas nuevo al mas viejo.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="open"><c>true</c>: los abiertos; <c>false</c>: los revisados.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los reportes.</returns>
    Task<IReadOnlyList<InboxReport>> ListAsync(string communityId, bool open, int limit, CancellationToken cancellationToken);

    /// <summary>Un reporte.</summary>
    /// <param name="reportId">Reporte.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El reporte o <c>null</c>.</returns>
    Task<InboxReport?> FindAsync(string reportId, CancellationToken cancellationToken);

    /// <summary>Cierra un reporte abierto (atomico: con dos administradores a la vez, solo uno lo cierra).</summary>
    /// <param name="review">La revision.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El reporte cerrado, o <c>null</c> si no existe o ya estaba cerrado.</returns>
    Task<InboxReport?> CloseAsync(ReportReview review, CancellationToken cancellationToken);

    /// <summary>Reportes procedentes contra una persona (RN-27).</summary>
    /// <param name="userId">La persona.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos.</returns>
    Task<long> CountUpheldAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de reportes.</summary>
internal sealed class ReportInbox(IReportRepository reports) : IReportInbox
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxReport>> ListAsync(string communityId, bool open, int limit, CancellationToken cancellationToken)
    {
        string[] statuses = open ? [ReportStatuses.Open] : ReportStatuses.Closed;
        var found = await reports.FindByStatusAsync(communityId, statuses, limit, cancellationToken).ConfigureAwait(false);
        return [.. found.Select(ToInbox)];
    }

    /// <inheritdoc />
    public async Task<InboxReport?> FindAsync(string reportId, CancellationToken cancellationToken)
    {
        var report = await reports.FindByIdAsync(reportId, cancellationToken).ConfigureAwait(false);
        return report is null ? null : ToInbox(report);
    }

    /// <inheritdoc />
    public async Task<InboxReport?> CloseAsync(ReportReview review, CancellationToken cancellationToken)
    {
        var closed = await reports.CloseAsync(review, cancellationToken).ConfigureAwait(false);
        return closed is null ? null : ToInbox(closed);
    }

    /// <inheritdoc />
    public Task<long> CountUpheldAsync(string userId, CancellationToken cancellationToken) =>
        reports.CountUpheldAsync(userId, cancellationToken);

    private static InboxReport ToInbox(ReportDocument report) => new(
        report.Id,
        report.CommunityId,
        report.ReporterId,
        report.Target.Type,
        report.Target.Id,
        report.SubjectId,
        report.ConversationId,
        report.Reason,
        report.Details,
        report.Status,
        report.CreatedAt,
        report.ReviewedBy,
        report.ReviewedAt,
        report.ReviewNote);
}
