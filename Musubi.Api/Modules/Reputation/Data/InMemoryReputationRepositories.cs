using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Musubi.Api.Modules.Reputation.Data;

// Implementaciones en memoria para UseInMemoryDatabase=true (dotnet run sin MongoDB y suites WAF).
// TestShared/Reputation verifica que se comporten igual que las de Mongo.

/// <summary>Reportes en memoria.</summary>
internal sealed class InMemoryReportRepository : IReportRepository
{
    private readonly Lock gate = new();
    private readonly List<ReportDocument> reports = [];

    /// <inheritdoc />
    public Task InsertAsync(ReportDocument report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        lock (gate)
        {
            reports.Add(report);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportDocument>> FindByReporterAsync(string reporterId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<ReportDocument>>(reports
                .Where(r => string.Equals(r.ReporterId, reporterId, StringComparison.Ordinal))
                .OrderByDescending(r => r.Id, StringComparer.Ordinal)
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByReporterAsync(string reporterId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)reports.RemoveAll(r => string.Equals(r.ReporterId, reporterId, StringComparison.Ordinal)));
        }
    }
}

/// <summary>Bloqueos en memoria.</summary>
internal sealed class InMemoryBlockRepository : IBlockRepository
{
    private readonly Lock gate = new();
    private readonly List<BlockDocument> blocks = [];

    /// <inheritdoc />
    public Task<bool> InsertIfAbsentAsync(BlockDocument block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);
        lock (gate)
        {
            // Misma regla que el indice unico ux_blocker_blocked.
            if (blocks.Exists(b => Is(b, block.BlockerId, block.BlockedId)))
            {
                return Task.FromResult(false);
            }

            blocks.Add(block);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string blockerId, string blockedId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(blocks.RemoveAll(b => Is(b, blockerId, blockedId)) > 0);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BlockDocument>> FindByBlockerAsync(string blockerId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<BlockDocument>>(blocks
                .Where(b => string.Equals(b.BlockerId, blockerId, StringComparison.Ordinal))
                .OrderByDescending(b => b.CreatedAt)
                .ThenByDescending(b => b.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<string>> FindRelatedAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in blocks)
            {
                if (string.Equals(b.BlockerId, userId, StringComparison.Ordinal))
                {
                    ids.Add(b.BlockedId);
                }
                else if (string.Equals(b.BlockedId, userId, StringComparison.Ordinal))
                {
                    ids.Add(b.BlockerId);
                }
            }

            return Task.FromResult<IReadOnlyCollection<string>>(ids);
        }
    }

    /// <inheritdoc />
    public Task<bool> ExistsBetweenAsync(string userId, string otherId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(blocks.Exists(b => Is(b, userId, otherId) || Is(b, otherId, userId)));
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByUserAsync(string userId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)blocks.RemoveAll(b =>
                string.Equals(b.BlockerId, userId, StringComparison.Ordinal) || string.Equals(b.BlockedId, userId, StringComparison.Ordinal)));
        }
    }

    private static bool Is(BlockDocument block, string blockerId, string blockedId) =>
        string.Equals(block.BlockerId, blockerId, StringComparison.Ordinal) && string.Equals(block.BlockedId, blockedId, StringComparison.Ordinal);
}
