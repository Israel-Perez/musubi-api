using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth.Data;

namespace Musubi.Api.Modules.Auth;

/// <summary>Configuracion del administrador (seccion <c>Admin</c>, HU-39).</summary>
internal sealed class AdminOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Admin";

    /// <summary>
    /// Correos institucionales que reciben el rol <c>admin</c> al iniciar sesion (Parameter Store
    /// <c>/musubi/prod/Admin/Emails/0</c>, <c>/1</c>...). Sacar un correo de la lista no quita el rol: se quita en
    /// la base (<c>db/README.md</c>).
    /// </summary>
    public IReadOnlyList<string> Emails { get; set; } = [];

    /// <summary>Indica si el correo (ya normalizado) esta en la lista.</summary>
    /// <param name="emailNormalized">Correo en minusculas y sin espacios.</param>
    /// <returns><c>true</c> si es de un administrador.</returns>
    public bool Includes(string emailNormalized) =>
        Emails.Any(email => EmailAddresses.TryNormalize(email, out var normalized)
            && string.Equals(normalized.Address, emailNormalized, StringComparison.Ordinal));
}

/// <summary>Una cuenta, como la ve un administrador (HU-40). Sin correo ni credenciales (R10).</summary>
/// <param name="Id">Usuario.</param>
/// <param name="CommunityId">Campus.</param>
/// <param name="FullName">Nombre completo.</param>
/// <param name="Status"><c>pendingVerification</c>, <c>active</c> o <c>suspended</c>.</param>
/// <param name="IsAdmin">Tiene el rol <c>admin</c>.</param>
/// <param name="CreatedAt">Alta (UTC).</param>
/// <param name="UpdatedAt">Ultimo cambio (UTC): con una suspension, cuando se suspendio.</param>
internal sealed record ModeratedAccount(
    string Id,
    string CommunityId,
    string FullName,
    string Status,
    bool IsAdmin,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Contrato publico de M-01 para M-14 (Modules/README: un modulo no lee las colecciones de otro).</summary>
internal interface IAccountModeration
{
    /// <summary>Una cuenta.</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta o <c>null</c>.</returns>
    Task<ModeratedAccount?> FindAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Varias cuentas en una consulta.</summary>
    /// <param name="userIds">Usuarios.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Id → cuenta; la que ya no existe no aparece.</returns>
    Task<IReadOnlyDictionary<string, ModeratedAccount>> FindManyAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);

    /// <summary>Las suspendidas de un campus, de la mas reciente a la mas vieja.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="limit">Maximo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las cuentas.</returns>
    Task<IReadOnlyList<ModeratedAccount>> ListSuspendedAsync(string communityId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Suspende una cuenta activa y cierra todas sus sesiones: no vuelve a entrar ni a renovar la sesion; su access
    /// token sigue valido hasta 15 minutos, pero el chat y los demas modulos ya la ven inactiva.
    /// </summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si estaba activa y se suspendio.</returns>
    Task<bool> SuspendAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Reactiva una cuenta suspendida: puede volver a iniciar sesion.</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si estaba suspendida y se reactivo.</returns>
    Task<bool> ReactivateAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre los repositorios de M-01.</summary>
internal sealed class AccountModeration(IUserRepository users, IRefreshTokenRepository tokens, TimeProvider time) : IAccountModeration
{
    /// <inheritdoc />
    public async Task<ModeratedAccount?> FindAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return user is null ? null : ToModerated(user);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ModeratedAccount>> FindManyAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return new Dictionary<string, ModeratedAccount>(StringComparer.Ordinal);
        }

        var found = await users.FindManyAsync(userIds, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(u => u.Id, ToModerated, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModeratedAccount>> ListSuspendedAsync(string communityId, int limit, CancellationToken cancellationToken) =>
        [.. (await users.FindByStatusAsync(communityId, UserStatuses.Suspended, limit, cancellationToken).ConfigureAwait(false)).Select(ToModerated)];

    /// <inheritdoc />
    public async Task<bool> SuspendAsync(string userId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (!await users.ChangeStatusAsync(userId, UserStatuses.Active, UserStatuses.Suspended, now, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        // Como al cambiar la contrasena: ningun dispositivo renueva la sesion.
        await tokens.RevokeAllForUserAsync(userId, now, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public Task<bool> ReactivateAsync(string userId, CancellationToken cancellationToken) =>
        users.ChangeStatusAsync(userId, UserStatuses.Suspended, UserStatuses.Active, time.GetUtcNow().UtcDateTime, cancellationToken);

    private static ModeratedAccount ToModerated(UserDocument user) => new(
        user.Id,
        user.CommunityId,
        user.FullName,
        user.Status,
        user.Roles.Contains(UserRoles.Admin, StringComparer.Ordinal),
        user.CreatedAt,
        user.UpdatedAt);
}
