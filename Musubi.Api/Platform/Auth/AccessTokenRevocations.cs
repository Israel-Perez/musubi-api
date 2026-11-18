using System;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using StackExchange.Redis;

namespace Musubi.Api.Platform.Auth;

/// <summary>
/// Lista de revocacion de access tokens (SCRUM-120). El JWT sigue sin estado, pero cada peticion consulta en Redis
/// (compartido por las 2 instancias) si su <c>jti</c> se cerro con un logout o si la persona perdio todas sus
/// sesiones (suspension, cambio o recuperacion de contrasena, cuenta eliminada, refresh token reutilizado).
/// Las entradas viven lo mismo que un access token: despues ya no hace falta recordarlas.
/// </summary>
internal interface IAccessTokenRevocations
{
    /// <summary>Revoca un access token (logout de ese dispositivo).</summary>
    /// <param name="tokenId">El <c>jti</c> del token.</param>
    /// <param name="expiresAt">Su vencimiento (UTC): la entrada vive hasta entonces.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task RevokeTokenAsync(string tokenId, DateTime expiresAt, CancellationToken cancellationToken);

    /// <summary>Revoca todos los access tokens emitidos a la persona hasta este momento.</summary>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task RevokeUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Si el token esta revocado.</summary>
    /// <param name="tokenId">El <c>jti</c> (puede faltar en tokens de prueba).</param>
    /// <param name="userId">El <c>sub</c>.</param>
    /// <param name="issuedAtMs">Emision en milisegundos Unix.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si ya no debe aceptarse.</returns>
    Task<bool> IsRevokedAsync(string? tokenId, string? userId, long issuedAtMs, CancellationToken cancellationToken);
}

/// <summary>Lectura de los claims de revocacion de un principal ya validado.</summary>
internal static class AccessTokenClaims
{
    /// <summary>Emision en milisegundos: <c>iat_ms</c> si existe; si no, el final del segundo de <c>iat</c>.</summary>
    /// <param name="principal">El principal del token.</param>
    /// <returns>Milisegundos Unix, o 0 si el token no trae emision.</returns>
    public static long IssuedAtMs(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (long.TryParse(principal.FindFirst(MusubiClaims.IssuedAtMs)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
        {
            return ms;
        }

        // Token sin iat_ms: se toma el principio del segundo, asi una revocacion en ese mismo segundo lo alcanza.
        return long.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Iat)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? seconds * 1000
            : 0;
    }

    /// <summary>Vencimiento (<c>exp</c>) del token.</summary>
    /// <param name="principal">El principal del token.</param>
    /// <returns>El vencimiento en UTC, o <c>null</c> si no trae.</returns>
    public static DateTime? ExpiresAt(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return long.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Exp)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
    }
}

/// <summary>
/// Implementacion sobre <see cref="IDistributedCache"/>: Redis en la nube y memoria de la instancia en Development
/// y en las pruebas. Si Redis no responde, la peticion sigue (RNF-15): el access token dura 15 minutos y el
/// refresh token, que si se revoca en MongoDB, es el que decide si la sesion continua.
/// </summary>
internal sealed partial class DistributedAccessTokenRevocations(
    IDistributedCache cache,
    IOptions<JwtOptions> options,
    TimeProvider time,
    ILogger<DistributedAccessTokenRevocations> logger) : IAccessTokenRevocations
{
    /// <summary>Prefijo de las llaves de un token revocado.</summary>
    public const string TokenKeyPrefix = "auth:revoked:jti:";

    /// <summary>Prefijo de las llaves "revocado desde" de un usuario.</summary>
    public const string UserKeyPrefix = "auth:revoked:user:";

    private static readonly byte[] Marker = [1];

    /// <inheritdoc />
    public async Task RevokeTokenAsync(string tokenId, DateTime expiresAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenId);
        var ttl = expiresAt - time.GetUtcNow().UtcDateTime + AuthPlatformServiceCollectionExtensions.ClockSkew;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        await SetAsync(TokenKeyPrefix + tokenId, Marker, ttl, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RevokeUserAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(userId);
        var now = time.GetUtcNow().ToUnixTimeMilliseconds();
        var ttl = TimeSpan.FromMinutes(options.Value.AccessTokenMinutes) + AuthPlatformServiceCollectionExtensions.ClockSkew;
        await SetAsync(UserKeyPrefix + userId, Encoding.UTF8.GetBytes(now.ToString(CultureInfo.InvariantCulture)), ttl, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> IsRevokedAsync(string? tokenId, string? userId, long issuedAtMs, CancellationToken cancellationToken)
    {
        try
        {
            var token = string.IsNullOrEmpty(tokenId) ? Task.FromResult<byte[]?>(null) : cache.GetAsync(TokenKeyPrefix + tokenId, cancellationToken);
            var user = string.IsNullOrEmpty(userId) ? Task.FromResult<byte[]?>(null) : cache.GetAsync(UserKeyPrefix + userId, cancellationToken);
            if (await token.ConfigureAwait(false) is not null)
            {
                return true;
            }

            // Revocado si se emitio antes de la revocacion. Uno emitido despues (la sesion nueva del cambio de
            // contrasena, o un login tras reactivar la cuenta) sigue valido.
            return await user.ConfigureAwait(false) is { } since
                && long.TryParse(Encoding.UTF8.GetString(since), NumberStyles.None, CultureInfo.InvariantCulture, out var revokedAtMs)
                && issuedAtMs < revokedAtMs;
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            LogUnavailable(logger, ex);
            return false;
        }
    }

    private async Task SetAsync(string key, byte[] value, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(key, value, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            // El refresh token ya se revoco en MongoDB: el access token se apaga solo al vencer.
            LogUnavailable(logger, ex);
        }
    }

    private static bool IsCacheFailure(Exception ex) => ex is RedisException or TimeoutException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lista de revocacion no disponible: se acepta el access token hasta que venza")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);
}
