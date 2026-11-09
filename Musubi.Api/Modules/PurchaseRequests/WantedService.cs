using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Caching;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>
/// Publicar lo que necesito y "Mis solicitudes" (HU-13, RF-16, RF-22). Reglas: RN-09 (producto, categoria,
/// presupuesto y cantidad obligatorios), RN-10 (vence en su fecha o a los 30 dias), RN-12 (maximo de activas) y
/// 02-07 (el texto se modera antes de guardarlo). Tablero "Se busca" del campus (HU-15, RF-19).
/// NOTE (HU-14): editar, cerrar y cancelar. NOTE (HU-18): avisar a quien tenga algo parecido ("Coincide con tu ...").
/// </summary>
internal sealed class WantedService(
    IWantedRepository requests,
    IMemberDirectory members,
    ICategoryDirectory categories,
    IBlockDirectory blocks,
    IProfileDirectory profiles,
    IContentModerator moderator,
    IOutputCacheStore outputCache,
    IOptions<CatalogOptions> catalog,
    TimeProvider time)
{
    /// <summary>Solicitudes por pagina del tablero si no se pide otro tamano (como la vitrina).</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Tamano maximo de pagina del tablero.</summary>
    public const int MaxPageSize = 50;

    /// <summary>Largo maximo de la busqueda del tablero.</summary>
    public const int MaxSearchLength = 80;

    /// <summary>Terminos de busqueda como maximo (acota el costo de los patrones, OWASP API4).</summary>
    public const int MaxSearchTerms = 5;

    /// <summary>Solicitudes activas por persona (RN-12).</summary>
    public const int MaxActive = 5;

    /// <summary>Dias que dura una solicitud sin fecha (RN-10).</summary>
    public const int DefaultDays = 30;

    /// <summary>Hasta cuantos dias adelante se puede pedir.</summary>
    public const int MaxDaysAhead = 60;

    /// <summary>Largo minimo y maximo del producto.</summary>
    public const int MinTitle = 3;

    /// <summary>Largo maximo del producto.</summary>
    public const int MaxTitle = 80;

    /// <summary>Largo maximo de la descripcion (maqueta "0 / 300").</summary>
    public const int MaxDescription = 300;

    /// <summary>Caracteristicas como maximo.</summary>
    public const int MaxFeatures = 5;

    /// <summary>Largo maximo de una caracteristica.</summary>
    public const int MaxFeature = 40;

    /// <summary>Piezas como maximo.</summary>
    public const int MaxQuantity = 10;

    /// <summary>Presupuesto maximo: $999,999 (como el precio de una publicacion).</summary>
    public const long MaxBudgetCents = ListingService.MaxPriceCents;

    /// <summary>"Otro" de la maqueta: una necesidad que no cabe en las categorias del campus.</summary>
    public const string OtherCategory = "otro";

    /// <summary>
    /// El campus esta en el centro de Mexico: UTC-6 durante el ano (sin horario de verano desde 2022). Un dia del
    /// campus termina a las 06:00 UTC del dia siguiente.
    /// </summary>
    private static readonly TimeSpan CampusOffset = TimeSpan.FromHours(-6);

    /// <summary>Publica una solicitud activa.</summary>
    /// <param name="userId">Quien busca (del token).</param>
    /// <param name="request">Lo que necesita.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La solicitud o el error.</returns>
    public async Task<Result<WantedResponse>> CreateAsync(string userId, CreateWantedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length is < MinTitle or > MaxTitle)
        {
            return WantedErrors.InvalidTitle;
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is { Length: > MaxDescription })
        {
            return WantedErrors.InvalidDescription;
        }

        var features = (request.Features ?? []).Select(f => (f ?? string.Empty).Trim()).ToList();
        if (features.Count > MaxFeatures || features.Exists(f => f.Length is 0 or > MaxFeature))
        {
            return WantedErrors.InvalidFeatures;
        }

        if (request.BudgetCents is not (>= 100 and <= MaxBudgetCents))
        {
            return WantedErrors.InvalidBudget;
        }

        if (request.Quantity is not (>= 1 and <= MaxQuantity))
        {
            return WantedErrors.InvalidQuantity;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now + CampusOffset);
        if (request.NeededBy is { } day && (day < today || day > today.AddDays(MaxDaysAhead)))
        {
            return WantedErrors.InvalidDate;
        }

        CategorySummary? category = null;
        if (string.Equals(request.Category, OtherCategory, StringComparison.Ordinal))
        {
            category = new CategorySummary(OtherCategory, "Otro");
        }
        else if (!string.IsNullOrEmpty(request.Category))
        {
            category = await categories.FindAsync(member.CommunityId, request.Category, cancellationToken).ConfigureAwait(false);
        }

        if (category is null)
        {
            return WantedErrors.InvalidCategory;
        }

        // RN-12: se cuenta antes de moderar, que cuesta una llamada a la nube si el proveedor es AWS.
        if (await requests.CountActiveAsync(member.Id, now, cancellationToken).ConfigureAwait(false) >= MaxActive)
        {
            return WantedErrors.TooManyActive;
        }

        // 02-07: el texto se modera antes de guardarlo.
        var verdicts = await moderator.CheckTextsAsync([title, description ?? string.Empty, string.Join(" · ", features)], cancellationToken)
            .ConfigureAwait(false);
        var rejected = ModerationErrors.For(verdicts[0], ModerationErrors.TextRejected("el producto que buscas"))
            ?? ModerationErrors.For(verdicts[1], ModerationErrors.TextRejected("la descripción"))
            ?? ModerationErrors.For(verdicts[2], ModerationErrors.TextRejected("las características"));
        if (rejected is not null)
        {
            return rejected;
        }

        var neededBy = request.NeededBy?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var document = new WantedRequestDocument
        {
            Id = MongoConventions.NewId(),
            CommunityId = member.CommunityId,
            RequesterId = member.Id,
            RequesterName = ListingService.ShortName(member.FullName),
            Title = title,
            CategorySlug = category.Slug,
            CategoryName = category.Name,
            Description = description,
            Features = features,
            BudgetCents = request.BudgetCents.Value,
            Quantity = request.Quantity.Value,
            NeededBy = neededBy,
            // RN-10: al terminar el dia elegido en el campus, o a los 30 dias.
            ExpiresAt = neededBy is { } d ? d.AddDays(1) - CampusOffset : now.AddDays(DefaultDays),
            CreatedAt = now,
        };
        await requests.InsertAsync(document, cancellationToken).ConfigureAwait(false);
        // El tablero del visitante vive en la cache de salida (HU-15): una solicitud nueva se ve al instante.
        await outputCache.EvictByTagAsync(CachingServiceCollectionExtensions.WantedBoardTag, cancellationToken).ConfigureAwait(false);
        return Result<WantedResponse>.Ok(ToResponse(document, now));
    }

    /// <summary>"Mis solicitudes": todas las mias, con su estado de ahora.</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La lista.</returns>
    public async Task<WantedListResponse> MineAsync(string userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var mine = await requests.FindByRequesterAsync(userId, 100, cancellationToken).ConfigureAwait(false);
        return new WantedListResponse(mine.Select(r => ToResponse(r, now)).ToList());
    }

    /// <summary>
    /// Una solicitud. Su dueno la ve siempre; los demas, solo si es de su campus, sigue activa y no hay un bloqueo
    /// entre los dos (HU-57).
    /// </summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="communityId">Su comunidad (del token).</param>
    /// <param name="id">Id de la solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La solicitud o <c>wanted.not_found</c>.</returns>
    public async Task<Result<WantedResponse>> GetAsync(string userId, string? communityId, string id, CancellationToken cancellationToken)
    {
        var request = string.IsNullOrEmpty(id) ? null : await requests.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return WantedErrors.NotFound;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var response = ToResponse(request, now);
        var owner = string.Equals(request.RequesterId, userId, StringComparison.Ordinal);
        if (!owner
            && (!string.Equals(request.CommunityId, communityId, StringComparison.Ordinal)
                || response.Status != WantedStatuses.Active
                || await blocks.BetweenAsync(userId, request.RequesterId, cancellationToken).ConfigureAwait(false)))
        {
            return WantedErrors.NotFound;
        }

        return Result<WantedResponse>.Ok(response);
    }

    /// <summary>
    /// Tablero "Se busca" del campus (HU-15, RF-19): solo activas y sin vencer, de la mas nueva a la mas vieja. Con
    /// sesion, el campus del token, sin las propias (RN-06) ni las de quien bloqueaste o te bloqueo (HU-57), y con el
    /// nombre completo, la foto y la carrera visible de quien busca. Un visitante ve el campus que pide (o el del
    /// piloto) y solo el nombre corto (RNF-05).
    /// </summary>
    /// <param name="viewer">Quien mira (del token) o un visitante.</param>
    /// <param name="filter">Filtros de la maqueta y pagina.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La pagina o el error de validacion.</returns>
    public async Task<Result<WantedPage>> BoardAsync(ShowcaseViewer viewer, WantedBoardFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.MinBudgetCents is < 0 or > MaxBudgetCents
            || filter.MaxBudgetCents is < 0 or > MaxBudgetCents
            || filter.MinBudgetCents > filter.MaxBudgetCents)
        {
            return WantedErrors.InvalidBudgetRange;
        }

        if (filter.NeededWithinDays is < 1 or > MaxDaysAhead)
        {
            return WantedErrors.InvalidDeadline;
        }

        if (!string.IsNullOrWhiteSpace(filter.Cursor) && !ObjectId.TryParse(filter.Cursor, out _))
        {
            return WantedErrors.InvalidCursor;
        }

        string? search = null;
        var trimmed = filter.Q?.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            if (trimmed.Length > MaxSearchLength || !trimmed.Any(char.IsLetterOrDigit))
            {
                return WantedErrors.InvalidQuery;
            }

            search = SearchTerms(trimmed);
        }

        var communityId = viewer.CommunityId ?? await FindCampusAsync(filter.Campus, cancellationToken).ConfigureAwait(false);
        if (communityId is null)
        {
            return ListingsErrors.UnknownCampus;
        }

        // RN-06 y HU-57: con sesion no salen las propias ni las de quien bloqueaste o te bloqueo.
        List<string>? excluded = null;
        if (viewer.UserId is { } userId)
        {
            excluded = [userId, .. await blocks.HiddenForAsync(userId, cancellationToken).ConfigureAwait(false)];
        }

        var now = time.GetUtcNow().UtcDateTime;
        DateTime? until = filter.NeededWithinDays is { } days
            ? DateOnly.FromDateTime(now + CampusOffset).AddDays(days).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : null;
        var slugs = (filter.Category ?? [])
            .Select(slug => slug.Trim())
            .Where(slug => slug.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var size = Math.Clamp(filter.Limit ?? DefaultPageSize, 1, MaxPageSize);
        var page = await requests.FindBoardAsync(
            new WantedBoardQuery(
                communityId,
                now,
                slugs.Length == 0 ? null : slugs,
                filter.MinBudgetCents,
                filter.MaxBudgetCents,
                until,
                search,
                excluded,
                string.IsNullOrWhiteSpace(filter.Cursor) ? null : filter.Cursor,
                size),
            cancellationToken).ConfigureAwait(false);

        var people = page.Select(r => r.RequesterId).Distinct(StringComparer.Ordinal).ToList();
        IReadOnlyDictionary<string, MemberSummary> names = new Dictionary<string, MemberSummary>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, ProfileCard> cards = new Dictionary<string, ProfileCard>(StringComparer.Ordinal);
        if (viewer.UserId is not null && people.Count > 0)
        {
            names = await members.FindMembersAsync(people, cancellationToken).ConfigureAwait(false);
            cards = await profiles.FindCardsAsync(communityId, people, cancellationToken).ConfigureAwait(false);
        }

        var signedIn = viewer.UserId is not null;
        var items = page.Select(r => ToResponse(r, now, Requester(r, signedIn, names, cards))).ToList();
        return Result<WantedPage>.Ok(new WantedPage(items, page.Count == size ? page[^1].Id : null));
    }

    /// <summary>Quien busca en el tablero: el visitante solo ve el nombre corto (RNF-05).</summary>
    private static WantedRequesterResponse Requester(
        WantedRequestDocument r,
        bool signedIn,
        IReadOnlyDictionary<string, MemberSummary> names,
        IReadOnlyDictionary<string, ProfileCard> cards)
    {
        if (!signedIn)
        {
            return new WantedRequesterResponse(null, r.RequesterName);
        }

        var name = names.TryGetValue(r.RequesterId, out var member) ? member.FullName : r.RequesterName;
        var card = cards.GetValueOrDefault(r.RequesterId);
        return new WantedRequesterResponse(r.RequesterId, name, card?.Photo, card?.Career);
    }

    /// <summary>El estado de ahora: una activa que paso su fecha esta vencida (RN-10).</summary>
    /// <param name="request">La solicitud.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <returns>El estado.</returns>
    public static string EffectiveStatus(WantedRequestDocument request, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Status == WantedStatuses.Active && request.ExpiresAt <= now ? WantedStatuses.Expired : request.Status;
    }

    /// <summary>
    /// Terminos seguros para los patrones: solo letras y digitos, normalizados como la busqueda de productos (HU-09) y
    /// como maximo <see cref="MaxSearchTerms"/>. Nada de lo escrito se vuelve sintaxis (OWASP API3, API4).
    /// </summary>
    private static string SearchTerms(string query) =>
        string.Join(' ', query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => SearchWord.Normalize(string.Concat(word.Where(char.IsLetterOrDigit))))
            .Where(word => word.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSearchTerms));

    private async Task<string?> FindCampusAsync(string? campus, CancellationToken cancellationToken)
    {
        var slug = string.IsNullOrWhiteSpace(campus) ? catalog.Value.DefaultCampus : campus.Trim().ToLowerInvariant();
        return await members.FindCommunityIdAsync(slug, cancellationToken).ConfigureAwait(false);
    }

    private static WantedResponse ToResponse(WantedRequestDocument r, DateTime now) =>
        ToResponse(r, now, new WantedRequesterResponse(r.RequesterId, r.RequesterName));

    private static WantedResponse ToResponse(WantedRequestDocument r, DateTime now, WantedRequesterResponse requester) => new(
        r.Id,
        r.Title,
        new WantedCategoryResponse(r.CategorySlug, r.CategoryName),
        r.Description,
        r.Features,
        r.BudgetCents,
        r.Quantity,
        r.NeededBy is { } d ? DateOnly.FromDateTime(d) : null,
        r.ExpiresAt,
        EffectiveStatus(r, now),
        r.OffersCount,
        requester,
        r.CreatedAt);
}
