using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;

namespace Musubi.Api.Modules.Auth;

/// <summary>Refresh token emitido: el valor en claro solo existe en la respuesta al cliente.</summary>
/// <param name="Token">Token opaco (base64url de 32 bytes aleatorios).</param>
/// <param name="ExpiresAt">Vencimiento (UTC).</param>
/// <param name="FamilyId">Familia (una por inicio de sesion / dispositivo).</param>
internal sealed record IssuedRefreshToken(string Token, DateTime ExpiresAt, string FamilyId);

/// <summary>Resultado de rotar un refresh token.</summary>
internal enum RotationStatus
{
    /// <summary>Rotado: se entrega un token nuevo de la misma familia.</summary>
    Rotated,

    /// <summary>Desconocido, vencido o revocado.</summary>
    Invalid,

    /// <summary>Se presento un token ya usado: posible robo, se revoca toda la familia.</summary>
    ReuseDetected,
}

/// <summary>Resultado de <see cref="RefreshTokenService.RotateAsync"/>.</summary>
/// <param name="Status">Estado.</param>
/// <param name="UserId">Usuario (solo si se roto).</param>
/// <param name="Next">Token nuevo (solo si se roto).</param>
internal sealed record RefreshRotation(RotationStatus Status, string? UserId = null, IssuedRefreshToken? Next = null);

/// <summary>
/// Sesiones "siempre vivas salvo logout" (L-10, L-16g, 02-03): refresh token opaco y rotativo. Cada uso entrega
/// uno nuevo y el anterior queda marcado; si alguien presenta uno ya usado, se revoca la familia completa.
/// En la base solo vive el hash SHA-256 (R10).
/// </summary>
internal sealed class RefreshTokenService(IRefreshTokenRepository tokens, IOptions<JwtOptions> options, TimeProvider time)
{
    /// <summary>Largo maximo guardado del User-Agent (validacion del esquema: 200).</summary>
    public const int MaxUserAgentLength = 200;

    /// <summary>Emite el primer token de una sesion (login) o el siguiente de una familia (rotacion).</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="familyId">Familia existente, o <c>null</c> para un inicio de sesion nuevo.</param>
    /// <param name="userAgent">User-Agent del cliente (opcional).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El token en claro y su vencimiento.</returns>
    public async Task<IssuedRefreshToken> IssueAsync(string userId, string? familyId, string? userAgent, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var family = familyId ?? AuthIds.New();
        var expiresAt = now.AddDays(options.Value.RefreshTokenDays);
        var device = string.IsNullOrWhiteSpace(userAgent)
            ? null
            : new RefreshTokenDevice { UserAgent = userAgent[..Math.Min(userAgent.Length, MaxUserAgentLength)] };

        await tokens.InsertAsync(
            new RefreshTokenDocument
            {
                Id = AuthIds.New(),
                UserId = userId,
                FamilyId = family,
                TokenHash = Hash(token),
                ExpiresAt = expiresAt,
                Device = device,
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        return new IssuedRefreshToken(token, expiresAt, family);
    }

    /// <summary>Rota un token: lo consume de forma atomica y entrega el siguiente de la misma familia.</summary>
    /// <param name="presentedToken">Token que envio el cliente.</param>
    /// <param name="userAgent">User-Agent del cliente (opcional).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El resultado de la rotacion.</returns>
    public async Task<RefreshRotation> RotateAsync(string presentedToken, string? userAgent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return new RefreshRotation(RotationStatus.Invalid);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var hash = Hash(presentedToken);
        var consumed = await tokens.TryConsumeAsync(hash, now, cancellationToken).ConfigureAwait(false);
        if (consumed is not null)
        {
            var next = await IssueAsync(consumed.UserId, consumed.FamilyId, userAgent, cancellationToken).ConfigureAwait(false);
            return new RefreshRotation(RotationStatus.Rotated, consumed.UserId, next);
        }

        var existing = await tokens.FindByHashAsync(hash, cancellationToken).ConfigureAwait(false);
        if (existing?.UsedAt is not null)
        {
            // Un token usado que vuelve a aparecer: alguien mas lo tiene. Se corta la familia (todas sus copias).
            await tokens.RevokeFamilyAsync(existing.FamilyId, now, cancellationToken).ConfigureAwait(false);
            return new RefreshRotation(RotationStatus.ReuseDetected);
        }

        return new RefreshRotation(RotationStatus.Invalid);
    }

    /// <summary>Logout: revoca la familia del token presentado (cierra la sesion de ese dispositivo).</summary>
    /// <param name="presentedToken">Token que envio el cliente.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si el token existia.</returns>
    public async Task<bool> RevokeAsync(string presentedToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(presentedToken))
        {
            return false;
        }

        var existing = await tokens.FindByHashAsync(Hash(presentedToken), cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        await tokens.RevokeFamilyAsync(existing.FamilyId, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Hash SHA-256 en hexadecimal (64 caracteres) del token en claro.</summary>
    /// <param name="token">Token en claro.</param>
    /// <returns>El hash.</returns>
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
