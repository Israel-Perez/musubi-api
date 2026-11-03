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

/// <summary>Carreras del campus para "Datos personales" (HU-04; NOTE (HU-41): administrarlas).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/careers")]
[Authorize]
public sealed class CareersController : ControllerBase
{
    /// <summary>Carreras activas del campus del token y su periodo.</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las carreras.</returns>
    [HttpGet]
    [ProducesResponseType<CareersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        this.Respond(
            await HttpContext.RequestServices.GetRequiredService<ProfileService>()
                .CareersAsync(User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty, cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);
}
