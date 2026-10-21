using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Musubi.Api.Modules.Auth.Data;

// Documentos de persistencia de M-01 (02-01). La validacion del servidor vive en db/001-identidad.js;
// los nombres de campo salen de MongoConventions (camelCase). Fila canonica: docs/modelo-datos.md.

/// <summary>Campus o universidad. El dominio del correo identifica la comunidad (RNF-21).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class CommunityDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Identificador legible (<c>uttt</c>).</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Nombre completo.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Siglas que muestra la app ("UTTT").</summary>
    public string ShortName { get; init; } = string.Empty;

    /// <summary>Dominios de correo del campus, en minusculas (tope 10).</summary>
    public IReadOnlyList<string> EmailDomains { get; init; } = [];

    /// <summary><c>active</c> o <c>inactive</c>.</summary>
    public string Status { get; init; } = CommunityStatuses.Active;

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Alta (UTC).</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>Estados de una comunidad.</summary>
internal static class CommunityStatuses
{
    /// <summary>Acepta registros.</summary>
    public const string Active = "active";

    /// <summary>No acepta registros nuevos.</summary>
    public const string Inactive = "inactive";
}

/// <summary>Identidad y credenciales de un usuario. Nunca se devuelve tal cual en una respuesta (R10).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class UserDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Comunidad a la que pertenece (R1).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string CommunityId { get; init; } = string.Empty;

    /// <summary>Correo tal como lo escribio.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Correo normalizado (minusculas, sin espacios); indice unico.</summary>
    public string EmailNormalized { get; init; } = string.Empty;

    /// <summary>Hash adaptativo de la contrasena (02-03, RNF-01).</summary>
    public string PasswordHash { get; init; } = string.Empty;

    /// <summary>Nombre completo.</summary>
    public string FullName { get; init; } = string.Empty;

    /// <summary>Ver <see cref="UserStatuses"/>.</summary>
    public string Status { get; init; } = UserStatuses.PendingVerification;

    /// <summary>Momento en que verifico su correo (HU-03).</summary>
    public DateTime? EmailVerifiedAt { get; init; }

    /// <summary>Versiones aceptadas de terminos y aviso de privacidad (HU-47).</summary>
    public UserConsents Consents { get; init; } = new();

    /// <summary>Roles (tope 3): <c>student</c>, <c>admin</c>.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [UserRoles.Student];

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Alta (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Ultimo cambio (UTC).</summary>
    public DateTime UpdatedAt { get; init; }
}

/// <summary>Consentimientos aceptados al registrarse.</summary>
internal sealed class UserConsents
{
    /// <summary>Version de los terminos y condiciones.</summary>
    public string TermsVersion { get; init; } = string.Empty;

    /// <summary>Version del aviso de privacidad.</summary>
    public string PrivacyVersion { get; init; } = string.Empty;

    /// <summary>Momento de la aceptacion (UTC).</summary>
    public DateTime AcceptedAt { get; init; }
}

/// <summary>Estados de un usuario.</summary>
internal static class UserStatuses
{
    /// <summary>Registrado, falta verificar el correo.</summary>
    public const string PendingVerification = "pendingVerification";

    /// <summary>Correo verificado.</summary>
    public const string Active = "active";

    /// <summary>Suspendido por un administrador (HU-40).</summary>
    public const string Suspended = "suspended";
}

/// <summary>Roles de usuario.</summary>
internal static class UserRoles
{
    /// <summary>Estudiante (compra, vende, pide).</summary>
    public const string Student = "student";

    /// <summary>Administrador de la comunidad (M-14).</summary>
    public const string Admin = "admin";
}

/// <summary>Codigo de verificacion vigente (uno por usuario y proposito; TTL).</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class VerificationCodeDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Usuario dueno del codigo.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; init; } = string.Empty;

    /// <summary>Ver <see cref="VerificationPurposes"/>.</summary>
    public string Purpose { get; init; } = VerificationPurposes.EmailVerification;

    /// <summary>Hash del codigo (nunca el codigo en claro).</summary>
    public string CodeHash { get; init; } = string.Empty;

    /// <summary>Intentos fallidos.</summary>
    public int Attempts { get; init; }

    /// <summary>Ultimo envio (para el tiempo de espera de "Reenviar").</summary>
    public DateTime LastSentAt { get; init; }

    /// <summary>Vencimiento (indice TTL; el codigo tambien filtra por esta fecha).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Alta (UTC).</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>Propositos de un codigo de verificacion.</summary>
internal static class VerificationPurposes
{
    /// <summary>Verificar el correo institucional (HU-03).</summary>
    public const string EmailVerification = "emailVerification";

    /// <summary>Recuperar la contrasena ("Olvidaste tu contrasena" de la maqueta).</summary>
    public const string PasswordReset = "passwordReset";
}

/// <summary>Refresh token de una sesion (solo su hash). La familia detecta la reutilizacion de un token robado.</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Lo instancia el serializador BSON.")]
internal sealed class RefreshTokenDocument
{
    /// <summary>Id (ObjectId).</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; init; } = string.Empty;

    /// <summary>Usuario de la sesion.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; init; } = string.Empty;

    /// <summary>Familia: todos los tokens rotados desde un mismo inicio de sesion.</summary>
    [BsonRepresentation(BsonType.ObjectId)]
    public string FamilyId { get; init; } = string.Empty;

    /// <summary>Hash SHA-256 del token opaco.</summary>
    public string TokenHash { get; init; } = string.Empty;

    /// <summary>Vencimiento (indice TTL).</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>Momento en que se uso para rotar (un token solo se usa una vez).</summary>
    public DateTime? UsedAt { get; init; }

    /// <summary>Momento en que se revoco (logout o reutilizacion detectada).</summary>
    public DateTime? RevokedAt { get; init; }

    /// <summary>Datos minimos del dispositivo.</summary>
    public RefreshTokenDevice? Device { get; init; }

    /// <summary>Version del documento (R4).</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Alta (UTC).</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>Dispositivo de la sesion (solo lo necesario para listar sesiones).</summary>
internal sealed class RefreshTokenDevice
{
    /// <summary>User-Agent recortado a 200 caracteres.</summary>
    public string? UserAgent { get; init; }
}
