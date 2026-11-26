using System;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Matching.Data;

namespace Musubi.Api.Modules.Matching;

/// <summary>Contrato publico de M-06 para M-14 (HU-42, conexion).</summary>
internal interface IMatchIndicators
{
    /// <summary>Coincidencias encontradas en el periodo y cuantas terminaron en pedido (RF-25).</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="from">Inicio (UTC, inclusivo).</param>
    /// <param name="to">Fin (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los conteos.</returns>
    Task<MatchUsage> ConnectionAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el registro de coincidencias.</summary>
internal sealed class MatchIndicators(IMatchRepository matches) : IMatchIndicators
{
    /// <inheritdoc />
    public Task<MatchUsage> ConnectionAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        matches.UsageAsync(communityId, from, to, cancellationToken);
}
