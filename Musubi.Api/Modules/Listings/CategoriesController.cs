using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Listings;

/// <summary>Categorias para publicar y filtrar (maqueta <c>Vender-3</c> y "Filtrar").</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/categories")]
[AllowAnonymous]
public sealed class CategoriesController : ControllerBase
{
    /// <summary>Categorias del campus: el del usuario con sesion o el del visitante.</summary>
    /// <param name="campus">Campus del visitante (por defecto el del piloto).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las categorias.</returns>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List([FromQuery] string? campus, CancellationToken cancellationToken) =>
        this.Respond(
            await HttpContext.RequestServices.GetRequiredService<ListingService>()
                .CategoriesAsync(User.FindFirst(MusubiClaims.Community)?.Value, campus, cancellationToken).ConfigureAwait(false),
            StatusCodes.Status200OK);
}
