using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;

namespace Musubi.Api.Modules.Admin;

/// <summary>
/// Quien puede administrar (RNF-03): el token dice <c>admin</c>, pero la base manda. Una cuenta suspendida o a la que
/// le quitaron el rol deja de administrar aunque su token siga vigente (HU-39, HU-40, HU-41).
/// </summary>
internal sealed class AdminGate(IAccountModeration accounts)
{
    /// <summary>La cuenta del administrador si esta activa y tiene el rol; si no, <c>null</c> (403).</summary>
    /// <param name="adminId">Usuario (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta o <c>null</c>.</returns>
    public async Task<ModeratedAccount?> ActiveAdminAsync(string adminId, CancellationToken cancellationToken)
    {
        var admin = await accounts.FindAsync(adminId, cancellationToken).ConfigureAwait(false);
        return admin is { IsAdmin: true, Status: UserStatuses.Active } ? admin : null;
    }
}
