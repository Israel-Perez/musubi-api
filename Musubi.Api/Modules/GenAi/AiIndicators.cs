using System;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.GenAi.Data;

namespace Musubi.Api.Modules.GenAi;

/// <summary>Contrato publico de M-10 para M-14 (HU-42, IA).</summary>
internal interface IAiIndicators
{
    /// <summary>Resumenes pedidos y acuerdos confirmados (y sin correcciones) en el periodo.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="from">Inicio (UTC, inclusivo).</param>
    /// <param name="to">Fin (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los conteos.</returns>
    Task<AiUsage> UsageAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el registro del uso de la IA.</summary>
internal sealed class AiIndicators(IAiUsageRepository usage) : IAiIndicators
{
    /// <inheritdoc />
    public Task<AiUsage> UsageAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        usage.UsageAsync(communityId, from, to, cancellationToken);
}
