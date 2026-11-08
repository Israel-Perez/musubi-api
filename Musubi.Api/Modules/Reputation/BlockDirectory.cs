using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Reputation.Data;

namespace Musubi.Api.Modules.Reputation;

/// <summary>
/// Contrato publico de M-13 para los demas modulos (Modules/README: un modulo no lee las colecciones de otro).
/// HU-57: un bloqueo en cualquier direccion impide escribirse (chat) y ver las publicaciones y el perfil del
/// otro (vitrina, busqueda, detalle y perfil publico).
/// </summary>
internal interface IBlockDirectory
{
    /// <summary>Ids de las personas que alguien no debe ver: las que bloqueo y las que lo bloquearon.</summary>
    /// <param name="userId">La persona (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los ids (vacio sin bloqueos).</returns>
    Task<IReadOnlyCollection<string>> HiddenForAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Indica si hay un bloqueo entre dos personas, en cualquier direccion.</summary>
    /// <param name="userId">Una persona.</param>
    /// <param name="otherId">La otra.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si alguno bloqueo al otro.</returns>
    Task<bool> BetweenAsync(string userId, string otherId, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de bloqueos (sin cache: un bloqueo aplica en la siguiente peticion).</summary>
internal sealed class BlockDirectory(IBlockRepository blocks) : IBlockDirectory
{
    /// <inheritdoc />
    public Task<IReadOnlyCollection<string>> HiddenForAsync(string userId, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(userId)
            ? Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>())
            : blocks.FindRelatedAsync(userId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> BetweenAsync(string userId, string otherId, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(otherId)
            ? Task.FromResult(false)
            : blocks.ExistsBetweenAsync(userId, otherId, cancellationToken);
}
