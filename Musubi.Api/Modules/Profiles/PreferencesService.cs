using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Profiles;

internal sealed class PreferencesService(IPreferenceRepository repository, ProfileService profiles) : IPreferencesService
{
    public async Task<Result<PreferenceResponse>> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await profiles.ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var preference = await repository.FindAsync(account.Id, cancellationToken).ConfigureAwait(false);
        return Result<PreferenceResponse>.Ok(new PreferenceResponse(preference?.Theme ?? "system"));
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
        return Result<PreferenceResponse>.Ok(new PreferenceResponse(preference.Theme));
    }
}
