using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Reputation.Data;

// Persistencia de las calificaciones (HU-27, HU-45). Mongo e InMemory pasan la misma bateria (TestShared/Reputation).

/// <summary>Calificaciones.</summary>
internal interface IRatingRepository
{
    /// <summary>Guarda una calificacion; <c>false</c> si quien califica ya califico ese pedido (<c>ux_order_rater</c>).</summary>
    /// <param name="rating">La calificacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si se guardo.</returns>
    Task<bool> TryInsertAsync(RatingDocument rating, CancellationToken cancellationToken);

    /// <summary>Las que recibio una persona, de la mas nueva a la mas vieja (<c>ix_rated_id</c>).</summary>
    /// <param name="ratedId">Quien las recibio.</param>
    /// <param name="beforeId">Cursor: solo las anteriores a este id, o <c>null</c>.</param>
    /// <param name="limit">Cuantas.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las calificaciones.</returns>
    Task<IReadOnlyList<RatingDocument>> FindByRatedAsync(string ratedId, string? beforeId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Borrado de una cuenta (HU-46, R11): se borran las que recibio y en las que dio queda <paramref name="replacementName"/>
    /// (siguen contando en la reputacion de la otra persona).
    /// </summary>
    /// <param name="userId">La persona.</param>
    /// <param name="replacementName">El nombre que queda.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La tarea.</returns>
    Task ReleaseAccountAsync(string userId, string replacementName, CancellationToken cancellationToken);
}

/// <summary>Calificaciones en MongoDB (indices de db/020-calificaciones.js).</summary>
internal sealed class MongoRatingRepository(IMongoDatabase database) : IRatingRepository
{
    private static readonly FilterDefinitionBuilder<RatingDocument> Filter = Builders<RatingDocument>.Filter;

    private readonly IMongoCollection<RatingDocument> ratings = database.GetCollection<RatingDocument>(MongoSchema.Ratings);

    /// <inheritdoc />
    public async Task<bool> TryInsertAsync(RatingDocument rating, CancellationToken cancellationToken)
    {
        try
        {
            await ratings.InsertOneAsync(rating, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // ux_order_rater: gana el primero de dos toques a la vez (RN-24).
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RatingDocument>> FindByRatedAsync(string ratedId, string? beforeId, int limit, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(ratedId, out _))
        {
            return [];
        }

        var filter = Filter.Eq(r => r.RatedId, ratedId);
        if (beforeId is not null)
        {
            filter &= Filter.Lt("_id", ObjectId.Parse(beforeId));
        }

        return await ratings.Find(filter)
            .SortByDescending(r => r.Id)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReleaseAccountAsync(string userId, string replacementName, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return;
        }

        await ratings.DeleteManyAsync(r => r.RatedId == userId, cancellationToken).ConfigureAwait(false);
        await ratings.UpdateManyAsync(
            r => r.RaterId == userId,
            Builders<RatingDocument>.Update.Set(r => r.RaterName, replacementName),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Calificaciones en memoria (pruebas y desarrollo sin base).</summary>
internal sealed class InMemoryRatingRepository : IRatingRepository
{
    private readonly Lock gate = new();
    private readonly List<RatingDocument> ratings = [];

    /// <inheritdoc />
    public Task<bool> TryInsertAsync(RatingDocument rating, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rating);
        lock (gate)
        {
            if (ratings.Exists(r => string.Equals(r.OrderId, rating.OrderId, StringComparison.Ordinal)
                && string.Equals(r.RaterId, rating.RaterId, StringComparison.Ordinal)))
            {
                return Task.FromResult(false);
            }

            ratings.Add(rating);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RatingDocument>> FindByRatedAsync(string ratedId, string? beforeId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<RatingDocument> found = ratings
                .Where(r => string.Equals(r.RatedId, ratedId, StringComparison.Ordinal)
                    && (beforeId is null || string.CompareOrdinal(r.Id, beforeId) < 0))
                .OrderByDescending(r => r.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList();
            return Task.FromResult(found);
        }
    }

    /// <inheritdoc />
    public Task ReleaseAccountAsync(string userId, string replacementName, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ratings.RemoveAll(r => string.Equals(r.RatedId, userId, StringComparison.Ordinal));
            for (var i = 0; i < ratings.Count; i++)
            {
                var r = ratings[i];
                if (string.Equals(r.RaterId, userId, StringComparison.Ordinal))
                {
                    ratings[i] = new RatingDocument
                    {
                        Id = r.Id,
                        CommunityId = r.CommunityId,
                        OrderId = r.OrderId,
                        RaterId = r.RaterId,
                        RaterName = replacementName,
                        RatedId = r.RatedId,
                        RatedRole = r.RatedRole,
                        Stars = r.Stars,
                        Tags = r.Tags,
                        Comment = r.Comment,
                        CreatedAt = r.CreatedAt,
                        SchemaVersion = r.SchemaVersion,
                    };
                }
            }

            return Task.CompletedTask;
        }
    }
}
