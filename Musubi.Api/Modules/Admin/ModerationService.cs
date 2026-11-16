using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Musubi.Api.Modules.Admin.Data;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Notifications;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Admin;

/// <summary>
/// Moderacion del campus (M-14): la bandeja de reportes (HU-39, RF-54) y suspender o reactivar cuentas (HU-40, RF-55).
/// Solo un administrador activo de su propio campus (RNF-03, R1) y cada accion queda en la bitacora (RNF-04).
/// </summary>
internal sealed partial class ModerationService(
    IAccountModeration accounts,
    IReportInbox reports,
    IProductModeration products,
    IModerationLogRepository log,
    INotificationCenter notifications,
    TimeProvider time,
    ILogger<ModerationService> logger)
{
    /// <summary>Reportes procedentes desde los que se sugiere suspender una cuenta (RN-27).</summary>
    public const int SuspensionThreshold = 3;

    /// <summary>Largo maximo de una nota o un motivo.</summary>
    public const int MaxReasonLength = 500;

    /// <summary>Reportes o cuentas por pagina.</summary>
    public const int PageSize = 50;

    /// <summary>Acciones del historial de una cuenta.</summary>
    public const int HistorySize = 20;

    /// <summary>Bandeja "abiertos".</summary>
    public const string OpenStatus = "open";

    /// <summary>Bandeja "revisados".</summary>
    public const string ClosedStatus = "closed";

    /// <summary>"Descartar".</summary>
    public const string DismissAction = "dismiss";

    /// <summary>"Procedente".</summary>
    public const string UpholdAction = "uphold";

    /// <summary>La bandeja de reportes del campus.</summary>
    /// <param name="adminId">Administrador (del token).</param>
    /// <param name="status"><c>open</c> o <c>closed</c>.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los reportes o el error.</returns>
    public async Task<Result<AdminReportsResponse>> ListReportsAsync(string adminId, string? status, CancellationToken cancellationToken)
    {
        var admin = await AdminAsync(adminId, cancellationToken).ConfigureAwait(false);
        if (admin is null)
        {
            return ApiErrors.Forbidden;
        }

        var open = (status ?? OpenStatus) switch
        {
            OpenStatus => true,
            ClosedStatus => false,
            _ => (bool?)null,
        };
        if (open is null)
        {
            return AdminErrors.InvalidStatus;
        }

        var found = await reports.ListAsync(admin.CommunityId, open.Value, PageSize, cancellationToken).ConfigureAwait(false);
        return Result<AdminReportsResponse>.Ok(new AdminReportsResponse(await ViewAsync(found, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>"Descartar" o "Procedente": cierra el reporte y, si es una publicacion procedente, la retira.</summary>
    /// <param name="adminId">Administrador (del token).</param>
    /// <param name="reportId">Reporte.</param>
    /// <param name="request">La accion y la nota.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El reporte revisado o el error.</returns>
    public async Task<Result<AdminReportResponse>> ResolveAsync(
        string adminId,
        string reportId,
        ResolveReportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var admin = await AdminAsync(adminId, cancellationToken).ConfigureAwait(false);
        if (admin is null)
        {
            return ApiErrors.Forbidden;
        }

        var status = request.Action switch
        {
            DismissAction => ReportStatuses.Dismissed,
            UpholdAction => ReportStatuses.Upheld,
            _ => null,
        };
        if (status is null)
        {
            return AdminErrors.InvalidAction;
        }

        var note = Clean(request.Note);
        if (note is { Length: > MaxReasonLength })
        {
            return AdminErrors.InvalidReason;
        }

        var report = await reports.FindAsync(reportId, cancellationToken).ConfigureAwait(false);
        if (report is null || !string.Equals(report.CommunityId, admin.CommunityId, StringComparison.Ordinal))
        {
            return AdminErrors.ReportNotFound;
        }

        if (report.Status != ReportStatuses.Open)
        {
            return AdminErrors.ReportClosed;
        }

        var product = report.TargetType == ReportTargetTypes.Product && report.TargetId is not null
            ? await products.FindAsync(report.TargetId, cancellationToken).ConfigureAwait(false)
            : null;
        var subjectId = report.SubjectId ?? (report.TargetType == ReportTargetTypes.User ? report.TargetId : product?.SellerId);
        var now = time.GetUtcNow().UtcDateTime;

        // Primero se cierra (atomico): con dos administradores a la vez, solo quien lo cierra retira la publicacion.
        var closed = await reports.CloseAsync(new ReportReview(report.Id, status, admin.Id, subjectId, note, now), cancellationToken).ConfigureAwait(false);
        if (closed is null)
        {
            return AdminErrors.ReportClosed;
        }

        await LogAsync(admin, status == ReportStatuses.Upheld ? ModerationActions.ReportUpheld : ModerationActions.ReportDismissed, closed, subjectId, note, now, cancellationToken).ConfigureAwait(false);
        if (status == ReportStatuses.Upheld && product is not null
            && await products.RemoveAsync(product.Id, cancellationToken).ConfigureAwait(false) is { } removed)
        {
            await LogAsync(admin, ModerationActions.ProductRemoved, closed, subjectId, note, now, cancellationToken).ConfigureAwait(false);
            await notifications.NotifyAsync(
                new Notice(
                    removed.SellerId,
                    NotificationTypes.ProductRemoved,
                    "Retiramos tu publicación",
                    "\"" + removed.Title + "\" no cumple las reglas de Musubi y ya no se muestra.",
                    NotificationTargets.MyProducts,
                    removed.Id),
                cancellationToken).ConfigureAwait(false);
        }

        await notifications.NotifyAsync(
            new Notice(
                closed.ReporterId,
                NotificationTypes.ReportReviewed,
                "Revisamos tu reporte",
                status == ReportStatuses.Upheld
                    ? "Gracias por avisarnos: tomamos medidas."
                    : "Gracias por avisarnos: no encontramos algo que retirar.",
                NotificationTargets.None,
                string.Empty),
            cancellationToken).ConfigureAwait(false);
        LogResolved(logger, closed.Id, admin.Id, status);
        var view = await ViewAsync([closed], cancellationToken).ConfigureAwait(false);
        return Result<AdminReportResponse>.Ok(view[0]);
    }

    /// <summary>Las cuentas suspendidas del campus.</summary>
    /// <param name="adminId">Administrador (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las cuentas o el error.</returns>
    public async Task<Result<AdminAccountsResponse>> ListSuspendedAsync(string adminId, CancellationToken cancellationToken)
    {
        var admin = await AdminAsync(adminId, cancellationToken).ConfigureAwait(false);
        if (admin is null)
        {
            return ApiErrors.Forbidden;
        }

        var found = await accounts.ListSuspendedAsync(admin.CommunityId, PageSize, cancellationToken).ConfigureAwait(false);
        return Result<AdminAccountsResponse>.Ok(new AdminAccountsResponse(
            [.. found.Select(a => new AdminAccountSummary(a.Id, a.FullName, a.Status, a.UpdatedAt))]));
    }

    /// <summary>Una cuenta con sus reportes procedentes y su historial.</summary>
    /// <param name="adminId">Administrador (del token).</param>
    /// <param name="userId">Cuenta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta o el error.</returns>
    public async Task<Result<AdminAccountResponse>> GetAccountAsync(string adminId, string userId, CancellationToken cancellationToken)
    {
        var admin = await AdminAsync(adminId, cancellationToken).ConfigureAwait(false);
        if (admin is null)
        {
            return ApiErrors.Forbidden;
        }

        var account = await InCampusAsync(admin, userId, cancellationToken).ConfigureAwait(false);
        return account is null
            ? AdminErrors.AccountNotFound
            : Result<AdminAccountResponse>.Ok(await AccountViewAsync(account, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>"Suspender cuenta": no vuelve a entrar y sus publicaciones se pausan.</summary>
    /// <param name="adminId">Administrador (del token).</param>
    /// <param name="userId">Cuenta.</param>
    /// <param name="request">El motivo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta suspendida o el error.</returns>
    public async Task<Result<AdminAccountResponse>> SuspendAsync(
        string adminId,
        string userId,
        SuspendAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var admin = await AdminAsync(adminId, cancellationToken).ConfigureAwait(false);
        if (admin is null)
        {
            return ApiErrors.Forbidden;
        }

        var reason = Clean(request.Reason);
        if (reason is null or { Length: > MaxReasonLength })
        {
            return AdminErrors.InvalidReason;
        }

        var account = await InCampusAsync(admin, userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return AdminErrors.AccountNotFound;
        }

        if (string.Equals(account.Id, admin.Id, StringComparison.Ordinal) || account.IsAdmin)
        {
            return AdminErrors.InvalidAccount;
        }

        if (!await accounts.SuspendAsync(account.Id, cancellationToken).ConfigureAwait(false))
        {
            return AdminErrors.AccountNotActive;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var hidden = await products.HideAllAsync(account.Id, cancellationToken).ConfigureAwait(false);
        await LogAccountAsync(admin, ModerationActions.AccountSuspended, account.Id, reason, now, cancellationToken).ConfigureAwait(false);
        LogSuspended(logger, account.Id, admin.Id, hidden);
        return await AccountResultAsync(account.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>"Reactivar cuenta": puede volver a entrar; sus publicaciones siguen pausadas hasta que las reactive.</summary>
    /// <param name="adminId">Administrador (del token).</param>
    /// <param name="userId">Cuenta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta reactivada o el error.</returns>
    public async Task<Result<AdminAccountResponse>> ReactivateAsync(string adminId, string userId, CancellationToken cancellationToken)
    {
        var admin = await AdminAsync(adminId, cancellationToken).ConfigureAwait(false);
        if (admin is null)
        {
            return ApiErrors.Forbidden;
        }

        var account = await InCampusAsync(admin, userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return AdminErrors.AccountNotFound;
        }

        if (!await accounts.ReactivateAsync(account.Id, cancellationToken).ConfigureAwait(false))
        {
            return AdminErrors.AccountNotSuspended;
        }

        await LogAccountAsync(admin, ModerationActions.AccountReactivated, account.Id, null, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        LogReactivated(logger, account.Id, admin.Id);
        return await AccountResultAsync(account.Id, cancellationToken).ConfigureAwait(false);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static AdminPerson? Person(IReadOnlyDictionary<string, ModeratedAccount> people, string? id) =>
        id is not null && people.TryGetValue(id, out var account) ? new AdminPerson(account.Id, account.FullName) : null;

    // El token dice admin, pero la base manda: una cuenta suspendida o sin el rol ya no modera (RNF-03).
    private async Task<ModeratedAccount?> AdminAsync(string adminId, CancellationToken cancellationToken)
    {
        var admin = await accounts.FindAsync(adminId, cancellationToken).ConfigureAwait(false);
        return admin is { IsAdmin: true, Status: UserStatuses.Active } ? admin : null;
    }

    // Otro campus es como si no existiera (R1, OWASP API1).
    private async Task<ModeratedAccount?> InCampusAsync(ModeratedAccount admin, string userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(userId, cancellationToken).ConfigureAwait(false);
        return account is not null && string.Equals(account.CommunityId, admin.CommunityId, StringComparison.Ordinal) ? account : null;
    }

    private async Task<Result<AdminAccountResponse>> AccountResultAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(userId, cancellationToken).ConfigureAwait(false);
        return account is null
            ? AdminErrors.AccountNotFound
            : Result<AdminAccountResponse>.Ok(await AccountViewAsync(account, cancellationToken).ConfigureAwait(false));
    }

    private async Task<AdminAccountResponse> AccountViewAsync(ModeratedAccount account, CancellationToken cancellationToken)
    {
        var upheld = await reports.CountUpheldAsync(account.Id, cancellationToken).ConfigureAwait(false);
        var history = await log.FindBySubjectAsync(account.Id, HistorySize, cancellationToken).ConfigureAwait(false);
        var actors = await accounts.FindManyAsync([.. history.Select(e => e.ActorId).Distinct(StringComparer.Ordinal)], cancellationToken).ConfigureAwait(false);
        return new AdminAccountResponse(
            account.Id,
            account.FullName,
            account.Status,
            account.IsAdmin,
            account.CreatedAt,
            upheld,
            upheld >= SuspensionThreshold && account.Status == UserStatuses.Active,
            [.. history.Select(e => new AdminLogEntry(e.Action, e.At, Person(actors, e.ActorId), e.Reason))]);
    }

    // Arma la bandeja con pocas consultas: las personas y las publicaciones van en lote.
    private async Task<IReadOnlyList<AdminReportResponse>> ViewAsync(IReadOnlyList<InboxReport> found, CancellationToken cancellationToken)
    {
        var productIds = found.Where(r => r.TargetType == ReportTargetTypes.Product && r.TargetId is not null)
            .Select(r => r.TargetId!).Distinct(StringComparer.Ordinal).ToList();
        var productsById = await products.FindManyAsync(productIds, cancellationToken).ConfigureAwait(false);

        string? SubjectOf(InboxReport r)
        {
            if (r.SubjectId is not null || r.TargetType == ReportTargetTypes.User)
            {
                return r.SubjectId ?? r.TargetId;
            }

            return r.TargetId is not null && productsById.TryGetValue(r.TargetId, out var p) ? p.SellerId : null;
        }

        var peopleIds = found.SelectMany(r => new[] { r.ReporterId, r.ReviewedBy, SubjectOf(r), r.TargetType == ReportTargetTypes.User ? r.TargetId : null })
            .OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var people = await accounts.FindManyAsync(peopleIds, cancellationToken).ConfigureAwait(false);

        var upheld = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var subjectId in found.Select(SubjectOf).OfType<string>().Distinct(StringComparer.Ordinal).Where(people.ContainsKey))
        {
            upheld[subjectId] = await reports.CountUpheldAsync(subjectId, cancellationToken).ConfigureAwait(false);
        }

        return [.. found.Select(r =>
        {
            var subjectId = SubjectOf(r);
            AdminSubject? subject = subjectId is not null && people.TryGetValue(subjectId, out var s)
                ? new AdminSubject(s.Id, s.FullName, s.Status, upheld[s.Id], upheld[s.Id] >= SuspensionThreshold && s.Status == UserStatuses.Active)
                : null;
            AdminReportTarget target = r.TargetType switch
            {
                ReportTargetTypes.Product when r.TargetId is not null && productsById.TryGetValue(r.TargetId, out var p) =>
                    new AdminReportTarget(r.TargetType, r.TargetId, p.Title, p.Photo, p.Status),
                ReportTargetTypes.User when r.TargetId is not null && people.TryGetValue(r.TargetId, out var u) =>
                    new AdminReportTarget(r.TargetType, r.TargetId, u.FullName, null, u.Status),
                _ => new AdminReportTarget(r.TargetType, r.TargetId, null, null, null),
            };
            var review = r.ReviewedAt is { } at ? new AdminReview(at, Person(people, r.ReviewedBy), r.ReviewNote) : null;
            return new AdminReportResponse(r.Id, r.Reason, r.Details, r.Status, r.CreatedAt, Person(people, r.ReporterId), target, subject, r.ConversationId, review);
        })];
    }

    private Task LogAsync(
        ModeratedAccount admin,
        string action,
        InboxReport report,
        string? subjectId,
        string? reason,
        DateTime at,
        CancellationToken cancellationToken) =>
        log.InsertAsync(
            new ModerationLogDocument
            {
                Id = MongoConventions.NewId(),
                CommunityId = admin.CommunityId,
                ActorId = admin.Id,
                Action = action,
                TargetType = report.TargetType,
                TargetId = report.TargetId,
                SubjectId = subjectId,
                ReportId = report.Id,
                Reason = reason,
                At = at,
            },
            cancellationToken);

    private Task LogAccountAsync(ModeratedAccount admin, string action, string userId, string? reason, DateTime at, CancellationToken cancellationToken) =>
        log.InsertAsync(
            new ModerationLogDocument
            {
                Id = MongoConventions.NewId(),
                CommunityId = admin.CommunityId,
                ActorId = admin.Id,
                Action = action,
                TargetType = ReportTargetTypes.User,
                TargetId = userId,
                SubjectId = userId,
                Reason = reason,
                At = at,
            },
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reporte {ReportId} revisado por {AdminId}: {Status}")]
    private static partial void LogResolved(ILogger logger, string reportId, string adminId, string status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cuenta {UserId} suspendida por {AdminId} ({Hidden} publicaciones pausadas)")]
    private static partial void LogSuspended(ILogger logger, string userId, string adminId, long hidden);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cuenta {UserId} reactivada por {AdminId}")]
    private static partial void LogReactivated(ILogger logger, string userId, string adminId);
}
