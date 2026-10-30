using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Media;

namespace Musubi.Api.Modules.Listings;

/// <summary>
/// Borra las publicaciones y las fotos de una cuenta que se elimina (HU-46, R11). Las fotos se
/// borran por prefijo: tambien las que se subieron y nunca se publicaron.
/// </summary>
internal sealed class ListingAccountEraser(IProductRepository products, IMediaStorage media) : IAccountDataEraser
{
    /// <inheritdoc />
    public async Task EraseAsync(string userId, CancellationToken cancellationToken)
    {
        await products.DeleteBySellerAsync(userId, cancellationToken).ConfigureAwait(false);
        await media.DeletePrefixAsync(ListingService.PhotoPrefix(userId), cancellationToken).ConfigureAwait(false);
    }
}
