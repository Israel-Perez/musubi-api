using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Musubi.Api.Platform.Auth;

/// <summary>Claims del access token (minimos: sin correo ni nombre, RNF-05).</summary>
internal static class MusubiClaims
{
    /// <summary>Id del usuario.</summary>
    public const string Subject = JwtRegisteredClaimNames.Sub;

    /// <summary>Id de la comunidad (R1).</summary>
    public const string Community = "community";

    /// <summary>Rol (uno por claim).</summary>
    public const string Role = "role";
}

/// <summary>Access token emitido.</summary>
/// <param name="Token">JWT firmado.</param>
/// <param name="ExpiresAt">Vencimiento (UTC).</param>
internal sealed record AccessToken(string Token, DateTime ExpiresAt);

/// <summary>Emite access tokens JWT HS256 de vida corta (02-03).</summary>
internal sealed class AccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider time)
{
    private readonly JsonWebTokenHandler handler = new();

    /// <summary>Emite el token de un usuario.</summary>
    /// <param name="userId">Id del usuario (<c>sub</c>).</param>
    /// <param name="communityId">Comunidad.</param>
    /// <param name="roles">Roles.</param>
    /// <returns>El token y su vencimiento.</returns>
    public AccessToken Issue(string userId, string communityId, IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var jwt = options.Value;
        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [MusubiClaims.Subject] = userId,
            [MusubiClaims.Community] = communityId,
            [MusubiClaims.Role] = new List<string>(roles),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
        };

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Claims = claims,
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expires);
    }
}
