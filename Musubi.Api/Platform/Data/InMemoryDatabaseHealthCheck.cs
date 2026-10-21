using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Musubi.Api.Platform.Data;

/// <summary>Readiness con <c>UseInMemoryDatabase=true</c> (local y pruebas): no hay MongoDB que consultar.</summary>
internal sealed class InMemoryDatabaseHealthCheck : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy("Modo en memoria (sin MongoDB)."));
}
