using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.Reputation.Data;

/// <summary>Reportes en MongoDB (indices <c>ix_community_status</c> e <c>ix_reporter</c>).</summary>
internal sealed class MongoReportRepository(IMongoDatabase database) : IReportRepository
{
    private readonly IMongoCollection<ReportDocument> reports = database.GetCollection<ReportDocument>(MongoSchema.Reports);

    /// <inheritdoc />
    public Task InsertAsync(ReportDocument report, CancellationToken cancellationToken) =>
        reports.InsertOneAsync(report, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportDocument>> FindByReporterAsync(string reporterId, CancellationToken cancellationToken) =>
        await reports.Find(r => r.ReporterId == reporterId)
            .SortByDescending(r => r.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<long> DeleteByReporterAsync(string reporterId, CancellationToken cancellationToken)
    {
        var result = await reports.DeleteManyAsync(r => r.ReporterId == reporterId, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount;
    }
}

/// <summary>Bloqueos en MongoDB (indices <c>ux_blocker_blocked</c> e <c>ix_blocked</c>).</summary>
internal sealed class MongoBlockRepository(IMongoDatabase database) : IBlockRepository
{
    private static readonly FilterDefinitionBuilder<BlockDocument> Filter = Builders<BlockDocument>.Filter;

    private readonly IMongoCollection<BlockDocument> blocks = database.GetCollection<BlockDocument>(MongoSchema.Blocks);

    /// <inheritdoc />
    public async Task<bool> InsertIfAbsentAsync(BlockDocument block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);
        try
        {
            await blocks.InsertOneAsync(block, cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // ux_blocker_blocked: ya estaba bloqueada; dos toques seguidos no duplican.
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string blockerId, string blockedId, CancellationToken cancellationToken)
    {
        var result = await blocks.DeleteOneAsync(b => b.BlockerId == blockerId && b.BlockedId == blockedId, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount > 0;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlockDocument>> FindByBlockerAsync(string blockerId, int limit, CancellationToken cancellationToken) =>
        await blocks.Find(b => b.BlockerId == blockerId)
            .SortByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> FindRelatedAsync(string userId, CancellationToken cancellationToken)
    {
        // ux_blocker_blocked cubre el primer lado e ix_blocked el segundo.
        var found = await blocks.Find(Filter.Eq(b => b.BlockerId, userId) | Filter.Eq(b => b.BlockedId, userId))
            .Project(b => new { b.BlockerId, b.BlockedId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return found
            .Select(b => string.Equals(b.BlockerId, userId, StringComparison.Ordinal) ? b.BlockedId : b.BlockerId)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public Task<bool> ExistsBetweenAsync(string userId, string otherId, CancellationToken cancellationToken) =>
        blocks.Find(
                (Filter.Eq(b => b.BlockerId, userId) & Filter.Eq(b => b.BlockedId, otherId))
                | (Filter.Eq(b => b.BlockerId, otherId) & Filter.Eq(b => b.BlockedId, userId)))
            .AnyAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken)
    {
        var result = await blocks.DeleteManyAsync(Filter.Eq(b => b.BlockerId, userId) | Filter.Eq(b => b.BlockedId, userId), cancellationToken)
            .ConfigureAwait(false);
        return result.DeletedCount;
    }
}
