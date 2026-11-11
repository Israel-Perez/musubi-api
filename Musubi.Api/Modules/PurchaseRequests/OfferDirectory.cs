using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.PurchaseRequests.Data;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>Lo que otro modulo necesita saber de una oferta (HU-23: el pedido sale de una oferta aceptada).</summary>
/// <param name="Id">Id de la oferta.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="RequesterId">Quien busca (compra).</param>
/// <param name="SellerId">Quien ofrece (vende).</param>
/// <param name="ProductId">La publicacion vinculada, o <c>null</c> en una oferta directa.</param>
/// <param name="Title">Lo que se ofrece.</param>
/// <param name="PriceCents">Precio en centavos.</param>
/// <param name="Photo">Ruta de la foto (<c>/media/products/...</c>), o <c>null</c>.</param>
/// <param name="Accepted">Quien busca la acepto (HU-17).</param>
/// <param name="RequestQuantity">Las piezas que pidio la solicitud.</param>
internal sealed record OfferSummary(
    string Id,
    string CommunityId,
    string RequesterId,
    string SellerId,
    string? ProductId,
    string Title,
    long PriceCents,
    string? Photo,
    bool Accepted,
    int RequestQuantity);

/// <summary>Contrato publico de M-05 para los demas modulos (Modules/README: un modulo no lee las colecciones de otro).</summary>
internal interface IOfferDirectory
{
    /// <summary>Una oferta por id.</summary>
    /// <param name="offerId">Id de la oferta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El resumen o <c>null</c> si no existe.</returns>
    Task<OfferSummary?> FindAsync(string offerId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre los repositorios de M-05.</summary>
internal sealed class OfferDirectory(IOfferRepository offers, IWantedRepository requests) : IOfferDirectory
{
    /// <inheritdoc />
    public async Task<OfferSummary?> FindAsync(string offerId, CancellationToken cancellationToken)
    {
        var offer = await offers.FindByIdAsync(offerId, cancellationToken).ConfigureAwait(false);
        if (offer is null)
        {
            return null;
        }

        var request = await requests.FindByIdAsync(offer.RequestId, cancellationToken).ConfigureAwait(false);
        return new OfferSummary(
            offer.Id,
            offer.CommunityId,
            offer.RequesterId,
            offer.SellerId,
            offer.ProductId,
            offer.Title,
            offer.PriceCents,
            offer.PhotoKey is null ? null : ListingService.PhotoUrl(offer.PhotoKey),
            offer.Status == OfferStatuses.Accepted,
            request?.Quantity ?? 1);
    }
}

/// <summary>Una solicitud que esta por vencer o que ya vencio (HU-31).</summary>
/// <param name="Id">Id.</param>
/// <param name="RequesterId">Quien busca.</param>
/// <param name="Title">Lo que busca.</param>
/// <param name="ExpiresAt">Vencimiento (UTC).</param>
internal sealed record WantedReminder(string Id, string RequesterId, string Title, DateTime ExpiresAt);

/// <summary>Contrato publico de M-05 para los avisos (HU-31): las solicitudes activas que vencen en una ventana.</summary>
internal interface IWantedReminders
{
    /// <summary>Las activas que vencen en [<paramref name="from"/>, <paramref name="to"/>).</summary>
    /// <param name="from">Desde (UTC).</param>
    /// <param name="to">Hasta (UTC).</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las solicitudes.</returns>
    Task<IReadOnlyList<WantedReminder>> ExpiringAsync(DateTime from, DateTime to, int limit, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de solicitudes.</summary>
internal sealed class WantedReminders(IWantedRepository requests) : IWantedReminders
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<WantedReminder>> ExpiringAsync(DateTime from, DateTime to, int limit, CancellationToken cancellationToken) =>
        (await requests.FindExpiringAsync(from, to, limit, cancellationToken).ConfigureAwait(false))
            .Select(r => new WantedReminder(r.Id, r.RequesterId, r.Title, r.ExpiresAt))
            .ToList();
}
