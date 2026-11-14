using System;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Modules.Recommendations;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Profiles;

internal sealed class PreferencesService(IPreferenceRepository repository, ProfileService profiles, IInteractionHistory history) : IPreferencesService
{
    public async Task<Result<PreferenceResponse>> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await profiles.ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var preference = await repository.FindAsync(account.Id, cancellationToken).ConfigureAwait(false);
        return Result<PreferenceResponse>.Ok(new PreferenceResponse(preference?.Theme ?? "system", preference?.Recommendations ?? true));
    }

    public async Task<Result<PreferenceResponse>> UpdateAsync(string userId, UpdatePreferenceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!PreferencesValidators.IsValidTheme(request.Theme))
        {
            return ProfilesErrors.InvalidTheme;
        }

        var account = await profiles.ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var preference = await repository.SetThemeAsync(account.Id, request.Theme, cancellationToken).ConfigureAwait(false);
        if (request.Recommendations is { } enabled)
        {
            preference = await repository.SetRecommendationsAsync(account.Id, enabled, cancellationToken).ConfigureAwait(false);
            if (!enabled)
            {
                // HU-35 y aviso de privacidad (HU-47): rechazar la finalidad borra el historial que la alimentaba.
                await history.ForgetAsync(account.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return Result<PreferenceResponse>.Ok(new PreferenceResponse(preference.Theme, preference.Recommendations));
    }
}
