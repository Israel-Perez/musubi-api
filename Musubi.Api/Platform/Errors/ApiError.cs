using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Musubi.Api.Platform.Errors;

/// <summary>
/// Error de negocio con codigo estable (02-03). El front traduce por <see cref="Code"/>, nunca por el texto.
/// Catalogo canonico: docs/errores.md.
/// </summary>
/// <param name="Code">Codigo estable en minusculas con punto (<c>auth.invalid_credentials</c>).</param>
/// <param name="Status">Estado HTTP.</param>
/// <param name="Message">Mensaje en espanol, apto para mostrarse tal cual (RNF-18).</param>
internal sealed record ApiError(string Code, int Status, string Message);

/// <summary>Codigos y mensajes genericos por estado HTTP (los usan las respuestas sin cuerpo propio).</summary>
internal static class ApiErrors
{
    /// <summary>Nombre de la extension de ProblemDetails con el codigo.</summary>
    public const string CodeExtension = "code";

    /// <summary>Nombre de la extension con el id de rastreo.</summary>
    public const string TraceIdExtension = "traceId";

    /// <summary>400 de validacion de entrada.</summary>
    public static readonly ApiError ValidationFailed = new("validation.failed", StatusCodes.Status400BadRequest, "Revisa los datos enviados.");

    /// <summary>401 sin sesion valida.</summary>
    public static readonly ApiError Unauthorized = new("auth.unauthorized", StatusCodes.Status401Unauthorized, "Inicia sesión para continuar.");

    /// <summary>403 sin permiso.</summary>
    public static readonly ApiError Forbidden = new("auth.forbidden", StatusCodes.Status403Forbidden, "No tienes permiso para hacer esto.");

    /// <summary>404.</summary>
    public static readonly ApiError NotFound = new("resource.not_found", StatusCodes.Status404NotFound, "No encontramos lo que buscas.");

    /// <summary>405.</summary>
    public static readonly ApiError MethodNotAllowed = new("request.method_not_allowed", StatusCodes.Status405MethodNotAllowed, "Esta operación no está disponible.");

    /// <summary>409.</summary>
    public static readonly ApiError Conflict = new("resource.conflict", StatusCodes.Status409Conflict, "La información cambió o ya existe. Actualiza e inténtalo de nuevo.");

    /// <summary>413.</summary>
    public static readonly ApiError PayloadTooLarge = new("request.too_large", StatusCodes.Status413PayloadTooLarge, "El archivo o los datos son demasiado grandes.");

    /// <summary>422.</summary>
    public static readonly ApiError Unprocessable = new("request.unprocessable", StatusCodes.Status422UnprocessableEntity, "No se pudo completar la operación.");

    /// <summary>429.</summary>
    public static readonly ApiError RateLimited = new("rate_limit.exceeded", StatusCodes.Status429TooManyRequests, "Demasiados intentos. Espera un momento e inténtalo de nuevo.");

    /// <summary>500.</summary>
    public static readonly ApiError ServerError = new("server.error", StatusCodes.Status500InternalServerError, "Ocurrió un error inesperado. Inténtalo más tarde.");

    /// <summary>503.</summary>
    public static readonly ApiError Unavailable = new("server.unavailable", StatusCodes.Status503ServiceUnavailable, "El servicio no está disponible por ahora. Inténtalo en unos minutos.");

    private static readonly Dictionary<int, ApiError> ByStatus = new()
    {
        [400] = ValidationFailed,
        [401] = Unauthorized,
        [403] = Forbidden,
        [404] = NotFound,
        [405] = MethodNotAllowed,
        [409] = Conflict,
        [413] = PayloadTooLarge,
        [422] = Unprocessable,
        [429] = RateLimited,
        [500] = ServerError,
        [503] = Unavailable,
    };

    /// <summary>Error generico para un estado (500 si no hay uno especifico).</summary>
    /// <param name="status">Estado HTTP.</param>
    /// <returns>El error generico.</returns>
    public static ApiError ForStatus(int status)
    {
        if (ByStatus.TryGetValue(status, out var error))
        {
            return error;
        }

        return status >= 500 ? ServerError : Unprocessable with { Status = status };
    }

    /// <summary>Completa un ProblemDetails con titulo en espanol, codigo y traceId (sin pisar lo que ya trae).</summary>
    /// <param name="problem">El ProblemDetails a completar.</param>
    /// <param name="httpContext">La peticion.</param>
    public static void Decorate(ProblemDetails problem, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(httpContext);

        var generic = ForStatus(problem.Status ?? httpContext.Response.StatusCode);
        if (!problem.Extensions.ContainsKey(CodeExtension))
        {
            problem.Extensions[CodeExtension] = generic.Code;
            // El titulo por defecto de ASP.NET viene en ingles; solo se reemplaza si no es un error de negocio.
            problem.Title = generic.Message;
        }

        problem.Detail = null;
        problem.Extensions[TraceIdExtension] = httpContext.TraceIdentifier;
    }
}
