using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;

namespace Musubi.Api.Modules.Auth;

/// <summary>Abre y arma sesiones (02-03): access JWT + refresh rotativo + datos minimos del usuario.</summary>
internal sealed class SessionIssuer(
    ICommunityRepository communities,
    IUserRepository users,
    AccessTokenIssuer accessTokens,
    RefreshTokenService refreshTokens,
    IOptions<AdminOptions> admins,
    TimeProvider time)
{
    /// <summary>Sesion nueva (familia nueva de refresh tokens): verificacion del correo o login.</summary>
    /// <param name="user">Usuario activo.</param>
    /// <param name="userAgent">User-Agent del cliente.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La sesion.</returns>
    public async Task<SessionResponse> StartAsync(UserDocument user, string? userAgent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (admins.Value.Includes(user.EmailNormalized) && !user.Roles.Contains(UserRoles.Admin, StringComparer.Ordinal))
        {
            // HU-39: el rol queda en la base; desde aqui el token y la app lo ven como administrador.
            await users.AddRoleAsync(user.Id, UserRoles.Admin, time.GetUtcNow().UtcDateTime, cancellationToken).ConfigureAwait(false);
            user = await users.FindByIdAsync(user.Id, cancellationToken).ConfigureAwait(false) ?? user;
        }

        var refresh = await refreshTokens.IssueAsync(user.Id, familyId: null, userAgent, cancellationToken).ConfigureAwait(false);
        return await BuildAsync(user, refresh, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Arma la respuesta con un refresh token ya emitido (rotacion).</summary>
    /// <param name="user">Usuario activo.</param>
    /// <param name="refresh">Refresh token emitido.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La sesion.</returns>
    public async Task<SessionResponse> BuildAsync(UserDocument user, IssuedRefreshToken refresh, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(refresh);
        var community = await communities.FindByIdAsync(user.CommunityId, cancellationToken).ConfigureAwait(false);
        var access = accessTokens.Issue(user.Id, user.CommunityId, user.Roles);
        return new SessionResponse(
            access.Token,
            access.ExpiresAt,
            refresh.Token,
            refresh.ExpiresAt,
            new UserSummary(
                user.Id,
                user.FullName,
                user.EmailNormalized,
                user.Status == UserStatuses.Active,
                new CampusResponse(community?.ShortName ?? string.Empty, community?.Name ?? string.Empty),
                user.Roles));
    }
}
