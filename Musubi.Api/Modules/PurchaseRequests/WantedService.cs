using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>
/// Publicar lo que necesito y "Mis solicitudes" (HU-13, RF-16, RF-22). Reglas: RN-09 (producto, categoria,
/// presupuesto y cantidad obligatorios), RN-10 (vence en su fecha o a los 30 dias), RN-12 (maximo de activas) y
/// 02-07 (el texto se modera antes de guardarlo).
/// NOTE (HU-14): editar, cerrar y cancelar. NOTE (HU-15): el tablero "Se busca" del campus.
/// NOTE (HU-18): avisar a quien tenga algo parecido.
/// </summary>
internal sealed class WantedService(
    IWantedRepository requests,
    IMemberDirectory members,
    ICategoryDirectory categories,
    IBlockDirectory blocks,
    IContentModerator moderator,
    TimeProvider time)
{
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

    /// <summary>El estado de ahora: una activa que paso su fecha esta vencida (RN-10).</summary>
    /// <param name="request">La solicitud.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <returns>El estado.</returns>
    public static string EffectiveStatus(WantedRequestDocument request, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Status == WantedStatuses.Active && request.ExpiresAt <= now ? WantedStatuses.Expired : request.Status;
    }

    private static WantedResponse ToResponse(WantedRequestDocument r, DateTime now) => new(
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
        new WantedRequesterResponse(r.RequesterId, r.RequesterName),
        r.CreatedAt);
}
