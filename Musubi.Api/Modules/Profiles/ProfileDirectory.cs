using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Profiles.Data;

namespace Musubi.Api.Modules.Profiles;

/// <summary>
/// Contrato publico de M-02 para los demas modulos (Modules/README: un modulo no lee las colecciones de otro).
/// Lo usan el chat (foto de la otra persona, HU-20/22) y la lista de bloqueados (HU-57).
/// </summary>
internal interface IProfileDirectory
{
    /// <summary>Rutas de la foto de perfil (<c>/media/avatars/...</c>) de varias personas.</summary>
    /// <param name="userIds">Ids de los usuarios.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Id → ruta; quien no tiene foto no aparece (la app muestra sus iniciales).</returns>
    Task<IReadOnlyDictionary<string, string>> FindPhotosAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de perfiles (lectura directa: una foto nueva se ve al instante).</summary>
internal sealed class ProfileDirectory(IProfileRepository profiles) : IProfileDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> FindPhotosAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var found = await profiles.FindManyAsync(userIds, cancellationToken).ConfigureAwait(false);
        return found
            .Where(p => p.AvatarKey is not null)
            .ToDictionary(p => p.Id, p => ProfileService.PhotoPath(p.AvatarKey)!, StringComparer.Ordinal);
    }
}
