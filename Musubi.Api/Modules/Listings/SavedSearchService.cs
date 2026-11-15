using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Listings;

/// <summary>"Avísame cuando aparezca" (maqueta <c>Busqueda-vacia</c>): lo que se busco, con sus filtros.</summary>
/// <param name="Q">Lo que se busco (hasta 80 caracteres, con al menos una letra o digito).</param>
/// <param name="Category">Categorias (cualquiera de ellas), o ninguna para todas.</param>
/// <param name="MinPriceCents">Precio minimo.</param>
/// <param name="MaxPriceCents">Precio maximo.</param>
/// <param name="Condition"><c>new</c>, <c>used</c> o ninguna.</param>
public sealed record SaveSearchRequest(
    [Required] string Q,
    IReadOnlyList<string>? Category = null,
    long? MinPriceCents = null,
    long? MaxPriceCents = null,
    string? Condition = null);

/// <summary>Una busqueda guardada ("Mis alertas").</summary>
/// <param name="Id">Id.</param>
/// <param name="Query">Lo que se busco.</param>
/// <param name="Categories">Categorias.</param>
/// <param name="MinPriceCents">Precio minimo.</param>
/// <param name="MaxPriceCents">Precio maximo.</param>
/// <param name="Condition">Estado.</param>
/// <param name="CreatedAt">Cuando se guardo.</param>
/// <param name="ExpiresAt">Cuando vence (30 dias, RN-34).</param>
public sealed record SavedSearchResponse(
    string Id,
    string Query,
    IReadOnlyList<string> Categories,
    long? MinPriceCents,
    long? MaxPriceCents,
    string? Condition,
    DateTime CreatedAt,
    DateTime ExpiresAt);

/// <summary>"Mis alertas".</summary>
/// <param name="Items">De la mas nueva a la mas vieja.</param>
public sealed record SavedSearchList(IReadOnlyList<SavedSearchResponse> Items);

/// <summary>Una busqueda guardada vigente, para avisar al publicar (HU-12 → M-06).</summary>
/// <param name="Id">Busqueda.</param>
/// <param name="UserId">Quien la guardo.</param>
/// <param name="Query">Lo que escribio.</param>
/// <param name="Terms">Palabras que debe tener la publicacion (todas).</param>
/// <param name="Categories">Categorias, o vacio para todas.</param>
/// <param name="MinPriceCents">Precio minimo.</param>
/// <param name="MaxPriceCents">Precio maximo.</param>
/// <param name="Condition">Estado, o <c>null</c>.</param>
internal sealed record SavedSearchCandidate(
    string Id,
    string UserId,
    string Query,
    IReadOnlyList<string> Terms,
    IReadOnlyList<string> Categories,
    long? MinPriceCents,
    long? MaxPriceCents,
    string? Condition)
{
    /// <summary>
    /// Si una publicacion es lo que se busco (decision de la usuaria, 14-Nov-2026): todas las palabras (cada una es el
    /// inicio de una palabra del nombre o la descripcion, como al buscar) y los filtros que tenia.
    /// </summary>
    /// <param name="product">La publicacion.</param>
    /// <returns><c>true</c> si coincide.</returns>
    public bool Matches(ProductPublished product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return Terms.Count > 0
            && Terms.All(term => product.Words.Any(word => word.StartsWith(term, StringComparison.Ordinal)))
            && (Categories.Count == 0 || Categories.Contains(product.CategorySlug, StringComparer.Ordinal))
            && (MinPriceCents is not { } min || product.PriceCents >= min)
            && (MaxPriceCents is not { } max || product.PriceCents <= max)
            && (Condition is null || string.Equals(Condition, product.Condition, StringComparison.Ordinal));
    }
}

/// <summary>Contrato publico de M-04 para M-06 (HU-12): las busquedas guardadas vigentes de un campus.</summary>
internal interface ISavedSearchDirectory
{
    /// <summary>Las vigentes de un campus.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="excludedUsers">Quien vende y las personas bloqueadas (RN-31, HU-57).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las busquedas.</returns>
    Task<IReadOnlyList<SavedSearchCandidate>> ActiveAsync(string communityId, IReadOnlyCollection<string> excludedUsers, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// "Avísame cuando aparezca" (HU-12, decisiones de la usuaria del 14-Nov-2026): guarda una busqueda sin resultados por
/// 30 dias (RN-34), hasta 10 por persona; "Mis alertas" en Ajustes las lista y las quita. Privadas (RNF-07).
/// </summary>
internal sealed class SavedSearchService(
    ISavedSearchRepository searches,
    IMemberDirectory members,
    TimeProvider time) : ISavedSearchDirectory, IAccountDataEraser
{
    /// <summary>Cuanto dura una busqueda guardada (RN-34).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>Busquedas guardadas por persona como maximo.</summary>
    public const int MaxPerUser = 10;

    /// <summary>Categorias por busqueda como maximo.</summary>
    public const int MaxCategories = 10;

    /// <summary>Guarda la busqueda o renueva la que ya tenia igual.</summary>
    /// <param name="userId">Quien (del token).</param>
    /// <param name="request">La busqueda.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La busqueda y si es nueva, o el error.</returns>
    public async Task<Result<(SavedSearchResponse Search, bool Created)>> SaveAsync(string userId, SaveSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = request.Q?.Trim() ?? string.Empty;
        var terms = Terms(query);
        if (query.Length > ListingService.MaxSearchLength || terms.Count == 0)
        {
            return ListingsErrors.InvalidQuery;
        }

        if (request.MinPriceCents is < 0 or > ListingService.MaxPriceCents
            || request.MaxPriceCents is < 0 or > ListingService.MaxPriceCents
            || (request.MinPriceCents is { } min && request.MaxPriceCents is { } max && min > max))
        {
            return ListingsErrors.InvalidPriceRange;
        }

        var condition = string.IsNullOrWhiteSpace(request.Condition) ? null : request.Condition.Trim();
        if (condition is not null and not (ProductConditions.New or ProductConditions.Used))
        {
            return ListingsErrors.InvalidCondition;
        }

        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var categories = (request.Category ?? [])
            .Select(slug => slug.Trim())
            .Where(slug => slug.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Take(MaxCategories)
            .ToList();
        var key = string.Join(
            '|',
            string.Join(' ', terms),
            string.Join(',', categories),
            request.MinPriceCents?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            request.MaxPriceCents?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            condition ?? string.Empty);

        var now = time.GetUtcNow().UtcDateTime;
        var mine = await searches.FindByUserAsync(userId, now, cancellationToken).ConfigureAwait(false);
        if (mine.Count >= MaxPerUser && !mine.Any(s => s.Key == key))
        {
            return ListingsErrors.TooManySavedSearches;
        }

        var (saved, created) = await searches.SaveAsync(
            new SavedSearchDocument
            {
                Id = MongoConventions.NewId(),
                UserId = userId,
                CommunityId = member.CommunityId,
                Query = query,
                Terms = terms,
                Categories = categories,
                MinPriceCents = request.MinPriceCents,
                MaxPriceCents = request.MaxPriceCents,
                Condition = condition,
                Key = key,
                CreatedAt = now,
                ExpiresAt = now + Lifetime,
            },
            now,
            cancellationToken).ConfigureAwait(false);
        return Result<(SavedSearchResponse, bool)>.Ok((ToResponse(saved), created));
    }

    /// <summary>"Mis alertas": las vigentes.</summary>
    /// <param name="userId">Quien (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las busquedas.</returns>
    public async Task<SavedSearchList> MineAsync(string userId, CancellationToken cancellationToken) =>
        new([.. (await searches.FindByUserAsync(userId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false)).Select(ToResponse)]);

    /// <summary>Quita una (idempotente: si no existe o no es suya, no pasa nada).</summary>
    /// <param name="userId">Quien (del token).</param>
    /// <param name="id">La busqueda.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public Task DeleteAsync(string userId, string id, CancellationToken cancellationToken) => searches.DeleteAsync(userId, id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedSearchCandidate>> ActiveAsync(
        string communityId,
        IReadOnlyCollection<string> excludedUsers,
        int limit,
        CancellationToken cancellationToken) =>
        [.. (await searches.FindActiveAsync(communityId, time.GetUtcNow().UtcDateTime, excludedUsers, limit, cancellationToken).ConfigureAwait(false))
            .Select(s => new SavedSearchCandidate(s.Id, s.UserId, s.Query, s.Terms, s.Categories, s.MinPriceCents, s.MaxPriceCents, s.Condition))];

    /// <inheritdoc />
    public Task EraseAsync(string userId, CancellationToken cancellationToken) => searches.DeleteByUserAsync(userId, cancellationToken);

    /// <summary>
    /// Las palabras de una busqueda, partidas igual que las de una publicacion (<see cref="SearchWord.Words"/>: "TI-84"
    /// son "ti" y "84"), normalizadas y sin repetir.
    /// </summary>
    /// <param name="query">Lo que se escribio.</param>
    /// <returns>Hasta 5 palabras.</returns>
    public static IReadOnlyList<string> Terms(string query) =>
        [.. SearchWord.Words(query)
            .Select(word => string.Concat(word.Where(char.IsLetterOrDigit)))
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(ListingService.MaxSearchTerms)];

    private static SavedSearchResponse ToResponse(SavedSearchDocument s) =>
        new(s.Id, s.Query, s.Categories, s.MinPriceCents, s.MaxPriceCents, s.Condition, s.CreatedAt, s.ExpiresAt);
}
