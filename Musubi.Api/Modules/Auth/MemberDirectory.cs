using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Auth.Data;

namespace Musubi.Api.Modules.Auth;

/// <summary>Datos minimos de un miembro para otros modulos (sin correo ni credenciales, R10).</summary>
/// <param name="Id">Id del usuario.</param>
/// <param name="CommunityId">Comunidad.</param>
/// <param name="FullName">Nombre completo.</param>
/// <param name="Active">Correo verificado y sin suspension.</param>
internal sealed record MemberSummary(string Id, string CommunityId, string FullName, bool Active);

/// <summary>
/// Contrato publico de M-01 para los demas modulos (Modules/README: un modulo no lee las colecciones de otro).
/// </summary>
internal interface IMemberDirectory
{
    /// <summary>Id de la comunidad activa con ese identificador legible.</summary>
    /// <param name="slug">Identificador (<c>uttt</c>).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El id o <c>null</c>.</returns>
    Task<string?> FindCommunityIdAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Datos minimos del miembro.</summary>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El miembro o <c>null</c>.</returns>
    Task<MemberSummary?> FindMemberAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre los repositorios de M-01.</summary>
internal sealed class MemberDirectory(ICommunityRepository communities, IUserRepository users) : IMemberDirectory
{
    /// <inheritdoc />
    public async Task<string?> FindCommunityIdAsync(string slug, CancellationToken cancellationToken)
    {
        var community = await communities.FindBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
        return community is { Status: CommunityStatuses.Active } ? community.Id : null;
    }

    /// <inheritdoc />
    public async Task<MemberSummary?> FindMemberAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return user is null
            ? null
            : new MemberSummary(user.Id, user.CommunityId, user.FullName, user.Status == UserStatuses.Active);
    }
}
