using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Media;

namespace Musubi.Api.Modules.Listings;

/// <summary>
/// Sube una foto de producto (HU-06, RNF-13): la normaliza con <see cref="PhotoProcessor"/> y la guarda en
/// S3 con una llave nueva del usuario. La app la reduce antes de enviarla; aqui se vuelve a codificar para
/// quitar metadatos (GPS) y no confiar en lo que llega.
/// </summary>
internal sealed class PhotoUploadService(IMediaStorage media)
{
    /// <summary>Tamano maximo del archivo subido (10 MB, igual que el limite de Kestrel).</summary>
    public const long MaxBytes = 10 * 1024 * 1024;

    /// <summary>Procesa y guarda la foto.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <param name="file">Archivo del formulario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La foto o el error.</returns>
    public async Task<Result<PhotoResponse>> UploadAsync(string userId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return ListingsErrors.PhotoUnsupported;
        }

        if (file.Length > MaxBytes)
        {
            return ListingsErrors.PhotoTooLarge;
        }

        byte[] bytes;
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await file.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            bytes = buffer.ToArray();
        }

        var rejection = PhotoProcessor.TryProcess(bytes, out var photo);
        if (rejection is not null || photo is null)
        {
            return rejection switch
            {
                PhotoRejection.TooSmall => ListingsErrors.PhotoTooSmall,
                PhotoRejection.TooLarge => ListingsErrors.PhotoTooLarge,
                _ => ListingsErrors.PhotoUnsupported,
            };
        }

        var key = ListingService.NewPhotoKey(userId);
        await media.PutAsync(key, photo.Content, PhotoProcessor.ContentType, cancellationToken).ConfigureAwait(false);
        return Result<PhotoResponse>.Ok(new PhotoResponse(key, ListingService.PhotoUrl(key)));
    }
}
