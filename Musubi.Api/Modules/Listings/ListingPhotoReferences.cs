using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Musubi.Api.Modules.Listings;

/// <summary>
/// Fotos de publicaciones que otro modulo sigue mostrando (HU-07): al eliminar una publicacion, esas fotos no se
/// borran de S3. Hoy lo implementan las ofertas (HU-16 copia la llave de la portada). Cada modulo responde por sus
/// datos: Listings no lee colecciones ajenas (Modules/README).
/// </summary>
internal interface IListingPhotoReferences
{
    /// <summary>Cuales de estas llaves usa todavia algo del vendedor.</summary>
    /// <param name="sellerId">Vendedor de la publicacion.</param>
    /// <param name="keys">Llaves de sus fotos.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las llaves en uso (un subconjunto de <paramref name="keys"/>).</returns>
    Task<IReadOnlyCollection<string>> InUseAsync(string sellerId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}
