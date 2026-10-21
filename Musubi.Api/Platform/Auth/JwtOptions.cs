using System;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Musubi.Api.Platform.Auth;

/// <summary>Configuracion de los tokens (seccion <c>Jwt</c>, 02-03). La llave es un secreto: user-secrets o SSM.</summary>
internal sealed class JwtOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Jwt";

    /// <summary>Emisor (<c>iss</c>).</summary>
    public string Issuer { get; set; } = "musubi-api";

    /// <summary>Audiencia (<c>aud</c>).</summary>
    public string Audience { get; set; } = "musubi-app";

    /// <summary>Llave HMAC-SHA256 en base64 (minimo 32 bytes). Secreto.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Vida del access token (minutos).</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Vida del refresh token desde su ultima rotacion (dias).</summary>
    public int RefreshTokenDays { get; set; } = 60;

    /// <summary>Llave simetrica para firmar y validar.</summary>
    /// <returns>La llave.</returns>
    public SymmetricSecurityKey CreateSigningKey() => new(Convert.FromBase64String(SigningKey));
}

/// <summary>Valida <see cref="JwtOptions"/> al arrancar (fail-fast) y genera una llave efimera solo en Development.</summary>
internal sealed class JwtOptionsSetup(IHostEnvironment environment) : IPostConfigureOptions<JwtOptions>, IValidateOptions<JwtOptions>
{
    /// <summary>Bytes minimos de la llave (HS256).</summary>
    public const int MinimumKeyBytes = 32;

    // Una sola llave por proceso: dotnet run sin user-secrets funciona, pero los tokens mueren al reiniciar.
    private static readonly Lazy<string> EphemeralKey = new(() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(MinimumKeyBytes)));

    /// <inheritdoc />
    public void PostConfigure(string? name, JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SigningKey) && environment.IsDevelopment())
        {
            options.SigningKey = EphemeralKey.Value;
        }
    }

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
        {
            return ValidateOptionsResult.Fail("Jwt:Issuer y Jwt:Audience son obligatorios.");
        }

        if (options.AccessTokenMinutes is < 1 or > 60 || options.RefreshTokenDays is < 1 or > 365)
        {
            return ValidateOptionsResult.Fail("Jwt:AccessTokenMinutes debe estar entre 1 y 60, y Jwt:RefreshTokenDays entre 1 y 365.");
        }

        var buffer = new byte[options.SigningKey.Length];
        if (!Convert.TryFromBase64String(options.SigningKey, buffer, out var bytes) || bytes < MinimumKeyBytes)
        {
            return ValidateOptionsResult.Fail(
                $"Jwt:SigningKey es obligatoria fuera de Development: base64 de al menos {MinimumKeyBytes} bytes (user-secrets o secreto del despliegue).");
        }

        return ValidateOptionsResult.Success;
    }
}
