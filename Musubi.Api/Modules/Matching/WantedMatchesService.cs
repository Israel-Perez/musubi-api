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

    /// <summary>
    /// Parecida: de la categoria, con al menos una palabra (o, con HU-60, un significado cercano) y hasta 50 % sobre el
    /// presupuesto ("Modelo parecido", RF-24).
    /// </summary>
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
    IBlockDirectory blocks,
    SemanticMatcher semantic)
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
        var bySense = semantic.Enabled;
        if (!request.Active || (terms.Count == 0 && !bySense))
        {
            return Result<WantedMatchPage>.Ok(new WantedMatchPage([], 0));
        }

        List<string> excluded = [userId, .. await blocks.HiddenForAsync(userId, cancellationToken).ConfigureAwait(false)];
        var candidates = await products.FindCandidatesAsync(
            new ProductMatchQuery(request.CommunityId, request.CategorySlug, terms, MaxSimilarPrice(request.BudgetCents), excluded, MatchRules.MaxCandidates, bySense),
            cancellationToken).ConfigureAwait(false);
        // HU-60: la app consulta aqui; solo los vectores ya guardados (los calcula el worker al publicar).
        var similarity = await semantic.SimilaritiesAsync(
            MatchText.Wanted(request.Id, request.RequesterId, request.CommunityId, request.Title),
            [.. candidates.Select(p => MatchText.Product(p.Id, p.SellerId, request.CommunityId, p.Title, p.Description))],
            computeMissing: false,
            cancellationToken).ConfigureAwait(false);
        var ranked = Rank(candidates, terms, request.BudgetCents, similarity).Take(Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit)).ToList();
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

    /// <summary>Exactas primero (las mas parecidas y de precio mas cercano al presupuesto); despues las parecidas.</summary>
    /// <param name="candidates">Las candidatas (ya de la categoria y el precio).</param>
    /// <param name="terms">Las palabras buscadas.</param>
    /// <param name="budgetCents">Presupuesto.</param>
    /// <param name="similarity">HU-60: similitud por id (sin ella, solo palabras).</param>
    /// <returns>Id y tipo, en orden.</returns>
    public static IEnumerable<(string Id, string Kind)> Rank(
        IReadOnlyList<ProductCandidate> candidates,
        IReadOnlyList<string> terms,
        long budgetCents,
        IReadOnlyDictionary<string, double>? similarity = null) =>
        candidates
            .Select(product =>
            {
                var share = MatchRules.Share(terms, product.Words);
                double? sense = similarity is not null && similarity.TryGetValue(product.Id, out var s) ? s : null;
                var exact = MatchRules.Compatible(share, sense) && product.PriceCents <= MatchRules.MaxPrice(budgetCents);
                var near = share > 0 || sense >= MatchRules.SimilarSemanticThreshold;
                return (product.Id, Near: near, Score: MatchRules.Score(share, sense), Distance: Math.Abs(product.PriceCents - budgetCents), Kind: exact ? WantedMatchKinds.Exact : WantedMatchKinds.Similar);
            })
            .Where(m => m.Near || m.Kind == WantedMatchKinds.Exact)
            .OrderBy(m => m.Kind == WantedMatchKinds.Exact ? 0 : 1)
            .ThenByDescending(m => m.Score)
            .ThenBy(m => m.Distance)
            .Select(m => (m.Id, m.Kind));
}
