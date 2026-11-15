using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Matching;

/// <summary>"Ya coinciden con lo que buscas" (HU-18, M-06). Fila canonica en docs/endpoints.md.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/wanted")]
[Authorize]
public sealed class WantedMatchesController : ControllerBase
{
    private WantedMatchesService matches => HttpContext.RequestServices.GetRequiredService<WantedMatchesService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Las publicaciones que coinciden con mi solicitud (maqueta <c>Necesidad-publicada</c>).</summary>
    /// <param name="id">Mi solicitud.</param>
    /// <param name="limit">De 1 a 20 (10 si no se manda).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las coincidencias; de otra persona o inexistente, <c>wanted.not_found</c>.</returns>
    [HttpGet("{id}/matches")]
    [ProducesResponseType<WantedMatchPage>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Matches(string id, [FromQuery] int? limit, CancellationToken cancellationToken) =>
        this.Respond(await matches.FindAsync(userId, id, limit, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);
}
