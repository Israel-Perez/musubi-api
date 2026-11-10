using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Profiles;

/// <summary>Errores de negocio de M-02 (catalogo canonico: docs/errores.md). Las fotos usan <c>listings.photo_*</c>.</summary>
internal static class ProfilesErrors
{
    /// <summary>Tema no permitido en las preferencias.</summary>
    public static readonly ApiError InvalidTheme = new(
        "profiles.invalid_theme", StatusCodes.Status400BadRequest, "Elige un tema válido.");

    /// <summary>Carrera que no existe o no esta activa en el campus.</summary>
    public static readonly ApiError InvalidCareer = new(
        "profiles.invalid_career", StatusCodes.Status400BadRequest, "Elige una carrera de la lista.");

    /// <summary>Periodo fuera del rango de la carrera (o del campus, sin carrera).</summary>
    public static readonly ApiError InvalidTerm = new(
        "profiles.invalid_term", StatusCodes.Status400BadRequest, "Elige un periodo que corresponda a tu carrera.");

    /// <summary>Perfil inexistente, de otro campus o de una cuenta inactiva (no revela cual, OWASP API3).</summary>
    public static readonly ApiError NotFound = new(
        "profiles.not_found", StatusCodes.Status404NotFound, "No encontramos ese perfil.");
}
