using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Musubi.Api.Modules.Auth;

/// <summary>Codigos de verificacion de 6 digitos (HU-03, HU-56).</summary>
internal static class VerificationCodes
{
    /// <summary>Vigencia del codigo.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Espera minima entre reenvios (maqueta: "Reenviar codigo en 0:42").</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Intentos fallidos permitidos por codigo (5 de 1 000 000 combinaciones).</summary>
    public const int MaxAttempts = 5;

    /// <summary>Genera un codigo uniforme con un CSPRNG (nunca <see cref="Random"/>).</summary>
    /// <returns>Seis digitos, con ceros a la izquierda.</returns>
    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>Hash ligado al usuario: el mismo codigo de dos usuarios no produce el mismo hash.</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="code">Codigo en claro.</param>
    /// <returns>SHA-256 en hexadecimal (64 caracteres).</returns>
    public static string Hash(string userId, string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{code}")));

    /// <summary>Compara en tiempo constante (no revela cuantos caracteres coinciden).</summary>
    /// <param name="expectedHash">Hash guardado.</param>
    /// <param name="userId">Usuario.</param>
    /// <param name="code">Codigo presentado.</param>
    /// <returns><c>true</c> si coincide.</returns>
    public static bool Matches(string expectedHash, string userId, string code) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHash),
            Encoding.ASCII.GetBytes(Hash(userId, code)));
}
