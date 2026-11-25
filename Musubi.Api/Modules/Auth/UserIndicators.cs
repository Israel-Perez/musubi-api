using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth.Data;

namespace Musubi.Api.Modules.Auth;

/// <summary>HU-42 (adopcion): las cuentas del campus.</summary>
/// <param name="Registered">Todas las cuentas.</param>
/// <param name="Validated">Las activas.</param>
/// <param name="Joined">Las que llegaron en el periodo.</param>
/// <param name="Active">Las que usaron la app en el periodo (empezaron o renovaron su sesion).</param>
internal sealed record Adoption(long Registered, long Validated, long Joined, long Active);

/// <summary>Contrato publico de M-01 para M-14 (HU-42).</summary>
internal interface IUserIndicators
{
    /// <summary>Adopcion del campus en el periodo.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="from">Inicio (UTC, inclusivo).</param>
    /// <param name="to">Fin (UTC, exclusivo).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los conteos.</returns>
    Task<Adoption> AdoptionAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken);

    /// <summary>De estas personas, las que son del campus.</summary>
    /// <param name="communityId">Campus.</param>
    /// <param name="userIds">Las personas.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las del campus.</returns>
    Task<IReadOnlyCollection<string>> OfCommunityAsync(string communityId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre los repositorios de M-01.</summary>
internal sealed class UserIndicators(IUserRepository users, IRefreshTokenRepository sessions) : IUserIndicators
{
    /// <inheritdoc />
    public async Task<Adoption> AdoptionAsync(string communityId, DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        var usage = await users.UsageAsync(communityId, from, to, cancellationToken).ConfigureAwait(false);
        var signedIn = await sessions.UsersSignedInAsync(from, to, cancellationToken).ConfigureAwait(false);
        var active = await OfCommunityAsync(communityId, signedIn, cancellationToken).ConfigureAwait(false);
        return new Adoption(usage.Registered, usage.Validated, usage.Joined, active.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> OfCommunityAsync(string communityId, IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return [];
        }

        var found = await users.FindManyAsync(userIds, cancellationToken).ConfigureAwait(false);
        return [.. found.Where(u => string.Equals(u.CommunityId, communityId, StringComparison.Ordinal)).Select(u => u.Id)];
    }
}
