using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Notifications.Data;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Notifications;

/// <summary>Un aviso que otro modulo quiere dar.</summary>
/// <param name="UserId">A quien.</param>
/// <param name="Type">Ver <see cref="NotificationTypes"/>.</param>
/// <param name="Title">Titulo.</param>
/// <param name="Body">Texto.</param>
/// <param name="TargetType">Lo que abre: <c>conversation</c>, <c>order</c> o <c>wanted</c>.</param>
/// <param name="TargetId">Su id.</param>
/// <param name="Category">Pestana del historial; <c>null</c> = solo push (un mensaje: su historial es la bandeja).</param>
/// <param name="DedupeKey">Para no repetirlo, o <c>null</c>.</param>
internal sealed record Notice(
    string UserId,
    string Type,
    string Title,
    string Body,
    string TargetType,
    string TargetId,
    string? Category = null,
    string? DedupeKey = null);

/// <summary>
/// Contrato publico de M-09 (HU-31) para los demas modulos: guarda el aviso en el historial (si tiene pestana) y lo
/// manda por push en segundo plano. Nunca falla hacia quien lo llama: si la base o FCM fallan, se registra y el
/// mensaje, el pedido o la oferta siguen su curso (RNF-15).
/// </summary>
internal interface INotificationCenter
{
    /// <summary>Da un aviso.</summary>
    /// <param name="notice">El aviso.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task NotifyAsync(Notice notice, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el historial y la cola de push.</summary>
internal sealed partial class NotificationCenter(
    INotificationRepository notifications,
    PushQueue queue,
    TimeProvider time,
    ILogger<NotificationCenter> logger) : INotificationCenter
{
    /// <summary>Lo que vive un aviso en el historial.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(60);

    /// <summary>Caracteres del texto de un push.</summary>
    public const int MaxBody = 140;

    /// <inheritdoc />
    public async Task NotifyAsync(Notice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var body = notice.Body.Length > MaxBody ? notice.Body[..(MaxBody - 1)].TrimEnd() + "…" : notice.Body;
        string? id = null;
        if (notice.Category is { } category)
        {
            var now = time.GetUtcNow().UtcDateTime;
            id = MongoConventions.NewId();
            try
            {
                var fresh = await notifications.TryInsertAsync(
                    new NotificationDocument
                    {
                        Id = id,
                        UserId = notice.UserId,
                        Type = notice.Type,
                        Category = category,
                        Title = notice.Title,
                        Body = body,
                        Target = new NotificationTarget { Type = notice.TargetType, Id = notice.TargetId },
                        DedupeKey = notice.DedupeKey,
                        CreatedAt = now,
                        ExpiresAt = now + Retention,
                    },
                    cancellationToken).ConfigureAwait(false);
                if (!fresh)
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException)
            {
                LogNotSaved(logger, notice.Type);
                return;
            }
        }

        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["type"] = notice.Type,
            ["targetType"] = notice.TargetType,
            ["targetId"] = notice.TargetId,
        };
        if (id is not null)
        {
            data["notificationId"] = id;
        }

        if (!queue.TryEnqueue(new PushJob(notice.UserId, new PushMessage(notice.Title, body, data))))
        {
            LogQueueFull(logger, notice.Type);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se guardo un aviso {Type}: base no disponible")]
    private static partial void LogNotSaved(ILogger logger, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cola de push llena: el aviso {Type} queda solo en el historial")]
    private static partial void LogQueueFull(ILogger logger, string type);
}

/// <summary>
/// El centro de notificaciones (maqueta <c>Notificaciones</c>, RF-43) y el registro de los dispositivos para push
/// (HU-31). Cada persona solo ve y marca los suyos.
/// </summary>
internal sealed class NotificationService(
    INotificationRepository notifications,
    IDeviceTokenRepository tokens,
    IMemberDirectory members,
    TimeProvider time)
{
    /// <summary>Avisos por pagina por defecto.</summary>
    public const int DefaultPageSize = 30;

    /// <summary>Avisos por pagina como maximo.</summary>
    public const int MaxPageSize = 50;

    /// <summary>Largo maximo de un token de FCM (los reales miden ~160).</summary>
    public const int MaxTokenLength = 512;

    /// <summary>Lo que vive un token sin que la app lo vuelva a registrar.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(60);

    /// <summary>Registra (o renueva) el token de este dispositivo para quien tiene la sesion.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <param name="body">El token de FCM.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Exito o el error.</returns>
    public async Task<Result<bool>> RegisterDeviceAsync(string userId, DeviceRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (Clean(body.Token) is not { } token)
        {
            return NotificationsErrors.InvalidToken;
        }

        if (await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false) is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        await tokens.UpsertAsync(userId, token, time.GetUtcNow().UtcDateTime, TokenLifetime, cancellationToken).ConfigureAwait(false);
        return Result<bool>.Ok(true);
    }

    /// <summary>Quita el token de este dispositivo (al cerrar sesion). Idempotente; solo quita los suyos.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <param name="body">El token de FCM.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Exito o el error.</returns>
    public async Task<Result<bool>> RemoveDeviceAsync(string userId, DeviceRequest body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (Clean(body.Token) is not { } token)
        {
            return NotificationsErrors.InvalidToken;
        }

        await tokens.RemoveAsync(token, userId, cancellationToken).ConfigureAwait(false);
        return Result<bool>.Ok(true);
    }

    /// <summary>Mis avisos, del mas nuevo al mas viejo.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <param name="category">Pestana, o <c>null</c> para "Todas".</param>
    /// <param name="cursor">Cursor de la pagina anterior.</param>
    /// <param name="limit">Tamano de pagina.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina o el error.</returns>
    public async Task<Result<NotificationPage>> ListAsync(string userId, string? category, string? cursor, int? limit, CancellationToken cancellationToken)
    {
        if (category is not null && !NotificationCategories.All.Contains(category, StringComparer.Ordinal))
        {
            return NotificationsErrors.InvalidCategory;
        }

        if (cursor is not null && !ObjectId.TryParse(cursor, out _))
        {
            return NotificationsErrors.InvalidCursor;
        }

        var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var page = await notifications.FindPageAsync(userId, category, cursor, size + 1, cancellationToken).ConfigureAwait(false);
        var items = page.Take(size).Select(ToResponse).ToList();
        var unread = await notifications.CountUnreadAsync(userId, cancellationToken).ConfigureAwait(false);
        return Result<NotificationPage>.Ok(new NotificationPage(items, unread, page.Count > size ? items[^1].Id : null));
    }

    /// <summary>"Marcar leidas" (todas) o abrir un aviso (uno). Idempotente.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <param name="id">El aviso, o <c>null</c> para todos.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Exito o el error.</returns>
    public async Task<Result<bool>> MarkReadAsync(string userId, string? id, CancellationToken cancellationToken)
    {
        if (id is not null && !ObjectId.TryParse(id, out _))
        {
            return NotificationsErrors.NotFound;
        }

        await notifications.MarkReadAsync(userId, id, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        return Result<bool>.Ok(true);
    }

    private static NotificationResponse ToResponse(NotificationDocument n) => new(
        n.Id,
        n.Type,
        n.Category,
        n.Title,
        n.Body,
        new NotificationTargetResponse(n.Target.Type, n.Target.Id),
        n.ReadAt is not null,
        n.CreatedAt);

    private static string? Clean(string? token)
    {
        var t = token?.Trim();
        return string.IsNullOrEmpty(t) || t.Length > MaxTokenLength || t.Any(char.IsWhiteSpace) ? null : t;
    }
}
