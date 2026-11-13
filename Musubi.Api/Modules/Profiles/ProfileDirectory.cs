using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Musubi.Api.Modules.Profiles.Data;

namespace Musubi.Api.Modules.Profiles;

/// <summary>
/// Contrato publico de M-02 para los demas modulos (Modules/README: un modulo no lee las colecciones de otro).
/// Lo usan el chat (foto de la otra persona, HU-20/22), la lista de bloqueados (HU-57), el tablero "Se busca"
/// (HU-15) y los puntos de entrega de los pedidos (HU-23).
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

    /// <summary>La carrera de una persona, para recomendarle su edificio (HU-23). Solo se usa con la propia.</summary>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El slug, o <c>null</c> si no la ha elegido.</returns>
    Task<string?> FindCareerSlugAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Lo que se muestra de una persona en una tarjeta (HU-15; HU-17 suma la reputacion).</summary>
/// <param name="Photo">Ruta de la foto (<c>/media/avatars/...</c>), o <c>null</c>.</param>
/// <param name="Career">Nombre corto de la carrera ("Ing. Software"), o <c>null</c> si no la tiene o la oculta.</param>
/// <param name="RatingAvg">Promedio de calificaciones (HU-27), o <c>null</c> sin calificaciones.</param>
/// <param name="RatingCount">Numero de calificaciones.</param>
// NOTE (HU-27): RatingAvg y RatingCount quedan en null/0 hasta que exista `ratings` (HU-27/HU-45).
internal sealed record ProfileCard(string? Photo, string? Career, double? RatingAvg = null, int RatingCount = 0);

/// <summary>Implementacion sobre el repositorio de perfiles (lectura directa: una foto nueva se ve al instante).</summary>
internal sealed class ProfileDirectory(IProfileRepository profiles, ICareerRepository careers) : IProfileDirectory
{
    /// <inheritdoc />
    public async Task<string?> FindCareerSlugAsync(string userId, CancellationToken cancellationToken) =>
        (await profiles.FindAsync(userId, cancellationToken).ConfigureAwait(false))?.CareerSlug;

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

/// <summary>Contrato de M-02 para M-13: sumar una calificacion recibida a la reputacion (HU-27, HU-45).</summary>
internal interface IProfileRatings
{
    /// <summary>Suma la calificacion y recalcula el promedio de quien la recibe.</summary>
    /// <param name="userId">Quien la recibe.</param>
    /// <param name="communityId">Su campus.</param>
    /// <param name="fullName">Su nombre (si aun no tiene perfil, se crea con el).</param>
    /// <param name="stars">De 1 a 5.</param>
    /// <param name="onTime">Si la marcaron "Puntual".</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La tarea.</returns>
    Task AddAsync(string userId, string communityId, string fullName, int stars, bool onTime, DateTime now, CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de perfiles.</summary>
internal sealed class ProfileRatings(IProfileRepository profiles) : IProfileRatings
{
    /// <inheritdoc />
    public Task AddAsync(string userId, string communityId, string fullName, int stars, bool onTime, DateTime now, CancellationToken cancellationToken) =>
        profiles.AddRatingAsync(new ProfileOwner(userId, communityId, fullName), stars, onTime, now, cancellationToken);
}

/// <summary>Contrato de M-02 para M-08: contar las ventas y compras completadas (HU-26).</summary>
internal interface IProfileTrades
{
    /// <summary>Suma una venta a quien vende y una compra a quien compra.</summary>
    /// <param name="sellerId">Quien vende.</param>
    /// <param name="sellerName">Su nombre (si aun no tiene perfil, se crea con el).</param>
    /// <param name="buyerId">Quien compra.</param>
    /// <param name="buyerName">Su nombre.</param>
    /// <param name="communityId">Campus del pedido.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La tarea.</returns>
    Task AddCompletedAsync(
        string sellerId,
        string sellerName,
        string buyerId,
        string buyerName,
        string communityId,
        DateTime now,
        CancellationToken cancellationToken);
}

/// <summary>Implementacion sobre el repositorio de perfiles.</summary>
internal sealed class ProfileTrades(IProfileRepository profiles) : IProfileTrades
{
    /// <inheritdoc />
    public async Task AddCompletedAsync(
        string sellerId,
        string sellerName,
        string buyerId,
        string buyerName,
        string communityId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await profiles.AddTradeAsync(new ProfileOwner(sellerId, communityId, sellerName), asSeller: true, now, cancellationToken).ConfigureAwait(false);
        await profiles.AddTradeAsync(new ProfileOwner(buyerId, communityId, buyerName), asSeller: false, now, cancellationToken).ConfigureAwait(false);
    }
}

