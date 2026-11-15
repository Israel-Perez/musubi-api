using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.PurchaseRequests;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Matching;

/// <summary>Que tan bien coincide una publicacion con una solicitud (HU-18).</summary>
internal static class WantedMatchKinds
{
    /// <summary>Cumple las reglas de HU-19 ("Dentro de tu presupuesto").</summary>
    public const string Exact = "exact";

    /// <summary>Parecida: de la categoria, con al menos una palabra y hasta 50 % sobre el presupuesto ("Modelo parecido", RF-24).</summary>
    public const string Similar = "similar";
}

/// <summary>Una publicacion que coincide con mi solicitud.</summary>
/// <param name="Product">La publicacion, con quien la vende.</param>
/// <param name="Kind"><c>exact</c> o <c>similar</c>.</param>
public sealed record WantedMatchResponse(ProductResponse Product, string Kind);

/// <summary>"Ya coinciden con lo que buscas" (maqueta <c>Necesidad-publicada</c>).</summary>
/// <param name="Items">Primero las exactas y despues las parecidas.</param>
/// <param name="Exact">Cuantas de ellas son exactas.</param>
public sealed record WantedMatchPage(IReadOnlyList<WantedMatchResponse> Items, int Exact);

/// <summary>
/// "Ya coinciden con lo que buscas" (HU-18, decision de la usuaria del 14-Nov-2026): las publicaciones disponibles que
/// cumplen las reglas de HU-19 y, si faltan, las parecidas (RF-24). Nunca lo propio ni de personas bloqueadas (RN-31,
/// HU-57). Solo quien publico la solicitud la consulta.
/// </summary>
internal sealed class WantedMatchesService(
    IWantedMatchDirectory wanted,
    IProductMatches products,
    IRecommendationCatalog catalog,
    IBlockDirectory blocks)
{
    /// <summary>Coincidencias si no se pide otro numero.</summary>
    public const int DefaultLimit = 10;

    /// <summary>Coincidencias como maximo.</summary>
    public const int MaxLimit = 20;

    /// <summary>Lo que una parecida puede pasar del presupuesto, en por ciento.</summary>
    public const int SimilarMarginPercent = 50;

    /// <summary>Las coincidencias de mi solicitud.</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="limit">De 1 a 20 (10 si no se manda).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las coincidencias o <c>wanted.not_found</c>.</returns>
    public async Task<Result<WantedMatchPage>> FindAsync(string userId, string requestId, int? limit, CancellationToken cancellationToken)
    {
        if (await wanted.FindOwnAsync(userId, requestId, cancellationToken).ConfigureAwait(false) is not { } request)
        {
            return WantedErrors.NotFound;
        }

        var terms = MatchRules.Terms(request.Title);
        if (!request.Active || terms.Count == 0)
        {
            return Result<WantedMatchPage>.Ok(new WantedMatchPage([], 0));
        }

        List<string> excluded = [userId, .. await blocks.HiddenForAsync(userId, cancellationToken).ConfigureAwait(false)];
        var candidates = await products.FindCandidatesAsync(
            new ProductMatchQuery(request.CommunityId, request.CategorySlug, terms, MaxSimilarPrice(request.BudgetCents), excluded, MatchRules.MaxCandidates),
            cancellationToken).ConfigureAwait(false);
        var ranked = Rank(candidates, terms, request.BudgetCents).Take(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit)).ToList();
        var kinds = ranked.ToDictionary(m => m.Id, m => m.Kind, StringComparer.Ordinal);
        // Las tarjetas salen en el mismo orden y solo las que siguen disponibles.
        var cards = await catalog.CardsAsync([.. ranked.Select(m => m.Id)], includeSeller: true, cancellationToken).ConfigureAwait(false);
        var items = cards.Select(card => new WantedMatchResponse(card, kinds[card.Id])).ToList();
        return Result<WantedMatchPage>.Ok(new WantedMatchPage(items, items.Count(i => i.Kind == WantedMatchKinds.Exact)));
    }

    /// <summary>Precio maximo de una parecida.</summary>
    /// <param name="budgetCents">Presupuesto.</param>
    /// <returns>El presupuesto mas el margen de las parecidas.</returns>
    public static long MaxSimilarPrice(long budgetCents) => budgetCents + (budgetCents * SimilarMarginPercent / 100);

    /// <summary>Exactas primero (mas palabras y precio mas cercano al presupuesto); despues las parecidas.</summary>
    /// <param name="candidates">Las candidatas (ya de la categoria y con al menos una palabra).</param>
    /// <param name="terms">Las palabras buscadas.</param>
    /// <param name="budgetCents">Presupuesto.</param>
    /// <returns>Id y tipo, en orden.</returns>
    public static IEnumerable<(string Id, string Kind)> Rank(IReadOnlyList<ProductCandidate> candidates, IReadOnlyList<string> terms, long budgetCents) =>
        candidates
            .Select(product =>
            {
                var share = MatchRules.Share(terms, product.Words);
                var exact = share >= MatchRules.MinShare && product.PriceCents <= MatchRules.MaxPrice(budgetCents);
                return (product.Id, Share: share, Distance: Math.Abs(product.PriceCents - budgetCents), Kind: exact ? WantedMatchKinds.Exact : WantedMatchKinds.Similar);
            })
            .Where(m => m.Share > 0)
            .OrderBy(m => m.Kind == WantedMatchKinds.Exact ? 0 : 1)
            .ThenByDescending(m => m.Share)
            .ThenBy(m => m.Distance)
            .Select(m => (m.Id, m.Kind));
}
