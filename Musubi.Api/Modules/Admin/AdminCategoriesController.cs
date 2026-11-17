using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Admin;

/// <summary>
/// Gestionar categorias (HU-41, RF-56; sin pantalla en la maqueta). Filas canonicas en docs/endpoints.md. Solo el rol
/// <c>admin</c> (RNF-03) y solo las categorias de su campus.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin/categories")]
[Authorize(Roles = UserRoles.Admin)]
public sealed class AdminCategoriesController : ControllerBase
{
    private CategoryAdminService categories => HttpContext.RequestServices.GetRequiredService<CategoryAdminService>();

    private string userId => User.FindFirst(MusubiClaims.Subject)?.Value ?? string.Empty;

    /// <summary>Las categorias del campus, tambien las desactivadas.</summary>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>200 con las categorias.</returns>
    [HttpGet]
    [ProducesResponseType<AdminCategoriesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        this.Respond(await categories.ListAsync(userId, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);

    /// <summary>"Agregar categoria": activa y al final.</summary>
    /// <param name="request">El nombre.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>201 con la categoria.</returns>
    [HttpPost]
    [ProducesResponseType<AdminCategoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateCategoryRequest request, CancellationToken cancellationToken) =>
        this.Respond(await categories.CreateAsync(userId, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status201Created);

    /// <summary>Renombrar, desactivar o volver a activar.</summary>
    /// <param name="slug">Categoria.</param>
    /// <param name="request">Lo que cambia.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>200 con la categoria.</returns>
    [HttpPatch("{slug}")]
    [ProducesResponseType<AdminCategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string slug, UpdateCategoryRequest request, CancellationToken cancellationToken) =>
        this.Respond(await categories.UpdateAsync(userId, slug, request, cancellationToken).ConfigureAwait(false), StatusCodes.Status200OK);
}
