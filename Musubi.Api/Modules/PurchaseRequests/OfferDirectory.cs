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
