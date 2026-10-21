using System;
using Microsoft.AspNetCore.Mvc;

namespace Musubi.Api.Platform.Errors;

/// <summary>Respuesta de error de negocio desde un controller, sin try/catch (critic B4): <c>return this.Problem(AuthErrors.X)</c>.</summary>
internal static class ApiProblemExtensions
{
    /// <summary>Crea la respuesta <c>application/problem+json</c> con el codigo y el mensaje del error.</summary>
    /// <param name="controller">El controller.</param>
    /// <param name="error">Error del catalogo (docs/errores.md).</param>
    /// <returns>El resultado HTTP.</returns>
    public static ObjectResult Problem(this ControllerBase controller, ApiError error)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(error);

        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(controller.HttpContext, error.Status, error.Message);
        problem.Title = error.Message;
        problem.Extensions[ApiErrors.CodeExtension] = error.Code;
        return new ObjectResult(problem) { StatusCode = error.Status, ContentTypes = { "application/problem+json" } };
    }
}
