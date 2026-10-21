using System;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Auth;

/// <summary>
/// Iniciar y cerrar sesion (HU-02): login con correo y contrasena, renovacion con el refresh token
/// (sesion siempre viva salvo logout, L-16g) y logout que revoca la sesion del dispositivo (L-10).
/// </summary>
internal sealed class LoginService(
    IUserRepository users,
    IRefreshTokenRepository tokens,
    PasswordHashing passwords,
    RefreshTokenService refreshTokens,
    SessionIssuer sessions,
    TimeProvider time)
{
    /// <summary>Inicia sesion. El error no revela si el correo existe (OWASP API2).</summary>
    /// <param name="request">Correo y contrasena.</param>
    /// <param name="userAgent">User-Agent del cliente.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La sesion, o el error del catalogo.</returns>
    public async Task<Result<SessionResponse>> LoginAsync(LoginRequest request, string? userAgent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = EmailAddresses.TryNormalize(request.Email, out var normalized)
            ? await users.FindByEmailAsync(normalized.Address, cancellationToken).ConfigureAwait(false)
            : null;

        if (user is null)
        {
            // Mismo costo que una contrasena real: el tiempo de respuesta no delata si el correo existe.
            passwords.VerifyAgainstDecoy(request.Password);
            return AuthErrors.InvalidCredentials;
        }

        var check = passwords.Verify(user.PasswordHash, request.Password);
        if (check == PasswordCheck.Failed)
        {
            return AuthErrors.InvalidCredentials;
        }

        // Solo despues de una contrasena correcta se dice por que no puede entrar.
        if (user.Status == UserStatuses.PendingVerification)
        {
            return AuthErrors.EmailNotVerified;
        }

        if (user.Status == UserStatuses.Suspended)
        {
            return AuthErrors.AccountSuspended;
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            await users.UpdatePasswordHashAsync(user.Id, passwords.Hash(request.Password), Now(), cancellationToken).ConfigureAwait(false);
        }

        return Result<SessionResponse>.Ok(await sessions.StartAsync(user, userAgent, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Renueva la sesion: rota el refresh token y emite un access token nuevo.</summary>
    /// <param name="request">Refresh token vigente.</param>
    /// <param name="userAgent">User-Agent del cliente.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La sesion renovada, o <c>auth.session_expired</c>.</returns>
    public async Task<Result<SessionResponse>> RefreshAsync(RefreshRequest request, string? userAgent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rotation = await refreshTokens.RotateAsync(request.RefreshToken, userAgent, cancellationToken).ConfigureAwait(false);
        if (rotation.Status != RotationStatus.Rotated || rotation.UserId is null || rotation.Next is null)
        {
            return AuthErrors.SessionExpired;
        }

        var user = await users.FindByIdAsync(rotation.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status != UserStatuses.Active)
        {
            // Cuenta borrada o suspendida: la sesion no sigue aunque el token fuera valido.
            await tokens.RevokeFamilyAsync(rotation.Next.FamilyId, Now(), cancellationToken).ConfigureAwait(false);
            return AuthErrors.SessionExpired;
        }

        return Result<SessionResponse>.Ok(await sessions.BuildAsync(user, rotation.Next, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Cierra la sesion de este dispositivo. Idempotente: un token desconocido no es error.</summary>
    /// <param name="request">Refresh token de la sesion.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return refreshTokens.RevokeAsync(request.RefreshToken, cancellationToken);
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
