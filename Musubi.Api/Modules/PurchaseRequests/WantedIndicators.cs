using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.PurchaseRequests.Data;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>HU-42 (demanda): las solicitudes "Se busca" del campus.</summary>
/// <param name="Requests">Las publicadas en el periodo.</param>
/// <param name="ByCategory">Por categoria (slug; <c>otro</c> para cualquiera).</param>
/// <param name="OffersOnThem">Ofertas que recibieron.</param>
/// <param name="OffersMade">Ofertas hechas en el periodo.</param>
/// <param name="DirectOffers">De esas, las directas (un producto que no estaba publicado, HU-16).</param>
/// <param name="HoursToFirstOffer">Horas en promedio hasta la primera oferta (de las que recibieron alguna), o <c>null</c>.</param>
/// <param name="HoursToAccepted">Horas en promedio hasta aceptar una oferta, o <c>null</c>.</param>
internal sealed record Demand(
    long Requests,
    IReadOnlyDictionary<string, long> ByCategory,
    long OffersOnThem,
    long OffersMade,
    long DirectOffers,
    double? HoursToFirstOffer,
    double? HoursToAccepted);

/// <summary>Contrato publico de M-05 para M-14 (HU-42).</summary>
internal interface IWantedIndicators
{
    /// <summary>La demanda del campus en el periodo.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="from">Inicio (UTC, inclusivo).</param>
    /// <param name="to">Fin (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los conteos.</returns>
    Task<Demand> DemandAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre los repositorios de M-05.</summary>
internal sealed class WantedIndicators(IWantedRepository requests, IOfferRepository offers) : IWantedIndicators
{
    /// <summary>Solicitudes que se leen como maximo (un campus publica muchas menos en un mes).</summary>
    public const int MaxRequests = 5000;

    /// <inheritdoc />
    public async Task<Demand> DemandAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var created = await requests.CreatedBetweenAsync(communityId, from, to, MaxRequests, cancellationToken).ConfigureAwait(false);
        var received = await offers.FindByRequestsAsync([.. created.Select(r => r.Id)], cancellationToken).ConfigureAwait(false);
        var made = await offers.UsageAsync(communityId, from, to, cancellationToken).ConfigureAwait(false);
        var byRequest = received.GroupBy(o => o.RequestId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var toFirst = new List<double>();
        var toAccepted = new List<double>();
        foreach (var request in created)
        {
            if (!byRequest.TryGetValue(request.Id, out var mine))
            {
                continue;
            }

            toFirst.Add((mine.Min(o => o.CreatedAt) - request.CreatedAt).TotalHours);
            if (mine.Where(o => o.Status == OfferStatuses.Accepted && o.DecidedAt is not null).Select(o => o.DecidedAt!.Value).DefaultIfEmpty().Min() is var accepted
                && accepted != default)
            {
                toAccepted.Add((accepted - request.CreatedAt).TotalHours);
            }
        }

        return new Demand(
            created.Count,
            created.GroupBy(r => r.CategorySlug, StringComparer.Ordinal).ToDictionary(g => g.Key, g => (long)g.Count(), StringComparer.Ordinal),
            received.Count,
            made.Total,
            made.Direct,
            toFirst.Count == 0 ? null : Math.Max(0, toFirst.Average()),
            toAccepted.Count == 0 ? null : Math.Max(0, toAccepted.Average()));
    }
}
