using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Musubi.Api.Platform.Data;

/// <summary>Readiness contra MongoDB (01-03): comando <c>ping</c> a la base configurada.</summary>
/// <remarks>La descripcion es generica: el detalle del error no sale en <c>/health/ready</c>.</remarks>
internal sealed class MongoHealthCheck(IMongoDatabase database) : IHealthCheck
{
    private static readonly BsonDocument PingCommand = new("ping", 1);

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await database.RunCommandAsync<BsonDocument>(PingCommand, cancellationToken: cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("MongoDB responde.");
        }
        catch (MongoException ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB no responde.", ex);
        }
        catch (TimeoutException ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB no responde a tiempo.", ex);
        }
    }
}
