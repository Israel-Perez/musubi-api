using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Musubi.Api.Platform.Data;

/// <summary>Readiness contra MongoDB (01-03, 02-01): la base responde y su esquema esta al dia.</summary>
/// <remarks>La descripcion es generica: el detalle del error no sale en <c>/health/ready</c>.</remarks>
internal sealed class MongoHealthCheck(IMongoDatabase database) : IHealthCheck
{
    private static readonly FilterDefinition<BsonDocument> SchemaFilter = Builders<BsonDocument>.Filter.Eq("_id", "schema");

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Una sola lectura prueba la conexion y la version del esquema (scripts de db/ aplicados).
            var schema = await database.GetCollection<BsonDocument>(MongoSchema.Meta)
                .Find(SchemaFilter)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var version = schema is not null && schema.TryGetValue("version", out var value) && value.IsInt32 ? value.AsInt32 : 0;
            return version >= MongoSchema.ExpectedVersion
                ? HealthCheckResult.Healthy("MongoDB responde.")
                : HealthCheckResult.Unhealthy("Esquema de datos desactualizado.");
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
