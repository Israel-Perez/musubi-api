using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Profiles;

internal interface IInterestsService
{
    Task<Result<InterestsResponse>> GetAsync(string userId, CancellationToken cancellationToken);

    Task<Result<InterestsResponse>> UpdateAsync(string userId, UpdateInterestsRequest request, CancellationToken cancellationToken);
}
