using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.Reputation;

namespace Musubi.Api.Modules.Matching;

/// <summary>
/// Quien busca algo como una publicacion (HU-38, RN-37): las solicitudes "Se busca" activas que coinciden (publicas, de
/// la mas parecida a la de mayor presupuesto) y cuantas personas mas lo esperan con una busqueda guardada (HU-12), que
/// solo se cuentan: nunca se dice quienes son.
/// </summary>
/// <param name="Requests">Las solicitudes, una por persona.</param>
/// <param name="AlertPeople">Personas con una busqueda guardada que coincide y sin solicitud de las de arriba.</param>
internal sealed record ProductDemand(IReadOnlyList<WantedForMatch> Requests, int AlertPeople)
{
    /// <summary>Sin nadie buscandola.</summary>
    public static readonly ProductDemand None = new([], 0);

    /// <summary>Personas que la buscan ("3 estudiantes la buscan").</summary>
    public int People => Requests.Count + AlertPeople;
}

/// <summary>Contrato publico de M-06 para M-12 (HU-36/37/38): la demanda de una publicacion.</summary>
internal interface IProductDemand
{
    /// <summary>Quien la busca ahora mismo, con las reglas de los avisos (HU-29 y HU-12).</summary>
    /// <param name="product">La publicacion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La demanda.</returns>
    Task<ProductDemand> ForAsync(ProductPublished product, CancellationToken cancellationToken);
}

/// <summary>Las reglas con las que una publicacion le sirve a una solicitud o a una busqueda guardada.</summary>
internal static class DemandRules
{
    /// <summary>Solicitudes candidatas que se leen como maximo.</summary>
    public const int MaxRequests = 200;

    /// <summary>Busquedas guardadas que se leen como maximo (10 por persona).</summary>
    public const int MaxSavedSearches = 2000;

    /// <summary>Presupuesto minimo para que un precio quepa con el margen de 20 % (RN-28).</summary>
    /// <param name="priceCents">Precio.</param>
    /// <returns>El presupuesto minimo.</returns>
    public static long MinBudget(long priceCents) =>
        (long)Math.Ceiling(priceCents * 100m / (100 + MatchRules.BudgetMarginPercent));

    /// <summary>
    /// RN-28: la mitad de las palabras buscadas y el precio hasta 20 % arriba del presupuesto; RN-29: de cada persona,
    /// solo su solicitud que mas se parece. De la mas parecida a la de mayor presupuesto.
    /// </summary>
    /// <param name="requests">Las candidatas.</param>
    /// <param name="product">La publicacion.</param>
    /// <returns>Las solicitudes que coinciden.</returns>
    public static IReadOnlyList<WantedForMatch> BestRequests(IEnumerable<WantedForMatch> requests, ProductPublished product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return [.. requests
            .Select(request => (Request: request, Share: MatchRules.Share(MatchRules.Terms(request.Title), product.Words)))
            .Where(match => match.Share >= MatchRules.MinShare && product.PriceCents <= MatchRules.MaxPrice(match.Request.BudgetCents))
            .GroupBy(match => match.Request.RequesterId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(match => match.Share).First())
            .OrderByDescending(match => match.Share)
            .ThenByDescending(match => match.Request.BudgetCents)
            .Select(match => match.Request)];
    }

    /// <summary>HU-12: las busquedas guardadas que coinciden (todas las palabras y los filtros), una por persona.</summary>
    /// <param name="searches">Las vigentes del campus.</param>
    /// <param name="product">La publicacion.</param>
    /// <returns>Las busquedas.</returns>
    public static IEnumerable<SavedSearchCandidate> MatchingSearches(IEnumerable<SavedSearchCandidate> searches, ProductPublished product) =>
        searches
            .Where(search => search.Matches(product))
            .GroupBy(search => search.UserId, StringComparer.Ordinal)
            .Select(group => group.First());
}

/// <summary>Implementacion con los mismos directorios que los avisos (M-05 y M-04).</summary>
internal sealed class ProductDemandService(IWantedMatchDirectory wanted, ISavedSearchDirectory savedSearches, IBlockDirectory blocks) : IProductDemand
{
    /// <inheritdoc />
    public async Task<ProductDemand> ForAsync(ProductPublished product, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        // RN-31 y HU-57: ni lo propio ni lo de quien bloqueo o fue bloqueado.
        List<string> excluded = [product.SellerId, .. await blocks.HiddenForAsync(product.SellerId, cancellationToken).ConfigureAwait(false)];
        var candidates = await wanted.ActiveCandidatesAsync(
            product.CommunityId,
            product.CategorySlug,
            DemandRules.MinBudget(product.PriceCents),
            excluded,
            DemandRules.MaxRequests,
            cancellationToken).ConfigureAwait(false);
        var requests = DemandRules.BestRequests(candidates, product);
        var requesters = requests.Select(request => request.RequesterId).ToHashSet(StringComparer.Ordinal);
        var searches = await savedSearches.ActiveAsync(product.CommunityId, excluded, DemandRules.MaxSavedSearches, cancellationToken).ConfigureAwait(false);
        var alerts = DemandRules.MatchingSearches(searches, product).Count(search => !requesters.Contains(search.UserId));
        return new ProductDemand(requests, alerts);
    }
}
