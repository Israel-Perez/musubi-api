using System;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Auth;

/// <summary>
/// Recuperar la contrasena (HU-56, maqueta "Olvidaste tu contrasena?"): codigo de 6 digitos al correo
/// institucional y contrasena nueva. Nunca revela si una cuenta existe.
/// </summary>
internal sealed class PasswordRecoveryService(
    IUserRepository users,
    IRefreshTokenRepository tokens,
    VerificationCodeService codes,
    PasswordHashing passwords,
    TimeProvider time)
{
    /// <summary>Envia el codigo si la cuenta existe, esta activa y paso la espera; responde igual en todos los casos.</summary>
    /// <param name="request">Correo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Desde cuando se puede volver a pedir.</returns>
    public async Task<ResendCodeResponse> StartAsync(RecoveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var generic = new ResendCodeResponse(time.GetUtcNow().UtcDateTime + VerificationCodes.ResendCooldown);
        if (!EmailAddresses.TryNormalize(request.Email, out var normalized))
        {
            return generic;
        }

        var user = await users.FindByEmailAsync(normalized.Address, cancellationToken).ConfigureAwait(false);
        if (user?.Status == UserStatuses.Active
            && await codes.CanSendAsync(user.Id, VerificationPurposes.PasswordReset, cancellationToken).ConfigureAwait(false))
        {
            await codes.SendAsync(user, VerificationPurposes.PasswordReset, cancellationToken).ConfigureAwait(false);
        }

        return generic;
    }

    /// <summary>Cambia la contrasena con el codigo y cierra todas las sesiones abiertas.</summary>
    /// <param name="request">Correo, codigo y contrasena nueva.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Exito, o el error del catalogo.</returns>
    public async Task<Result<bool>> CompleteAsync(RecoveryResetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!PasswordPolicy.IsValid(request.NewPassword))
        {
            return AuthErrors.WeakPassword;
        }

        var user = EmailAddresses.TryNormalize(request.Email, out var normalized)
            ? await users.FindByEmailAsync(normalized.Address, cancellationToken).ConfigureAwait(false)
            : null;
        if (user?.Status != UserStatuses.Active)
        {
            return AuthErrors.InvalidCode;
        }

        if (await codes.VerifyAsync(user.Id, VerificationPurposes.PasswordReset, request.Code, cancellationToken).ConfigureAwait(false) is { } error)
        {
            return error;
        }

        var now = time.GetUtcNow().UtcDateTime;
        await users.UpdatePasswordHashAsync(user.Id, passwords.Hash(request.NewPassword), now, cancellationToken).ConfigureAwait(false);
        // Quien tuviera la contrasena vieja (o una sesion robada) queda fuera.
        await tokens.RevokeAllForUserAsync(user.Id, now, cancellationToken).ConfigureAwait(false);
        return Result<bool>.Ok(true);
    }
}
