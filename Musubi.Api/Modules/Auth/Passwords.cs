using System;
using System.Linq;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Auth.Data;

namespace Musubi.Api.Modules.Auth;

/// <summary>Politica de contrasena de la maqueta: al menos 8 caracteres, una letra y un numero.</summary>
internal static class PasswordPolicy
{
    /// <summary>Longitud minima (maqueta: "Al menos 8 caracteres").</summary>
    public const int MinLength = 8;

    /// <summary>Longitud maxima: acota el costo del hash (evita DoS con contrasenas enormes).</summary>
    public const int MaxLength = 128;

    /// <summary>Valida la contrasena.</summary>
    /// <param name="password">Contrasena en claro.</param>
    /// <returns><c>true</c> si cumple la politica.</returns>
    public static bool IsValid(string? password) =>
        password is { Length: >= MinLength and <= MaxLength }
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);
}

/// <summary>Resultado de verificar una contrasena.</summary>
internal enum PasswordCheck
{
    /// <summary>No coincide.</summary>
    Failed,

    /// <summary>Coincide.</summary>
    Success,

    /// <summary>Coincide, pero el hash usa parametros viejos: rehacerlo al iniciar sesion.</summary>
    SuccessRehashNeeded,
}

/// <summary>Hash adaptativo de contrasenas (RNF-01): PBKDF2-HMAC-SHA512 con sal aleatoria, formato Identity V3.</summary>
internal sealed class PasswordHashing
{
    /// <summary>Iteraciones de PBKDF2-HMAC-SHA512 (recomendacion OWASP Password Storage Cheat Sheet).</summary>
    public const int Iterations = 210_000;

    private static readonly UserDocument Owner = new();

    private readonly PasswordHasher<UserDocument> hasher = new(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = Iterations,
    }));

    /// <summary>Calcula el hash de una contrasena nueva.</summary>
    /// <param name="password">Contrasena en claro.</param>
    /// <returns>Hash en base64 (incluye algoritmo, iteraciones y sal).</returns>
    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return hasher.HashPassword(Owner, password);
    }

    // Hash de una contrasena aleatoria: se verifica contra el cuando el correo no existe, para igualar el tiempo.
    private readonly Lazy<string> decoy = new(() => new PasswordHasher<UserDocument>(Options.Create(new PasswordHasherOptions
    {
        CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
        IterationCount = Iterations,
    })).HashPassword(Owner, Guid.NewGuid().ToString("N")));

    /// <summary>Gasta el mismo tiempo que una verificacion real (login con correo inexistente, OWASP API2).</summary>
    /// <param name="password">Contrasena presentada.</param>
    public void VerifyAgainstDecoy(string? password) => _ = Verify(decoy.Value, password ?? string.Empty);

    /// <summary>Verifica una contrasena contra su hash (comparacion en tiempo constante).</summary>
    /// <param name="hash">Hash guardado.</param>
    /// <param name="password">Contrasena en claro.</param>
    /// <returns>El resultado.</returns>
    public PasswordCheck Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
        {
            return PasswordCheck.Failed;
        }

        return hasher.VerifyHashedPassword(Owner, hash, password) switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };
    }
}
