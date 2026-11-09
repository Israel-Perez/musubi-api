using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>Campus de un miembro y su periodo academico (HU-04).</summary>
/// <param name="ShortName">Siglas ("UTTT").</param>
/// <param name="Name">Nombre completo.</param>
/// <param name="TermUnit">Periodo (<c>cuatrimestre</c>).</param>
/// <param name="TermCount">Numero de periodos.</param>
internal sealed record MemberCampus(string ShortName, string Name, string TermUnit, int TermCount);

/// <summary>
/// Cuenta de un miembro para su propio perfil (HU-04). Lleva el correo: solo se usa para responderle
/// a su titular o, sin el correo, para el perfil publico dentro del campus.
/// </summary>
/// <param name="Id">Id del usuario.</param>
/// <param name="CommunityId">Comunidad.</param>
/// <param name="FullName">Nombre completo (no se edita: viene del registro).</param>
/// <param name="Email">Correo institucional verificado.</param>
/// <param name="EmailVerifiedAt">Cuando lo verifico.</param>
/// <param name="CreatedAt">Alta.</param>
/// <param name="Active">Correo verificado y sin suspension.</param>
/// <param name="Campus">Campus y su periodo.</param>
internal sealed record MemberAccount(
    string Id,
    string CommunityId,
    string FullName,
    string Email,
    DateTime? EmailVerifiedAt,
    DateTime CreatedAt,
    bool Active,
    MemberCampus Campus);

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

    /// <summary>Datos minimos de varios miembros en una consulta (HU-15: nombres del tablero "Se busca").</summary>
    /// <param name="userIds">Ids de los usuarios.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Id → miembro; quien ya no existe no aparece.</returns>
    Task<IReadOnlyDictionary<string, MemberSummary>> FindMembersAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);

    /// <summary>Cuenta y campus del miembro (HU-04).</summary>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta o <c>null</c>.</returns>
    Task<MemberAccount?> FindAccountAsync(string userId, CancellationToken cancellationToken);
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

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, MemberSummary>> FindMembersAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return new Dictionary<string, MemberSummary>(StringComparer.Ordinal);
        }

        var found = await users.FindManyAsync(userIds, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(
            u => u.Id,
            u => new MemberSummary(u.Id, u.CommunityId, u.FullName, u.Status == UserStatuses.Active),
            StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<MemberAccount?> FindAccountAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        var community = await communities.FindByIdAsync(user.CommunityId, cancellationToken).ConfigureAwait(false);
        var term = community?.AcademicTerm ?? CommunityAcademicTerm.Default;
        return new MemberAccount(
            user.Id,
            user.CommunityId,
            user.FullName,
            user.EmailNormalized,
            user.EmailVerifiedAt,
            user.CreatedAt,
            user.Status == UserStatuses.Active,
            new MemberCampus(community?.ShortName ?? string.Empty, community?.Name ?? string.Empty, term.Unit, term.Count));
    }
}
