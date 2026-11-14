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

/// <summary>Lee y reemplaza los intereses de la cuenta autenticada (HU-66).</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/profiles/me/interests")]
[Authorize]
public sealed class InterestsController : ControllerBase
{
    private IInterestsService interests => HttpContext.RequestServices.GetRequiredService<IInterestsService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Intereses guardados; sin elegir responde una lista vacia.</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las categorias elegidas.</returns>
    [HttpGet]
    [ProducesResponseType<InterestsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        this.Respond(await interests.GetAsync(userId, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>Reemplaza los intereses por categorias activas del campus.</summary>
    /// <param name="request">Las categorias elegidas.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los intereses guardados o un error catalogado.</returns>
    [HttpPut]
    [ProducesResponseType<InterestsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(UpdateInterestsRequest request, CancellationToken cancellationToken)
    {
        var result = await interests.UpdateAsync(userId, request, cancellationToken).ConfigureAwait(false);
        return this.Respond(result, StatusCodes.Status200OK);
    }
}
