using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Auth;

/// <summary>Errores de negocio de M-01 (catalogo canonico: docs/errores.md). Textos tomados de la maqueta.</summary>
internal static class AuthErrors
{
    /// <summary>Login fallido: nunca dice si el correo existe (OWASP API2).</summary>
    public static readonly ApiError InvalidCredentials = new(
        "auth.invalid_credentials", StatusCodes.Status401Unauthorized, "El correo o la contraseña no coinciden. Revísalos e inténtalo otra vez.");

    /// <summary>Refresh token invalido, vencido o reutilizado.</summary>
    public static readonly ApiError SessionExpired = new(
        "auth.session_expired", StatusCodes.Status401Unauthorized, "Tu sesión terminó. Inicia sesión de nuevo.");

    /// <summary>Login con correo sin verificar (HU-03).</summary>
    public static readonly ApiError EmailNotVerified = new(
        "auth.email_not_verified", StatusCodes.Status403Forbidden, "Verifica tu correo institucional para continuar.");

    /// <summary>Cuenta suspendida (HU-40).</summary>
    public static readonly ApiError AccountSuspended = new(
        "auth.account_suspended", StatusCodes.Status403Forbidden, "Tu cuenta está suspendida. Escríbenos si crees que es un error.");

    /// <summary>Registro con un dominio que no es de ninguna comunidad (HU-01).</summary>
    public static readonly ApiError EmailNotInstitutional = new(
        "auth.email_not_institutional", StatusCodes.Status422UnprocessableEntity, "Usa el correo que te dio tu universidad, por ejemplo tu.nombre@uttt.edu.mx.");

    /// <summary>Registro con un correo ya usado (HU-01).</summary>
    public static readonly ApiError EmailAlreadyRegistered = new(
        "auth.email_already_registered", StatusCodes.Status409Conflict, "Ese correo ya tiene una cuenta. Inicia sesión.");

    /// <summary>Contrasena fuera de la politica de la maqueta.</summary>
    public static readonly ApiError WeakPassword = new(
        "auth.weak_password", StatusCodes.Status400BadRequest, "La contraseña debe tener al menos 8 caracteres, una letra y un número.");

    /// <summary>Registro sin aceptar terminos y aviso de privacidad (HU-47).</summary>
    public static readonly ApiError ConsentRequired = new(
        "auth.consent_required", StatusCodes.Status400BadRequest, "Acepta los Términos y condiciones y el Aviso de privacidad para continuar.");

    /// <summary>Codigo de verificacion incorrecto o vencido (HU-03).</summary>
    public static readonly ApiError InvalidCode = new(
        "auth.invalid_code", StatusCodes.Status400BadRequest, "El código no es válido o ya venció. Pide uno nuevo.");

    /// <summary>Demasiados intentos con el mismo codigo (HU-03).</summary>
    public static readonly ApiError TooManyCodeAttempts = new(
        "auth.code_attempts_exceeded", StatusCodes.Status429TooManyRequests, "Demasiados intentos con este código. Pide uno nuevo.");

    /// <summary>Reenvio antes del tiempo de espera (HU-03).</summary>
    public static readonly ApiError ResendTooSoon = new(
        "auth.resend_too_soon", StatusCodes.Status429TooManyRequests, "Espera un momento antes de pedir otro código.");
}
