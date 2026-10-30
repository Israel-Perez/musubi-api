using System;
using System.ComponentModel.DataAnnotations;

namespace Musubi.Api.Modules.Auth;

// Contrato de /api/v1/auth (filas en docs/endpoints.md; formas en ContractTest/Fixtures/).
// Las respuestas nunca llevan hashes, codigos ni datos de otros usuarios (critic B2, OWASP API3).

/// <summary>Correo a validar contra los dominios de las comunidades (maqueta: "Campus detectado").</summary>
/// <param name="Email">Correo institucional.</param>
public sealed record CampusRequest([Required, MaxLength(254)] string Email);

/// <summary>Campus detectado por el dominio del correo.</summary>
/// <param name="ShortName">Siglas ("UTTT").</param>
/// <param name="Name">Nombre completo.</param>
public sealed record CampusResponse(string ShortName, string Name);

/// <summary>Registro (HU-01) con los consentimientos de HU-47.</summary>
/// <param name="Email">Correo institucional.</param>
/// <param name="FullName">Nombre completo.</param>
/// <param name="Password">Contrasena en claro (solo entrada; nunca se guarda ni se devuelve).</param> <!-- critic-allow: input-credential (HU-01) -->
/// <param name="AcceptTerms">Acepto los terminos y condiciones.</param>
/// <param name="AcceptPrivacy">Acepto el aviso de privacidad.</param>
public sealed record RegisterRequest(
    [Required, MaxLength(254)] string Email,
    [Required, MinLength(2), MaxLength(120)] string FullName,
    [Required, MaxLength(128)] string Password, // critic-allow: input-credential (HU-01)
    [Required] bool? AcceptTerms,
    [Required] bool? AcceptPrivacy);

/// <summary>Cuenta creada: falta verificar el correo (HU-03).</summary>
/// <param name="Email">Correo al que se envio el codigo.</param>
/// <param name="CodeExpiresAt">Vencimiento del codigo (UTC).</param>
/// <param name="ResendAvailableAt">Desde cuando se puede pedir otro codigo (UTC).</param>
public sealed record RegisterResponse(string Email, DateTime CodeExpiresAt, DateTime ResendAvailableAt);

/// <summary>Pedir otro codigo de verificacion.</summary>
/// <param name="Email">Correo institucional.</param>
public sealed record ResendCodeRequest([Required, MaxLength(254)] string Email);

/// <summary>Respuesta generica al reenvio (no revela si la cuenta existe).</summary>
/// <param name="ResendAvailableAt">Desde cuando se puede volver a pedir (UTC).</param>
public sealed record ResendCodeResponse(DateTime ResendAvailableAt);

/// <summary>Confirmar el correo con el codigo de 6 digitos (HU-03).</summary>
/// <param name="Email">Correo institucional.</param>
/// <param name="Code">Codigo de 6 digitos.</param>
public sealed record ConfirmCodeRequest(
    [Required, MaxLength(254)] string Email,
    [Required, RegularExpression("^[0-9]{6}$", ErrorMessage = "El código tiene 6 dígitos.")] string Code);

/// <summary>Sesion iniciada (02-03): access JWT corto + refresh rotativo.</summary>
/// <param name="AccessToken">JWT HS256.</param>
/// <param name="AccessTokenExpiresAt">Vencimiento del access token (UTC).</param>
/// <param name="RefreshToken">Token opaco para renovar la sesion.</param>
/// <param name="RefreshTokenExpiresAt">Vencimiento del refresh token (UTC).</param>
/// <param name="User">Datos minimos del usuario (maqueta "Verificacion-lista").</param>
public sealed record SessionResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    UserSummary User);

/// <summary>Usuario de la sesion.</summary>
/// <param name="Id">Id.</param>
/// <param name="FullName">Nombre completo.</param>
/// <param name="Email">Correo institucional.</param>
/// <param name="EmailVerified">Pertenencia validada (HU-03).</param>
/// <param name="Campus">Campus.</param>
public sealed record UserSummary(string Id, string FullName, string Email, bool EmailVerified, CampusResponse Campus);

/// <summary>Iniciar sesion (HU-02, maqueta "Iniciar sesion").</summary>
/// <param name="Email">Correo institucional.</param>
/// <param name="Password">Contrasena en claro (solo entrada).</param> <!-- critic-allow: input-credential (HU-02) -->
public sealed record LoginRequest(
    [Required, MaxLength(254)] string Email,
    [Required, MaxLength(128)] string Password); // critic-allow: input-credential (HU-02)

/// <summary>Renovar la sesion con el refresh token (02-03, L-16g).</summary>
/// <param name="RefreshToken">Token opaco vigente.</param>
public sealed record RefreshRequest([Required, MaxLength(200)] string RefreshToken);

/// <summary>Cerrar la sesion de este dispositivo (HU-02, L-10).</summary>
/// <param name="RefreshToken">Token opaco de la sesion.</param>
public sealed record LogoutRequest([Required, MaxLength(200)] string RefreshToken);

/// <summary>Pedir el codigo para recuperar la cuenta (HU-56).</summary>
/// <param name="Email">Correo institucional.</param>
public sealed record RecoveryRequest([Required, MaxLength(254)] string Email);

/// <summary>Definir la contrasena nueva con el codigo (HU-56).</summary>
/// <param name="Email">Correo institucional.</param>
/// <param name="Code">Codigo de 6 digitos.</param>
/// <param name="NewPassword">Contrasena nueva en claro (solo entrada).</param> <!-- critic-allow: input-credential (HU-56) -->
public sealed record RecoveryResetRequest(
    [Required, MaxLength(254)] string Email,
    [Required, RegularExpression("^[0-9]{6}$", ErrorMessage = "El código tiene 6 dígitos.")] string Code,
    [Required, MaxLength(128)] string NewPassword); // critic-allow: input-credential (HU-56)

/// <summary>Eliminar mi cuenta y mis datos (HU-46): se confirma con la contrasena actual.</summary>
/// <param name="Password">Contrasena actual en claro (solo entrada).</param> <!-- critic-allow: input-credential (HU-46) -->
public sealed record DeleteAccountRequest(
    [Required, MaxLength(128)] string Password); // critic-allow: input-credential (HU-46)
