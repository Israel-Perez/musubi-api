using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Profiles;

/// <summary>
/// Intereses de la cuenta (HU-66): categorias del campus que eligio al entrar por primera vez o en Ajustes.
/// Viven en <c>preferences</c>, que el perfil publico nunca lee (RNF-07).
/// NOTE (HU-35): "Para ti" los usa como arranque en frio mientras no haya interacciones (RN-46).
/// </summary>
internal sealed class InterestsService(IPreferenceRepository repository, ProfileService profiles, ICategoryDirectory categories)
    : IInterestsService
{
    /// <summary>Tope de categorias por cuenta; el campus del piloto tiene 6.</summary>
    public const int MaxCategories = 20;

    public async Task<Result<InterestsResponse>> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await profiles.ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var preference = await repository.FindAsync(account.Id, cancellationToken).ConfigureAwait(false);
        return Result<InterestsResponse>.Ok(new InterestsResponse(preference?.Interests ?? []));
    }

    public async Task<Result<InterestsResponse>> UpdateAsync(string userId, UpdateInterestsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = await profiles.ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var chosen = new List<string>();
        foreach (var slug in request.Categories)
        {
            if (string.IsNullOrEmpty(slug)
                || await categories.FindAsync(account.CommunityId, slug, cancellationToken).ConfigureAwait(false) is null)
            {
                return ProfilesErrors.InvalidInterest;
            }

            if (!chosen.Contains(slug))
            {
                chosen.Add(slug);
            }
        }

        var preference = await repository.SetInterestsAsync(account.Id, chosen, cancellationToken).ConfigureAwait(false);
        return Result<InterestsResponse>.Ok(new InterestsResponse(preference.Interests));
    }
}
