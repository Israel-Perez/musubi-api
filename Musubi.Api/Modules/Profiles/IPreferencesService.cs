using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Profiles;

public interface IPreferencesService
{
    Task<Result<PreferenceResponse>> GetAsync(string userId, CancellationToken cancellationToken);

    Task<Result<PreferenceResponse>> UpdateAsync(string userId, UpdatePreferenceRequest request, CancellationToken cancellationToken);
}
