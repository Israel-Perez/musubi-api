using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Data;

namespace Musubi.Api.Modules.PurchaseRequests.Data;

// Persistencia de M-05 (HU-13, HU-15). Mongo e InMemory pasan la misma bateria (TestShared/PurchaseRequests).

/// <summary>Consulta del tablero "Se busca" (HU-15): solo activas y sin vencer, de la mas nueva a la mas vieja.</summary>
/// <param name="CommunityId">Campus.</param>
/// <param name="Now">Ahora (UTC): las que vencieron no salen (RN-10).</param>
/// <param name="CategorySlugs">Categorias (OR), o <c>null</c> para todas.</param>
/// <param name="MinBudgetCents">Presupuesto minimo, inclusivo.</param>
/// <param name="MaxBudgetCents">Presupuesto maximo, inclusivo.</param>
/// <param name="NeededByUntil">Solo las que tienen fecha y la necesitan a mas tardar este dia (medianoche UTC).</param>
/// <param name="Search">Terminos ya normalizados (<see cref="SearchWord.Normalize"/>) separados por espacio, o <c>null</c>.</param>
/// <param name="ExcludeRequesters">Quien consulta y las personas bloqueadas en cualquier sentido (HU-57).</param>
/// <param name="BeforeId">Cursor: el id de la ultima de la pagina anterior.</param>
/// <param name="Limit">Tamano de pagina.</param>
internal sealed record WantedBoardQuery(
    string CommunityId,
    DateTime Now,
    IReadOnlyCollection<string>? CategorySlugs,
    long? MinBudgetCents,
    long? MaxBudgetCents,
    DateTime? NeededByUntil,
    string? Search,
    IReadOnlyCollection<string>? ExcludeRequesters,
    string? BeforeId,
    int Limit);

/// <summary>Lo que cambia al editar una solicitud (HU-14): ya validado, con su nuevo vencimiento (RN-10).</summary>
/// <param name="Title">Producto que se busca.</param>
/// <param name="CategorySlug">Slug de la categoria, u <c>otro</c>.</param>
/// <param name="CategoryName">Copia del nombre de la categoria.</param>
/// <param name="Description">Descripcion, o <c>null</c> para quitarla.</param>
/// <param name="Features">Caracteristicas deseadas.</param>
/// <param name="BudgetCents">Presupuesto maximo en centavos.</param>
/// <param name="Quantity">Piezas.</param>
/// <param name="NeededBy">Dia en que la necesita (medianoche UTC), o <c>null</c> para quitarlo.</param>
/// <param name="ExpiresAt">Nuevo vencimiento (UTC).</param>
internal sealed record WantedDetails(
    string Title,
    string CategorySlug,
    string CategoryName,
    string? Description,
    IReadOnlyList<string> Features,
    long BudgetCents,
    int Quantity,
    DateTime? NeededBy,
    DateTime ExpiresAt);

/// <summary>
/// Busqueda del tablero (HU-15): cada termino es el inicio de una palabra del producto o de la descripcion, sin
/// distinguir mayusculas ni acentos. Las solicitudes no guardan un indice de palabras: el tablero de un campus es
/// chico (5 activas por persona, RN-12) y la consulta ya va acotada por <c>ix_community_status_id</c>.
/// </summary>
internal static class WantedSearch
{
    // Mismos separadores que SearchWord (HU-09); sin \b ni \p{L}, que en PCRE dependen de UTF/UCP.
    private const string WordStart = "(?:^|[\\s,.;:!?()\\-/\"'])";

    /// <summary>Patron de un termino normalizado; solo trae letras y digitos (el servicio limpia la consulta).</summary>
    /// <param name="term">Termino en minusculas, sin acentos.</param>
    /// <returns>El patron (para usar sin distinguir mayusculas).</returns>
    public static string Pattern(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        var pattern = new StringBuilder(WordStart);
        foreach (var c in term)
        {
            pattern.Append(c switch
            {
                'a' => "[aáàäâAÁÀÄÂ]",
                'e' => "[eéèëêEÉÈËÊ]",
                'i' => "[iíìïîIÍÌÏÎ]",
                'o' => "[oóòöôOÓÒÖÔ]",
                'u' => "[uúùüûUÚÙÜÛ]",
                'n' => "[nñNÑ]",
                _ => Regex.Escape(c.ToString()),
            });
        }

        return pattern.ToString();
    }

    /// <summary>Los patrones de una busqueda (OR entre terminos).</summary>
    /// <param name="search">Terminos normalizados separados por espacio.</param>
    /// <returns>Un patron por termino.</returns>
    public static IReadOnlyList<string> Patterns(string search) =>
        [.. search.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Pattern)];
}

/// <summary>Solicitudes de compra.</summary>
internal interface IWantedRepository
{
    /// <summary>Guarda una solicitud nueva.</summary>
    /// <param name="request">La solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task InsertAsync(WantedRequestDocument request, CancellationToken cancellationToken);

    /// <summary>Una solicitud por id, o <c>null</c>.</summary>
    /// <param name="id">Id.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La solicitud o <c>null</c>.</returns>
    Task<WantedRequestDocument?> FindByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Cuantas solicitudes activas y sin vencer tiene alguien (RN-12).</summary>
    /// <param name="requesterId">Quien busca.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El numero.</returns>
    Task<long> CountActiveAsync(string requesterId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Las solicitudes de alguien, de la mas nueva a la mas vieja ("Mis solicitudes").</summary>
    /// <param name="requesterId">Quien busca.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las solicitudes.</returns>
    Task<IReadOnlyList<WantedRequestDocument>> FindByRequesterAsync(string requesterId, int limit, CancellationToken cancellationToken);

    /// <summary>Suma una oferta a <c>offersCount</c> (HU-16, <c>$inc</c>).</summary>
    /// <param name="id">La solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task IncrementOffersAsync(string id, CancellationToken cancellationToken);

    /// <summary>Una pagina del tablero "Se busca" (HU-15).</summary>
    /// <param name="query">Filtros, cursor y tamano.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las solicitudes, de la mas nueva a la mas vieja.</returns>
    Task<IReadOnlyList<WantedRequestDocument>> FindBoardAsync(WantedBoardQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Cambia los datos de una solicitud (HU-14). Solo cambia si es de quien busca, sigue activa y no ha vencido; el
    /// filtro lleva esas tres condiciones, asi que no hay que leer antes (OWASP API1).
    /// </summary>
    /// <param name="id">La solicitud.</param>
    /// <param name="requesterId">Quien busca (del token).</param>
    /// <param name="details">Datos ya validados.</param>
    /// <param name="requireNoOffers"><c>true</c> si cambian el producto o la categoria: solo sin ofertas.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La solicitud ya cambiada, o <c>null</c> si no cumple el filtro.</returns>
    Task<WantedRequestDocument?> UpdateDetailsAsync(string id, string requesterId, WantedDetails details, bool requireNoOffers, DateTime now, CancellationToken cancellationToken);

    /// <summary>Cierra o cancela una solicitud activa y sin vencer de quien busca (HU-14).</summary>
    /// <param name="id">La solicitud.</param>
    /// <param name="requesterId">Quien busca (del token).</param>
    /// <param name="status"><c>closed</c> o <c>cancelled</c>.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La solicitud ya cambiada, o <c>null</c> si no cumple el filtro.</returns>
    Task<WantedRequestDocument?> SetStatusAsync(string id, string requesterId, string status, DateTime now, CancellationToken cancellationToken);

    /// <summary>Borra las solicitudes de alguien (HU-46, R11).</summary>
    /// <param name="requesterId">Quien busca.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantas se borraron.</returns>
    Task<long> DeleteByRequesterAsync(string requesterId, CancellationToken cancellationToken);
}

/// <summary>Solicitudes en MongoDB (indices <c>ix_community_status_id</c> e <c>ix_requester_id</c>).</summary>
internal sealed class MongoWantedRepository(IMongoDatabase database) : IWantedRepository
{
    private readonly IMongoCollection<WantedRequestDocument> requests = database.GetCollection<WantedRequestDocument>(MongoSchema.WantedRequests);

    /// <inheritdoc />
    public Task InsertAsync(WantedRequestDocument request, CancellationToken cancellationToken) =>
        requests.InsertOneAsync(request, cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<WantedRequestDocument?> FindByIdAsync(string id, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await requests.Find(r => r.Id == id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public Task<long> CountActiveAsync(string requesterId, DateTime now, CancellationToken cancellationToken) =>
        requests.CountDocumentsAsync(
            r => r.RequesterId == requesterId && r.Status == WantedStatuses.Active && r.ExpiresAt > now,
            cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WantedRequestDocument>> FindByRequesterAsync(string requesterId, int limit, CancellationToken cancellationToken) =>
        await requests.Find(r => r.RequesterId == requesterId)
            .SortByDescending(r => r.Id)
            .Limit(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task IncrementOffersAsync(string id, CancellationToken cancellationToken) =>
        requests.UpdateOneAsync(r => r.Id == id, Builders<WantedRequestDocument>.Update.Inc(r => r.OffersCount, 1), cancellationToken: cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WantedRequestDocument>> FindBoardAsync(WantedBoardQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var f = Builders<WantedRequestDocument>.Filter;
        // Prefijo de ix_community_status_id (campus + estado) y orden por _id descendente.
        var filter = f.Eq(r => r.CommunityId, query.CommunityId)
            & f.Eq(r => r.Status, WantedStatuses.Active)
            & f.Gt(r => r.ExpiresAt, query.Now);
        if (query.CategorySlugs is { Count: > 0 } categories)
        {
            filter &= f.In(r => r.CategorySlug, categories);
        }

        if (query.MinBudgetCents is { } min)
        {
            filter &= f.Gte(r => r.BudgetCents, min);
        }

        if (query.MaxBudgetCents is { } max)
        {
            filter &= f.Lte(r => r.BudgetCents, max);
        }

        if (query.NeededByUntil is { } until)
        {
            filter &= f.Ne(r => r.NeededBy, null) & f.Lte(r => r.NeededBy, until);
        }

        if (query.ExcludeRequesters is { Count: > 0 } excluded)
        {
            var ids = excluded.Where(id => ObjectId.TryParse(id, out _)).ToList();
            if (ids.Count > 0)
            {
                filter &= f.Nin(r => r.RequesterId, ids);
            }
        }

        if (query.BeforeId is { } before)
        {
            filter &= f.Lt("_id", ObjectId.Parse(before));
        }

        if (query.Search is { } search)
        {
            filter &= f.Or(WantedSearch.Patterns(search).SelectMany(pattern => new[]
            {
                f.Regex(r => r.Title, new BsonRegularExpression(pattern, "i")),
                f.Regex(r => r.Description, new BsonRegularExpression(pattern, "i")),
            }));
        }

        return await requests.Find(filter)
            .SortByDescending(r => r.Id)
            .Limit(query.Limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WantedRequestDocument?> UpdateDetailsAsync(
        string id, string requesterId, WantedDetails details, bool requireNoOffers, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var filter = ActiveOf(id, requesterId, now);
        if (requireNoOffers)
        {
            // Una oferta que llega entre la lectura y aqui gana: el producto ya no cambia (HU-14).
            filter &= Builders<WantedRequestDocument>.Filter.Eq(r => r.OffersCount, 0);
        }

        var u = Builders<WantedRequestDocument>.Update;
        var update = u.Set(r => r.Title, details.Title)
            .Set(r => r.CategorySlug, details.CategorySlug)
            .Set(r => r.CategoryName, details.CategoryName)
            .Set(r => r.Features, details.Features)
            .Set(r => r.BudgetCents, details.BudgetCents)
            .Set(r => r.Quantity, details.Quantity)
            .Set(r => r.ExpiresAt, details.ExpiresAt);
        // El $jsonSchema no acepta null: lo que se borra se quita.
        update = details.Description is { } description ? update.Set(r => r.Description, description) : update.Unset(r => r.Description);
        update = details.NeededBy is { } day ? update.Set(r => r.NeededBy, day) : update.Unset(r => r.NeededBy);
        return await requests.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<WantedRequestDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WantedRequestDocument?> SetStatusAsync(string id, string requesterId, string status, DateTime now, CancellationToken cancellationToken) =>
        ObjectId.TryParse(id, out _)
            ? await requests.FindOneAndUpdateAsync(
                ActiveOf(id, requesterId, now),
                Builders<WantedRequestDocument>.Update.Set(r => r.Status, status),
                new FindOneAndUpdateOptions<WantedRequestDocument> { ReturnDocument = ReturnDocument.After },
                cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<long> DeleteByRequesterAsync(string requesterId, CancellationToken cancellationToken)
    {
        var result = await requests.DeleteManyAsync(r => r.RequesterId == requesterId, cancellationToken).ConfigureAwait(false);
        return result.DeletedCount;
    }

    // Quien busca va en el filtro de cada escritura (OWASP API1), con el estado y el vencimiento (RN-10, RN-11).
    private static FilterDefinition<WantedRequestDocument> ActiveOf(string id, string requesterId, DateTime now)
    {
        var f = Builders<WantedRequestDocument>.Filter;
        return f.Eq(r => r.Id, id)
            & f.Eq(r => r.RequesterId, requesterId)
            & f.Eq(r => r.Status, WantedStatuses.Active)
            & f.Gt(r => r.ExpiresAt, now);
    }
}

/// <summary>Solicitudes en memoria (UseInMemoryDatabase=true).</summary>
internal sealed class InMemoryWantedRepository : IWantedRepository
{
    private readonly Lock gate = new();
    private readonly List<WantedRequestDocument> requests = [];

    /// <inheritdoc />
    public Task InsertAsync(WantedRequestDocument request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            requests.Add(request);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<WantedRequestDocument?> FindByIdAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(requests.Find(r => string.Equals(r.Id, id, StringComparison.Ordinal)));
        }
    }

    /// <inheritdoc />
    public Task<long> CountActiveAsync(string requesterId, DateTime now, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)requests.Count(r =>
                string.Equals(r.RequesterId, requesterId, StringComparison.Ordinal) && r.Status == WantedStatuses.Active && r.ExpiresAt > now));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WantedRequestDocument>> FindByRequesterAsync(string requesterId, int limit, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<WantedRequestDocument>>(requests
                .Where(r => string.Equals(r.RequesterId, requesterId, StringComparison.Ordinal))
                .OrderByDescending(r => r.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task IncrementOffersAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var i = requests.FindIndex(r => string.Equals(r.Id, id, StringComparison.Ordinal));
            if (i >= 0)
            {
                var r = requests[i];
                requests[i] = Copy(r, Details(r), r.Status, r.OffersCount + 1);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<WantedRequestDocument?> UpdateDetailsAsync(
        string id, string requesterId, WantedDetails details, bool requireNoOffers, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(details);
        return Task.FromResult(Replace(
            id,
            r => IsActiveOf(r, requesterId, now) && (!requireNoOffers || r.OffersCount == 0),
            r => Copy(r, details, r.Status, r.OffersCount)));
    }

    /// <inheritdoc />
    public Task<WantedRequestDocument?> SetStatusAsync(string id, string requesterId, string status, DateTime now, CancellationToken cancellationToken) =>
        Task.FromResult(Replace(id, r => IsActiveOf(r, requesterId, now), r => Copy(r, Details(r), status, r.OffersCount)));

    /// <inheritdoc />
    public Task<IReadOnlyList<WantedRequestDocument>> FindBoardAsync(WantedBoardQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var patterns = query.Search is { } search
            ? WantedSearch.Patterns(search)
                .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                .ToList()
            : null;
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<WantedRequestDocument>>(requests
                .Where(r => string.Equals(r.CommunityId, query.CommunityId, StringComparison.Ordinal)
                    && r.Status == WantedStatuses.Active
                    && r.ExpiresAt > query.Now
                    && (query.CategorySlugs is not { Count: > 0 } || query.CategorySlugs.Contains(r.CategorySlug, StringComparer.Ordinal))
                    && (query.MinBudgetCents is not { } min || r.BudgetCents >= min)
                    && (query.MaxBudgetCents is not { } max || r.BudgetCents <= max)
                    && (query.NeededByUntil is not { } until || (r.NeededBy is { } day && day <= until))
                    && (query.ExcludeRequesters is null || !query.ExcludeRequesters.Contains(r.RequesterId, StringComparer.Ordinal))
                    && (query.BeforeId is null || string.CompareOrdinal(r.Id, query.BeforeId) < 0)
                    && (patterns is null || patterns.Exists(p => p.IsMatch(r.Title) || (r.Description is { } d && p.IsMatch(d)))))
                .OrderByDescending(r => r.Id, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToList());
        }
    }

    /// <inheritdoc />
    public Task<long> DeleteByRequesterAsync(string requesterId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult((long)requests.RemoveAll(r => string.Equals(r.RequesterId, requesterId, StringComparison.Ordinal)));
        }
    }

    private static bool IsActiveOf(WantedRequestDocument r, string requesterId, DateTime now) =>
        string.Equals(r.RequesterId, requesterId, StringComparison.Ordinal) && r.Status == WantedStatuses.Active && r.ExpiresAt > now;

    private static WantedDetails Details(WantedRequestDocument r) =>
        new(r.Title, r.CategorySlug, r.CategoryName, r.Description, r.Features, r.BudgetCents, r.Quantity, r.NeededBy, r.ExpiresAt);

    // Los documentos son inmutables (init): un cambio reemplaza el documento, como lo devuelve Mongo.
    private static WantedRequestDocument Copy(WantedRequestDocument r, WantedDetails d, string status, int offersCount) => new()
    {
        Id = r.Id,
        CommunityId = r.CommunityId,
        RequesterId = r.RequesterId,
        RequesterName = r.RequesterName,
        Title = d.Title,
        CategorySlug = d.CategorySlug,
        CategoryName = d.CategoryName,
        Description = d.Description,
        Features = d.Features,
        BudgetCents = d.BudgetCents,
        Quantity = d.Quantity,
        NeededBy = d.NeededBy,
        ExpiresAt = d.ExpiresAt,
        Status = status,
        OffersCount = offersCount,
        CreatedAt = r.CreatedAt,
        SchemaVersion = r.SchemaVersion,
    };

    private WantedRequestDocument? Replace(string id, Func<WantedRequestDocument, bool> when, Func<WantedRequestDocument, WantedRequestDocument> change)
    {
        lock (gate)
        {
            var i = requests.FindIndex(r => string.Equals(r.Id, id, StringComparison.Ordinal));
            if (i < 0 || !when(requests[i]))
            {
                return null;
            }

            requests[i] = change(requests[i]);
            return requests[i];
        }
    }
}
