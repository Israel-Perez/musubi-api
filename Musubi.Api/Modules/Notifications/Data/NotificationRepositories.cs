using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Notifications.Data;

// Contratos de persistencia de M-09 (HU-31). Mongo* (nube y local) e InMemory* (UseInMemoryDatabase=true) pasan la
// misma bateria de pruebas (TestShared/Notifications).

/// <summary>Avisos del centro de notificaciones.</summary>
internal interface INotificationRepository
{
    /// <summary>Guarda el aviso; con una <c>dedupeKey</c> que ya existe para esa persona no hace nada.</summary>
    /// <param name="notification">El aviso.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si es nuevo.</returns>
    Task<bool> TryInsertAsync(NotificationDocument notification, CancellationToken cancellationToken);

    /// <summary>Los avisos de una persona, del mas nuevo al mas viejo, paginados por cursor.</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="category">Pestana, o <c>null</c> para "Todas".</param>
    /// <param name="beforeId">Cursor: solo ids menores.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los avisos.</returns>
    Task<IReadOnlyList<NotificationDocument>> FindPageAsync(string userId, string? category, string? beforeId, int limit, CancellationToken cancellationToken);

    /// <summary>Cuantos no ha leido.</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El numero.</returns>
    Task<long> CountUnreadAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Cuantos de un tipo tiene una persona desde un momento (HU-19, RN-30).</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="type">Tipo.</param>
    /// <param name="since">Desde (UTC, incluido).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El numero.</returns>
    Task<long> CountSinceAsync(string userId, string type, DateTime since, CancellationToken cancellationToken);

    /// <summary>Marca como leidos los suyos (uno, o todos con <paramref name="id"/> <c>null</c>).</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="id">El aviso, o <c>null</c>.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos cambiaron.</returns>
    Task<long> MarkReadAsync(string userId, string? id, DateTime now, CancellationToken cancellationToken);

    /// <summary>Borra los avisos de una persona (HU-46).</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se borraron.</returns>
    Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Tokens de FCM de los dispositivos.</summary>
internal interface IDeviceTokenRepository
{
    /// <summary>Registra el token para el usuario (si era de otra cuenta, pasa a esta) y alarga su vida.</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="token">Token de FCM.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="lifetime">Cuanto vive sin volver a registrarse.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task UpsertAsync(string userId, string token, DateTime now, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>Quita el token (al cerrar sesion o si FCM dice que ya no existe).</summary>
    /// <param name="token">Token.</param>
    /// <param name="userId">Solo si es de este usuario, o <c>null</c> para cualquiera.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si existia.</returns>
    Task<bool> RemoveAsync(string token, string? userId, CancellationToken cancellationToken);

    /// <summary>Los tokens vigentes de una persona.</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los tokens.</returns>
    Task<IReadOnlyList<string>> FindByUserAsync(string userId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Borra los tokens de una persona (HU-46).</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se borraron.</returns>
    Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Avisos en MongoDB (coleccion <c>notifications</c>, db/017).</summary>
internal sealed class MongoNotificationRepository(IMongoDatabase database) : INotificationRepository
{
    private static readonly FilterDefinitionBuilder<NotificationDocument> Filter = Builders<NotificationDocument>.Filter;

    private readonly IMongoCollection<NotificationDocument> notifications = database.GetCollection<NotificationDocument>(MongoSchema.Notifications);

    /// <inheritdoc />
    public async Task<bool> TryInsertAsync(NotificationDocument notification, CancellationToken cancellationToken)
    {
        try
        {
            await notifications.InsertOneAsync(notification, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // ux_user_dedupe: ese aviso ya se dio.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NotificationDocument>> FindPageAsync(string userId, string? category, string? beforeId, int limit, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return [];
        }

        var filter = Filter.Eq(n => n.UserId, userId);
        if (category is not null)
        {
            filter &= Filter.Eq(n => n.Category, category);
        }

        if (beforeId is not null)
        {
            filter &= Filter.Lt("_id", ObjectId.Parse(beforeId));
        }

        return await notifications.Find(filter).SortByDescending(n => n.Id).Limit(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> CountSinceAsync(string userId, string type, DateTime since, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? await notifications.CountDocumentsAsync(
                Filter.Eq(n => n.UserId, userId) & Filter.Eq(n => n.Type, type) & Filter.Gte(n => n.CreatedAt, since),
                cancellationToken: cancellationToken).ConfigureAwait(false)
            : 0;

    /// <inheritdoc />
    public async Task<long> CountUnreadAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? await notifications.CountDocumentsAsync(Filter.Eq(n => n.UserId, userId) & Filter.Eq(n => n.ReadAt, null), cancellationToken: cancellationToken).ConfigureAwait(false)
            : 0;

    /// <inheritdoc />
    public async Task<long> MarkReadAsync(string userId, string? id, DateTime now, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(userId, out _) || (id is not null && !ObjectId.TryParse(id, out _)))
        {
            return 0;
        }

        var filter = Filter.Eq(n => n.UserId, userId) & Filter.Eq(n => n.ReadAt, null);
        if (id is not null)
        {
            filter &= Filter.Eq(n => n.Id, id);
        }

        return (await notifications.UpdateManyAsync(filter, Builders<NotificationDocument>.Update.Set(n => n.ReadAt, now), cancellationToken: cancellationToken)
            .ConfigureAwait(false)).ModifiedCount;
    }

    /// <inheritdoc />
    public async Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? (await notifications.DeleteManyAsync(n => n.UserId == userId, cancellationToken).ConfigureAwait(false)).DeletedCount
            : 0;
}

/// <summary>Tokens en MongoDB (coleccion <c>deviceTokens</c>, db/017).</summary>
internal sealed class MongoDeviceTokenRepository(IMongoDatabase database) : IDeviceTokenRepository
{
    private static readonly FilterDefinitionBuilder<DeviceTokenDocument> Filter = Builders<DeviceTokenDocument>.Filter;

    private readonly IMongoCollection<DeviceTokenDocument> tokens = database.GetCollection<DeviceTokenDocument>(MongoSchema.DeviceTokens);

    /// <inheritdoc />
    public async Task UpsertAsync(string userId, string token, DateTime now, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var update = Builders<DeviceTokenDocument>.Update
            .Set(t => t.UserId, userId)
            .Set(t => t.LastSeenAt, now)
            .Set(t => t.ExpiresAt, now + lifetime)
            .SetOnInsert(t => t.Id, MongoConventions.NewId())
            .SetOnInsert(t => t.CreatedAt, now)
            .SetOnInsert(t => t.SchemaVersion, 1);
        try
        {
            await tokens.UpdateOneAsync(Filter.Eq(t => t.Token, token), update, new UpdateOptions { IsUpsert = true }, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Dos registros del mismo token a la vez: el segundo actualiza el que ya entro.
            await tokens.UpdateOneAsync(Filter.Eq(t => t.Token, token), update, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string token, string? userId, CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(t => t.Token, token);
        if (userId is not null)
        {
            filter &= Filter.Eq(t => t.UserId, userId);
        }

        return (await tokens.DeleteOneAsync(filter, cancellationToken).ConfigureAwait(false)).DeletedCount > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindByUserAsync(string userId, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? await tokens.Find(Filter.Eq(t => t.UserId, userId) & Filter.Gt(t => t.ExpiresAt, now))
                .Project(t => t.Token)
                .ToListAsync(cancellationToken).ConfigureAwait(false)
            : [];

    /// <inheritdoc />
    public async Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken) =>
        ObjectId.TryParse(userId, out _)
            ? (await tokens.DeleteManyAsync(t => t.UserId == userId, cancellationToken).ConfigureAwait(false)).DeletedCount
            : 0;
}

/// <summary>Avisos en memoria (Development y pruebas).</summary>
internal sealed class InMemoryNotificationRepository : INotificationRepository
{
    private readonly Lock gate = new();
    private readonly List<NotificationDocument> notifications = [];

    /// <inheritdoc />
    public Task<bool> TryInsertAsync(NotificationDocument notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (gate)
        {
            if (notification.DedupeKey is not null
                && notifications.Exists(n => n.UserId == notification.UserId && n.DedupeKey == notification.DedupeKey))
            {
                return Task.FromResult(false);
            }

            notifications.Add(notification);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<NotificationDocument>> FindPageAsync(string userId, string? category, string? beforeId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<NotificationDocument> page = notifications
                .Where(n => n.UserId == userId
                    && (category is null || n.Category == category)
                    && (beforeId is null || string.CompareOrdinal(n.Id, beforeId) < 0))
                .OrderByDescending(n => n.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult(page);
        }
    }

    /// <inheritdoc />
    public Task<long> CountSinceAsync(string userId, string type, DateTime since, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)notifications.Count(n => n.UserId == userId && n.Type == type && n.CreatedAt >= since));
        }
    }

    /// <inheritdoc />
    public Task<long> CountUnreadAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)notifications.Count(n => n.UserId == userId && n.ReadAt is null));
        }
    }

    /// <inheritdoc />
    public Task<long> MarkReadAsync(string userId, string? id, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            long changed = 0;
            for (var i = 0; i < notifications.Count; i++)
            {
                var n = notifications[i];
                if (n.UserId == userId && n.ReadAt is null && (id is null || n.Id == id))
                {
                    notifications[i] = new NotificationDocument
                    {
                        Id = n.Id,
                        UserId = n.UserId,
                        Type = n.Type,
                        Category = n.Category,
                        Title = n.Title,
                        Body = n.Body,
                        Target = n.Target,
                        DedupeKey = n.DedupeKey,
                        ReadAt = now,
                        CreatedAt = n.CreatedAt,
                        ExpiresAt = n.ExpiresAt,
                        SchemaVersion = n.SchemaVersion,
                    };
                    changed++;
                }
            }

            return Task.FromResult(changed);
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)notifications.RemoveAll(n => n.UserId == userId));
        }
    }
}

/// <summary>Tokens en memoria (Development y pruebas).</summary>
internal sealed class InMemoryDeviceTokenRepository : IDeviceTokenRepository
{
    private readonly Lock gate = new();
    private readonly List<DeviceTokenDocument> tokens = [];

    /// <inheritdoc />
    public Task UpsertAsync(string userId, string token, DateTime now, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var existing = tokens.Find(t => t.Token == token);
            tokens.RemoveAll(t => t.Token == token);
            tokens.Add(new DeviceTokenDocument
            {
                Id = existing?.Id ?? MongoConventions.NewId(),
                UserId = userId,
                Token = token,
                CreatedAt = existing?.CreatedAt ?? now,
                LastSeenAt = now,
                ExpiresAt = now + lifetime,
            });
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string token, string? userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(tokens.RemoveAll(t => t.Token == token && (userId is null || t.UserId == userId)) > 0);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> FindByUserAsync(string userId, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<string> found = tokens.Where(t => t.UserId == userId && t.ExpiresAt > now).Select(t => t.Token).ToList();
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)tokens.RemoveAll(t => t.UserId == userId));
        }
    }
}
