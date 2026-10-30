using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Auth;

/// <summary>
/// Borra los datos de una cuenta que pertenecen a un modulo (HU-46). M-01 no lee las colecciones
/// de otros modulos: cada uno registra su implementacion. NOTE (HU-04): perfil y foto.
/// NOTE (HU-20): conversaciones. NOTE (HU-23): pedidos y calificaciones se anonimizan (R11).
/// </summary>
internal interface IAccountDataEraser
{
    /// <summary>Borra los datos del usuario en el modulo. Idempotente: correrlo otra vez no falla.</summary>
    /// <param name="userId">Id de la cuenta.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task EraseAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>
/// Eliminar mi cuenta y mis datos (HU-46, RNF-08, borrado real R11). Pide la contrasena actual: con
/// una sesion robada no se puede borrar la cuenta. El usuario se borra al final, asi un fallo a
/// medias deja la cuenta viva y la persona puede reintentar.
/// </summary>
internal sealed partial class AccountDeletionService(
    IUserRepository users,
    IVerificationCodeRepository codes,
    IRefreshTokenRepository tokens,
    IEnumerable<IAccountDataEraser> erasers,
    PasswordHashing passwords,
    ILogger<AccountDeletionService> logger)
{
    /// <summary>Borra la cuenta del token si la contrasena coincide.</summary>
    /// <param name="userId">Id de la cuenta (del access token).</param>
    /// <param name="request">Contrasena actual.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Exito, <c>auth.wrong_password</c> o <c>auth.session_expired</c> si la cuenta ya no existe.</returns>
    public async Task<Result<bool>> DeleteAsync(string userId, DeleteAccountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = string.IsNullOrEmpty(userId) ? null : await users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            passwords.VerifyAgainstDecoy(request.Password);
            return AuthErrors.SessionExpired;
        }

        if (passwords.Verify(user.PasswordHash, request.Password) == PasswordCheck.Failed)
        {
            return AuthErrors.WrongPassword;
        }

        foreach (var eraser in erasers)
        {
            await eraser.EraseAsync(user.Id, cancellationToken).ConfigureAwait(false);
        }

        await codes.DeleteAllForUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
        await tokens.DeleteAllForUserAsync(user.Id, cancellationToken).ConfigureAwait(false);
        await users.DeleteAsync(user.Id, cancellationToken).ConfigureAwait(false);
        LogDeleted(logger, user.Id);
        return Result<bool>.Ok(true);
    }

    // Solo el id interno: ni correo ni nombre en los logs (RNF-05).
    [LoggerMessage(Level = LogLevel.Information, Message = "Cuenta eliminada a peticion de su titular: {UserId}")]
    private static partial void LogDeleted(ILogger logger, string userId);
}
