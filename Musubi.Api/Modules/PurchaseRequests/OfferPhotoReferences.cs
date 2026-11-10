using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.PurchaseRequests.Data;

namespace Musubi.Api.Modules.PurchaseRequests;

/// <summary>
/// Una oferta guarda la llave de la portada de la publicacion que vincula (HU-16): si la publicacion se elimina
/// (HU-07), esa foto se queda en S3 para que la oferta la siga mostrando.
/// </summary>
internal sealed class OfferPhotoReferences(IOfferRepository offers) : IListingPhotoReferences
{
    /// <inheritdoc />
    public Task<IReadOnlyCollection<string>> InUseAsync(string sellerId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        offers.FindPhotoKeysInUseAsync(sellerId, keys, cancellationToken);
}
