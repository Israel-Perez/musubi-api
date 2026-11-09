using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Profiles.Data;

namespace Musubi.Api.Modules.Profiles;

/// <summary>
/// Contrato publico de M-02 para los demas modulos (Modules/README: un modulo no lee las colecciones de otro).
/// Lo usan el chat (foto de la otra persona, HU-20/22), la lista de bloqueados (HU-57) y el tablero "Se busca"
/// (HU-15).
/// </summary>
internal interface IProfileDirectory
{
    /// <summary>Rutas de la foto de perfil (<c>/media/avatars/...</c>) de varias personas.</summary>
    /// <param name="userIds">Ids de los usuarios.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Id → ruta; quien no tiene foto no aparece (la app muestra sus iniciales).</returns>
    Task<IReadOnlyDictionary<string, string>> FindPhotosAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);

    /// <summary>
    /// Foto y carrera de varias personas de un campus (HU-15). La carrera solo sale si la persona tiene encendido
    /// "Mostrar mi carrera" (HU-04, RNF-05).
    /// </summary>
    /// <param name="communityId">Campus de quien consulta (las carreras son por campus).</param>
    /// <param name="userIds">Ids de los usuarios.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Id → tarjeta; quien no tiene perfil guardado no aparece (sin foto ni carrera).</returns>
    Task<IReadOnlyDictionary<string, ProfileCard>> FindCardsAsync(
        string communityId,
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken);
}

/// <summary>Lo que se muestra de una persona en una tarjeta (HU-15; HU-17 suma la reputacion).</summary>
/// <param name="Photo">Ruta de la foto (<c>/media/avatars/...</c>), o <c>null</c>.</param>
/// <param name="Career">Nombre corto de la carrera ("Ing. Software"), o <c>null</c> si no la tiene o la oculta.</param>
/// <param name="RatingAvg">Promedio de calificaciones (HU-27), o <c>null</c> sin calificaciones.</param>
/// <param name="RatingCount">Numero de calificaciones.</param>
internal sealed record ProfileCard(string? Photo, string? Career, double? RatingAvg = null, int RatingCount = 0);

/// <summary>Implementacion sobre el repositorio de perfiles (lectura directa: una foto nueva se ve al instante).</summary>
internal sealed class ProfileDirectory(IProfileRepository profiles, ICareerRepository careers) : IProfileDirectory
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, ProfileCard>> FindCardsAsync(
        string communityId,
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return new Dictionary<string, ProfileCard>(StringComparer.Ordinal);
        }

        var found = await profiles.FindManyAsync(userIds, cancellationToken).ConfigureAwait(false);
        // El catalogo de carreras va por la cache (CachedCareerRepository): una lectura por pagina, no por persona.
        var list = await careers.ListActiveAsync(communityId, cancellationToken).ConfigureAwait(false);
        return found.ToDictionary(
            p => p.Id,
            p => new ProfileCard(
                ProfileService.PhotoPath(p.AvatarKey),
                p.Visibility.Career && p.CareerSlug is { } slug
                    ? list.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.Ordinal))?.ShortName
                    : null,
                p.Stats.RatingAvg,
                p.Stats.RatingCount),
            StringComparer.Ordinal);
    }

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
