using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.PurchaseRequests.Data;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>Una solicitud, con lo necesario para buscarle coincidencias (HU-18, HU-29).</summary>
/// <param name="Id">Solicitud.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="RequesterId">Quien busca.</param>
/// <param name="Title">Lo que busca.</param>
/// <param name="CategorySlug">Categoria, o <c>null</c> si es "otro" (cualquiera).</param>
/// <param name="BudgetCents">Presupuesto.</param>
/// <param name="Active">Si sigue activa y sin vencer.</param>
/// <param name="RequesterName">Nombre corto de quien busca ("Ana R.", HU-38: "Quienes la buscan").</param>
/// <param name="ExpiresAt">Vencimiento (UTC).</param>
internal sealed record WantedForMatch(
    string Id,
    string CommunityId,
    string RequesterId,
    string Title,
    string? CategorySlug,
    long BudgetCents,
    bool Active,
    string RequesterName = "",
    DateTime ExpiresAt = default);

/// <summary>Contrato publico de M-05 para M-06 (HU-18, HU-29).</summary>
internal interface IWantedMatchDirectory
{
    /// <summary>Una solicitud, solo para quien la publico (RNF-03, OWASP API1).</summary>
    /// <param name="requesterId">Quien consulta (del token).</param>
    /// <param name="requestId">La solicitud.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La solicitud, o <c>null</c> si no existe o es de otra persona.</returns>
    Task<WantedForMatch?> FindOwnAsync(string requesterId, string requestId, CancellationToken cancellationToken);

    /// <summary>
    /// Las solicitudes activas del campus que podrian servirle a una publicacion nueva (HU-29): de su categoria o de
    /// "otro" y con presupuesto de al menos <paramref name="minBudgetCents"/>.
    /// </summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="categorySlug">Categoria de la publicacion.</param>
    /// <param name="minBudgetCents">Presupuesto minimo.</param>
    /// <param name="excludedRequesters">Quien vende y las personas bloqueadas en cualquier sentido (RN-31, HU-57).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las solicitudes, de la mas nueva a la mas vieja.</returns>
    Task<IReadOnlyList<WantedForMatch>> ActiveCandidatesAsync(
        string communityId,
        string categorySlug,
        long minBudgetCents,
        IReadOnlyCollection<string> excludedRequesters,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de solicitudes (la misma consulta del tablero, HU-15).</summary>
internal sealed class WantedMatchDirectory(IWantedRepository requests, TimeProvider time) : IWantedMatchDirectory
{
    /// <inheritdoc />
    public async Task<WantedForMatch?> FindOwnAsync(string requesterId, string requestId, CancellationToken cancellationToken)
    {
        var request = await requests.FindByIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        return request is null || !string.Equals(request.RequesterId, requesterId, StringComparison.Ordinal)
            ? null
            : ToMatch(request, time.GetUtcNow().UtcDateTime);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WantedForMatch>> ActiveCandidatesAsync(
        string communityId,
        string categorySlug,
        long minBudgetCents,
        IReadOnlyCollection<string> excludedRequesters,
        int limit,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var found = await requests.FindBoardAsync(
            new WantedBoardQuery(
                communityId,
                now,
                [categorySlug, WantedService.OtherCategory],
                minBudgetCents,
                null,
                null,
                null,
                excludedRequesters,
                null,
                limit),
            cancellationToken).ConfigureAwait(false);
        return [.. found.Select(request => ToMatch(request, now))];
    }

    private static WantedForMatch ToMatch(WantedRequestDocument request, DateTime now) => new(
        request.Id,
        request.CommunityId,
        request.RequesterId,
        request.Title,
        request.CategorySlug == WantedService.OtherCategory ? null : request.CategorySlug,
        request.BudgetCents,
        request.Status == WantedStatuses.Active && request.ExpiresAt > now,
        request.RequesterName,
        request.ExpiresAt);
}
