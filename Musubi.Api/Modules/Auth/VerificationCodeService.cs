using System;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Email;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Auth;

/// <summary>Envia y verifica codigos de 6 digitos por proposito: verificar el correo (HU-03) o recuperar la contrasena (HU-56).</summary>
internal sealed class VerificationCodeService(IVerificationCodeRepository codes, IEmailSender email, TimeProvider time)
{
    /// <summary>Genera, guarda (solo el hash) y envia un codigo nuevo; reemplaza el anterior del mismo proposito.</summary>
    /// <param name="user">Destinatario.</param>
    /// <param name="purpose">Ver <see cref="VerificationPurposes"/>.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Vencimiento del codigo (UTC).</returns>
    public async Task<DateTime> SendAsync(UserDocument user, string purpose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = time.GetUtcNow().UtcDateTime;
        var plain = VerificationCodes.Generate();
        var expiresAt = now + VerificationCodes.Lifetime;
        await codes.UpsertAsync(
            new VerificationCodeDocument
            {
                Id = AuthIds.New(),
                UserId = user.Id,
                Purpose = purpose,
                CodeHash = VerificationCodes.Hash(user.Id, plain),
                LastSentAt = now,
                ExpiresAt = expiresAt,
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);

        var message = purpose == VerificationPurposes.PasswordReset
            ? AuthEmails.PasswordResetCode(user.EmailNormalized, user.FullName, plain)
            : AuthEmails.VerificationCode(user.EmailNormalized, user.FullName, plain);
        await email.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return expiresAt;
    }

    /// <summary>Si ya paso la espera minima entre envios (60 s) para ese proposito.</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="purpose">Proposito.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si se puede enviar otro codigo.</returns>
    public async Task<bool> CanSendAsync(string userId, string purpose, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var current = await codes.FindActiveAsync(userId, purpose, now, cancellationToken).ConfigureAwait(false);
        return current is null || now >= current.LastSentAt + VerificationCodes.ResendCooldown;
    }

    /// <summary>
    /// Verifica el codigo: vigente, menos de 5 intentos y comparacion en tiempo constante. Si es correcto lo
    /// borra (un solo uso); si no, suma el intento.
    /// </summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="purpose">Proposito.</param>
    /// <param name="code">Codigo presentado.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>null</c> si es valido; si no, <c>auth.invalid_code</c> o <c>auth.code_attempts_exceeded</c>.</returns>
    public async Task<ApiError?> VerifyAsync(string userId, string purpose, string code, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var stored = await codes.FindActiveAsync(userId, purpose, now, cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return AuthErrors.InvalidCode;
        }

        if (stored.Attempts >= VerificationCodes.MaxAttempts)
        {
            return AuthErrors.TooManyCodeAttempts;
        }

        if (!VerificationCodes.Matches(stored.CodeHash, userId, code))
        {
            var attempts = await codes.IncrementAttemptsAsync(stored.Id, cancellationToken).ConfigureAwait(false);
            return attempts >= VerificationCodes.MaxAttempts ? AuthErrors.TooManyCodeAttempts : AuthErrors.InvalidCode;
        }

        await codes.DeleteAsync(stored.Id, cancellationToken).ConfigureAwait(false);
        return null;
    }
}
