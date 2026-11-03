using System;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Auth;

/// <summary>
/// Cambiar la contrasena con sesion (HU-04, maqueta <c>Ajustes</c>). Pide la actual: una sesion robada no
/// basta para quedarse con la cuenta. Cierra todas las sesiones (tambien la de quien la robo) y abre una
/// nueva para este dispositivo, asi la app no vuelve a pedir el login (OWASP ASVS V3.3).
/// </summary>
internal sealed class PasswordChangeService(
    IUserRepository users,
    IRefreshTokenRepository tokens,
    PasswordHashing passwords,
    SessionIssuer sessions,
    TimeProvider time)
{
    /// <summary>Cambia la contrasena del usuario del token.</summary>
    /// <param name="userId">Id del usuario (del access token).</param>
    /// <param name="request">Contrasena actual y nueva.</param>
    /// <param name="userAgent">User-Agent del cliente (para la sesion nueva).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>
    /// La sesion nueva, <c>auth.wrong_password</c> (400: la app trata un 401 como sesion vencida),
    /// <c>auth.weak_password</c> o <c>auth.session_expired</c> si la cuenta ya no existe o no esta activa.
    /// </returns>
    public async Task<Result<SessionResponse>> ChangeAsync(string userId, ChangePasswordRequest request, string? userAgent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = string.IsNullOrEmpty(userId) ? null : await users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is not { Status: UserStatuses.Active })
        {
            passwords.VerifyAgainstDecoy(request.CurrentPassword);
            return AuthErrors.SessionExpired;
        }

        if (passwords.Verify(user.PasswordHash, request.CurrentPassword) == PasswordCheck.Failed)
        {
            return AuthErrors.WrongPassword;
        }

        if (!PasswordPolicy.IsValid(request.NewPassword))
        {
            return AuthErrors.WeakPassword;
        }

        var now = time.GetUtcNow().UtcDateTime;
        await users.UpdatePasswordHashAsync(user.Id, passwords.Hash(request.NewPassword), now, cancellationToken).ConfigureAwait(false);
        await tokens.RevokeAllForUserAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
        return Result<SessionResponse>.Ok(await sessions.StartAsync(user, userAgent, cancellationToken).ConfigureAwait(false));
    }
}
