using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Errors;
using Musubi.Api.Platform.Media;
using Musubi.Api.Platform.Moderation;

namespace Musubi.Api.Modules.Listings;

/// <summary>
/// HU-59: "Llenar con IA" en <c>Vender-1</c>. Con una foto que esta persona ya subio (moderada y sin metadatos, 02-07),
/// la IA propone el nombre, la categoria del campus, el estado y la descripcion. El texto propuesto tambien se modera.
/// No guarda nada: la app lo pone en el borrador y quien vende lo revisa antes de publicar (RN-38). Sin IA, 503 y se
/// llena a mano (RNF-15).
/// </summary>
internal sealed class PhotoDraftService(
    IMemberDirectory members,
    ICategoryRepository categories,
    IMediaStorage media,
    IContentModerator moderator,
    IPhotoDraftWriter writer)
{
    /// <summary>Propone la publicacion de una foto.</summary>
    /// <param name="userId">Usuario (del token).</param>
    /// <param name="request">La llave de la foto.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La propuesta o el error.</returns>
    public async Task<Result<PhotoDraftResponse>> DraftAsync(string userId, PhotoDraftRequest request, CancellationToken cancellationToken)
    {
        var member = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (member is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        // Solo una foto que esta misma persona subio (la llave lleva su id, OWASP API1).
        var key = request?.Photo?.Trim();
        if (!ListingService.IsOwnPhotoKey(userId, key))
        {
            return ListingsErrors.PhotoNotFound;
        }

        // Sin proveedor no se lee la foto ni sale nada del servidor.
        if (!writer.Enabled)
        {
            return ListingsErrors.DraftUnavailable;
        }

        if (await media.GetAsync(key!, cancellationToken).ConfigureAwait(false) is not { } photo)
        {
            return ListingsErrors.PhotoNotFound;
        }

        var active = await categories.ListActiveAsync(member.CommunityId, cancellationToken).ConfigureAwait(false);
        var prompt = new PhotoDraftPrompt(photo, [.. active.Select(c => new DraftCategory(c.Slug, c.Name))]);
        if (await writer.SuggestAsync(prompt, cancellationToken).ConfigureAwait(false) is not { } draft)
        {
            return ListingsErrors.DraftUnavailable;
        }

        // 02-07: lo que propone la IA se modera como si lo hubiera escrito quien vende; si no pasa, se llena a mano.
        var verdicts = await moderator.CheckTextsAsync([draft.Title, draft.Description], cancellationToken).ConfigureAwait(false);
        if (verdicts.Any(v => v != ModerationVerdict.Allowed))
        {
            return ListingsErrors.DraftUnavailable;
        }

        return Result<PhotoDraftResponse>.Ok(new PhotoDraftResponse(draft.Title, draft.Category, draft.Condition, draft.ConditionDetail, draft.Description));
    }
}
