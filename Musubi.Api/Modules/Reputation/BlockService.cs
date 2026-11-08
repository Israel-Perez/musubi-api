using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Profiles;
using Musubi.Api.Modules.Reputation.Data;
using Musubi.Api.Platform.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Reputation;

/// <summary>
/// Bloquear, desbloquear y "Usuarios bloqueados" (HU-57). Solo se bloquea a alguien del mismo campus; el
/// efecto (no escribirse ni ver publicaciones o perfil) lo aplican el chat, la vitrina y el perfil publico con
/// <see cref="IBlockDirectory"/>.
/// </summary>
internal sealed class BlockService(
    IBlockRepository blocks,
    IMemberDirectory members,
    IProfileDirectory profiles,
    TimeProvider time)
{
    /// <summary>Personas que devuelve la lista como maximo (sin paginar: nadie bloquea a cientos).</summary>
    public const int MaxListed = 200;

    /// <summary>Bloquea a otra persona. Idempotente: si ya estaba bloqueada responde igual.</summary>
    /// <param name="userId">Quien bloquea (del token).</param>
    /// <param name="request">A quien.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Exito o el error.</returns>
    public async Task<Result<bool>> BlockAsync(string userId, BlockRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var me = await members.FindMemberAsync(userId, cancellationToken).ConfigureAwait(false);
        if (me is not { Active: true })
        {
            return ApiErrors.Forbidden;
        }

        if (string.Equals(request.UserId, me.Id, StringComparison.Ordinal))
        {
            return ReputationErrors.BlockSelf;
        }

        var other = ObjectId.TryParse(request.UserId, out _)
            ? await members.FindMemberAsync(request.UserId, cancellationToken).ConfigureAwait(false)
            : null;
        // Otro campus es como si no existiera (RNF-21, OWASP API1). Una cuenta suspendida si se puede bloquear.
        if (other is null || !string.Equals(other.CommunityId, me.CommunityId, StringComparison.Ordinal))
        {
            return ReputationErrors.UserNotFound;
        }

        await blocks.InsertIfAbsentAsync(
            new BlockDocument
            {
                Id = MongoConventions.NewId(),
                CommunityId = me.CommunityId,
                BlockerId = me.Id,
                BlockedId = other.Id,
                BlockedName = other.FullName,
                CreatedAt = time.GetUtcNow().UtcDateTime,
            },
            cancellationToken).ConfigureAwait(false);
        return Result<bool>.Ok(true);
    }

    /// <summary>Quita el bloqueo ("Desbloquear"). Idempotente: si no estaba bloqueada no pasa nada.</summary>
    /// <param name="userId">Quien desbloquea (del token).</param>
    /// <param name="blockedId">A quien.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    public Task UnblockAsync(string userId, string blockedId, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(blockedId)
            ? Task.CompletedTask
            : blocks.DeleteAsync(userId, blockedId, cancellationToken);

    /// <summary>"Usuarios bloqueados": a quienes bloqueo, del mas reciente al mas viejo, con su foto.</summary>
    /// <param name="userId">Quien consulta (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La lista.</returns>
    public async Task<BlockedUsersResponse> ListAsync(string userId, CancellationToken cancellationToken)
    {
        var mine = await blocks.FindByBlockerAsync(userId, MaxListed, cancellationToken).ConfigureAwait(false);
        var photos = await profiles.FindPhotosAsync(mine.Select(b => b.BlockedId).ToList(), cancellationToken).ConfigureAwait(false);
        return new BlockedUsersResponse(mine
            .Select(b => new BlockedUserResponse(b.BlockedId, b.BlockedName, photos.GetValueOrDefault(b.BlockedId), b.CreatedAt))
            .ToList());
    }
}
