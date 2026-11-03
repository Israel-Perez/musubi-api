using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.Profiles;

/// <summary>
/// Foto de perfil (HU-04, RNF-05, RNF-13): el mismo procesamiento que las fotos de productos (sin metadatos,
/// WebP), recortada cuadrada a 512 px. Al reemplazarla o quitarla se borra la anterior de S3 (R11).
/// Se modera antes de guardarla (02-07).
/// </summary>
internal sealed class ProfilePhotoService(
    ProfileService service,
    IProfileRepository profiles,
    ICareerRepository careers,
    IMediaStorage media,
    IContentModerator moderator,
    TimeProvider time)
{
    /// <summary>Sube o reemplaza la foto.</summary>
    /// <param name="userId">Id del usuario (del token).</param>
    /// <param name="file">Archivo del formulario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil o el error.</returns>
    public async Task<Result<MyProfileResponse>> UploadAsync(string userId, IFormFile? file, CancellationToken cancellationToken)
    {
        var account = await service.ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var read = await PhotoUploadService.ReadAsync(file, square: true, cancellationToken).ConfigureAwait(false);
        if (read.Error is { } error)
        {
            return error;
        }

        // 02-07: se modera antes de guardar; una foto rechazada nunca llega a S3.
        var verdict = await moderator.CheckImageAsync(read.Value!.ModerationCopy, cancellationToken).ConfigureAwait(false);
        if (ModerationErrors.For(verdict, ModerationErrors.ImageRejected) is { } rejected)
        {
            return rejected;
        }

        var key = ProfileService.NewAvatarKey(account.Id);
        await media.PutAsync(key, read.Value.Content, PhotoProcessor.ContentType, cancellationToken).ConfigureAwait(false);
        var previous = await profiles.SetAvatarAsync(ProfileService.Owner(account), key, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (previous is not null)
        {
            await media.DeleteAsync(previous, cancellationToken).ConfigureAwait(false);
        }

        var profile = await profiles.FindAsync(account.Id, cancellationToken).ConfigureAwait(false);
        var list = await careers.ListActiveAsync(account.CommunityId, cancellationToken).ConfigureAwait(false);
        return Result<MyProfileResponse>.Ok(ProfileService.ToMine(account, profile, list));
    }

    /// <summary>Quita la foto. Idempotente: sin foto tambien responde exito.</summary>
    /// <param name="userId">Id del usuario (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Exito o el error.</returns>
    public async Task<Result<bool>> RemoveAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await service.ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var profile = await profiles.FindAsync(account.Id, cancellationToken).ConfigureAwait(false);
        if (profile?.AvatarKey is null)
        {
            return Result<bool>.Ok(true);
        }

        var previous = await profiles.SetAvatarAsync(ProfileService.Owner(account), null, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        if (previous is not null)
        {
            await media.DeleteAsync(previous, cancellationToken).ConfigureAwait(false);
        }

        return Result<bool>.Ok(true);
    }
}
