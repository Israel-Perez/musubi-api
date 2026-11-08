using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Chat;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Email;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Reputation;

/// <summary>Configuracion de los reportes (seccion <c>Reports</c>).</summary>
internal sealed class ReportOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Reports";

    /// <summary>
    /// Correo del equipo que revisa los reportes (Parameter Store <c>/musubi/prod/Reports/NotifyEmail</c>). Vacio:
    /// no se avisa por correo y el reporte solo queda guardado y en el log.
    /// </summary>
    public string NotifyEmail { get; set; } = string.Empty;
}

/// <summary>
/// Reportar a una persona, una publicacion o un problema general (HU-28, RF-37, RN-23). El reporte se guarda para
/// la revision del equipo (HU-39) y, si hay correo configurado, se le avisa en el momento ("Te escribimos en menos
/// de 24 h"). El texto no se modera (02-07): no se publica y una cita del abuso es la evidencia que se necesita.
/// </summary>
internal sealed partial class ReportService(
    IReportRepository reports,
    IMemberDirectory members,
    IProductDirectory products,
    IConversationDirectory conversations,
    IEmailSender email,
    IOptions<ReportOptions> options,
    ILogger<ReportService> logger,
    TimeProvider time)
{
    /// <summary>Largo maximo de "Cuentanos que paso".</summary>
    public const int MaxDetailsLength = 1000;

    /// <summary>Guarda el reporte y avisa al equipo.</summary>
    /// <param name="userId">Quien reporta (del token).</param>
    /// <param name="request">Lo reportado, el motivo y el detalle.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El reporte recibido o el error.</returns>
    public async Task<Result<ReportResponse>> CreateAsync(string userId, CreateReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var me = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (me is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        if (!ReportReasons.All.Contains(request.Reason, StringComparer.Ordinal))
        {
            return ReputationErrors.InvalidReason;
        }

        var details = string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim();
        if (details is { Length: > MaxDetailsLength })
        {
            return ReputationErrors.InvalidDetails;
        }

        var target = await ValidateTargetAsync(me, request, cancellationToken).ConfigureAwait(false);
        if (target.Error is { } error)
        {
            return error;
        }

        var report = new ReportDocument
        {
            Id = MongoConventions.NewId(),
            CommunityId = me.CommunityId,
            ReporterId = me.Id,
            Target = target.Value!,
            ConversationId = string.IsNullOrEmpty(request.ConversationId) ? null : request.ConversationId,
            Reason = request.Reason,
            Details = details,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        await reports.InsertAsync(report, cancellationToken).ConfigureAwait(false);
        // Sin el texto ni quien reporta (RNF-05): el detalle se lee en la base o en el correo del equipo.
        LogReceived(logger, report.Id, report.Target.Type, report.Reason);
        await NotifyAsync(report, cancellationToken).ConfigureAwait(false);
        // NOTE (HU-39): bandeja de reportes del administrador, con estado y respuesta a quien reporto.
        return Result<ReportResponse>.Ok(new ReportResponse(report.Id, report.CreatedAt));
    }

    private async Task<Result<ReportTarget>> ValidateTargetAsync(MemberSummary me, CreateReportRequest request, CancellationToken cancellationToken)
    {
        switch (request.TargetType)
        {
            case ReportTargetTypes.General when string.IsNullOrEmpty(request.TargetId) && string.IsNullOrEmpty(request.ConversationId):
                return Result<ReportTarget>.Ok(new ReportTarget { Type = ReportTargetTypes.General });

            case ReportTargetTypes.User when !string.IsNullOrEmpty(request.TargetId):
                if (string.Equals(request.TargetId, me.Id, StringComparison.Ordinal))
                {
                    return ReputationErrors.InvalidTarget;
                }

                var other = ObjectId.TryParse(request.TargetId, out _)
                    ? await members.FindMemberAsync(request.TargetId, cancellationToken).ConfigureAwait(false)
                    : null;
                if (other is null || !string.Equals(other.CommunityId, me.CommunityId, StringComparison.Ordinal)
                    || !await InConversationAsync(request.ConversationId, me.Id, other.Id, cancellationToken).ConfigureAwait(false))
                {
                    return ReputationErrors.TargetNotFound;
                }

                return Result<ReportTarget>.Ok(new ReportTarget { Type = ReportTargetTypes.User, Id = other.Id });

            case ReportTargetTypes.Product when !string.IsNullOrEmpty(request.TargetId) && string.IsNullOrEmpty(request.ConversationId):
                var product = ObjectId.TryParse(request.TargetId, out _)
                    ? await products.FindAsync(request.TargetId, cancellationToken).ConfigureAwait(false)
                    : null;
                if (product is null || !string.Equals(product.CommunityId, me.CommunityId, StringComparison.Ordinal))
                {
                    return ReputationErrors.TargetNotFound;
                }

                return string.Equals(product.SellerId, me.Id, StringComparison.Ordinal)
                    ? ReputationErrors.InvalidTarget
                    : Result<ReportTarget>.Ok(new ReportTarget { Type = ReportTargetTypes.Product, Id = product.Id });

            default:
                return ReputationErrors.InvalidTarget;
        }
    }

    // Sin conversacion no hay nada que revisar; con ella, los dos deben participar (RN-22: no se revela si existe).
    private async Task<bool> InConversationAsync(string? conversationId, string reporterId, string otherId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(conversationId))
        {
            return true;
        }

        var participants = ObjectId.TryParse(conversationId, out _)
            ? await conversations.FindParticipantsAsync(conversationId, cancellationToken).ConfigureAwait(false)
            : null;
        return participants is not null
            && participants.Contains(reporterId, StringComparer.Ordinal)
            && participants.Contains(otherId, StringComparer.Ordinal);
    }

    private async Task NotifyAsync(ReportDocument report, CancellationToken cancellationToken)
    {
        var to = options.Value.NotifyEmail;
        if (string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        var body = string.Join(
            '\n',
            "Llegó un reporte nuevo a Musubi.",
            string.Empty,
            "Reporte: " + report.Id,
            "Fecha (UTC): " + report.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            "Tipo: " + report.Target.Type + (report.Target.Id is null ? string.Empty : " " + report.Target.Id),
            "Conversación: " + (report.ConversationId ?? "—"),
            "Motivo: " + report.Reason,
            "Quién reporta: " + report.ReporterId,
            string.Empty,
            "Qué pasó:",
            report.Details ?? "(sin detalle)",
            string.Empty,
            "Revísalo en la colección reports (status: open). La app le dijo a la persona que le escribirán en menos de 24 horas.");
        try
        {
            await email.SendAsync(new EmailMessage(to, "Nuevo reporte en Musubi (" + report.Reason + ")", body), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El reporte ya se guardo: un fallo del correo no se lo niega a quien reporta.
            LogNotifyFailed(logger, ex, report.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reporte {ReportId} recibido ({TargetType}, {Reason})")]
    private static partial void LogReceived(ILogger logger, string reportId, string targetType, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "No se pudo avisar por correo del reporte {ReportId}")]
    private static partial void LogNotifyFailed(ILogger logger, Exception exception, string reportId);
}
