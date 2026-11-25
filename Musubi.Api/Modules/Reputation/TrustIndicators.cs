using System;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Reputation.Data;

namespace Musubi.Api.Modules.Reputation;

/// <summary>HU-42 (confianza): calificaciones y reportes del campus en el periodo.</summary>
/// <param name="Ratings">Calificaciones y su promedio.</param>
/// <param name="Reports">Reportes hechos y procedentes.</param>
internal sealed record Trust(RatingUsage Ratings, ReportUsage Reports);

/// <summary>Contrato publico de M-13 para M-14 (HU-42).</summary>
internal interface ITrustIndicators
{
    /// <summary>La confianza del campus en el periodo.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="from">Inicio (UTC, inclusivo).</param>
    /// <param name="to">Fin (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los conteos.</returns>
    Task<Trust> TrustAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre las calificaciones y los reportes.</summary>
internal sealed class TrustIndicators(IRatingRepository ratings, IReportRepository reports) : ITrustIndicators
{
    /// <inheritdoc />
    public async Task<Trust> TrustAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        new(
            await ratings.UsageAsync(communityId, from, to, cancellationToken).ConfigureAwait(false),
            await reports.UsageAsync(communityId, from, to, cancellationToken).ConfigureAwait(false));
}
