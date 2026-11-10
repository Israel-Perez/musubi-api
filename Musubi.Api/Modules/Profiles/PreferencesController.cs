using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Profiles;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/profiles/me/preferences")]
[Authorize]
public sealed class PreferencesController : ControllerBase
{
    private IPreferencesService preferences => HttpContext.RequestServices.GetRequiredService<IPreferencesService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    [HttpGet]
    [ProducesResponseType<PreferenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.Respond(await preferences.GetAsync(userId, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    [HttpPut]
    [ProducesResponseType<PreferenceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(UpdatePreferenceRequest request, CancellationToken cancellationToken)
    {
        var result = await preferences.UpdateAsync(userId, request, cancellationToken).ConfigureAwait(false);
        return this.Respond(result, StatusCodes.Status200OK);
    }
}
