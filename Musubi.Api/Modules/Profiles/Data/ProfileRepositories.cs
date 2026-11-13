using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Musubi.Api.Modules.Profiles.Data;

// Contratos de persistencia de M-02 (HU-04). Mongo* (nube y local) e InMemory* (UseInMemoryDatabase=true)
// pasan la misma bateria de pruebas (TestShared/Profiles).

/// <summary>Dueno de un perfil: lo que se guarda si el perfil aun no existe.</summary>
/// <param name="UserId">Id del usuario (<c>_id</c> del perfil).</param>
/// <param name="CommunityId">Comunidad.</param>
/// <param name="FullName">Nombre (copia R8).</param>
internal sealed record ProfileOwner(string UserId, string CommunityId, string FullName);

/// <summary>Carreras por comunidad.</summary>
internal interface ICareerRepository
{
    /// <summary>Carreras activas de la comunidad, en su orden.</summary>
    /// <param name="communityId">Comunidad.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las carreras.</returns>
    Task<IReadOnlyList<CareerDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken);
}

/// <summary>Perfiles. Cada escritura crea el documento si no existe (upsert) con los valores por defecto.</summary>
internal interface IProfileRepository
{
    /// <summary>El perfil, o <c>null</c> si la persona nunca lo edito.</summary>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil o <c>null</c>.</returns>
    Task<ProfileDocument?> FindAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Los perfiles que existan de varias personas (fotos del chat y de los bloqueados, HU-22/57).</summary>
    /// <param name="userIds">Ids de los usuarios (los que no tengan perfil no aparecen).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los perfiles encontrados, en cualquier orden.</returns>
    Task<IReadOnlyList<ProfileDocument>> FindManyAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);

    /// <summary>Guarda carrera y periodo ("Datos personales"); <c>null</c> los borra.</summary>
    /// <param name="owner">Dueno.</param>
    /// <param name="careerSlug">Carrera o <c>null</c>.</param>
    /// <param name="term">Periodo o <c>null</c>.</param>
    /// <param name="now">Momento del cambio.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil guardado.</returns>
    Task<ProfileDocument> SetDetailsAsync(ProfileOwner owner, string? careerSlug, int? term, DateTime now, CancellationToken cancellationToken);

    /// <summary>Guarda "Quien te ve".</summary>
    /// <param name="owner">Dueno.</param>
    /// <param name="visibility">Interruptores.</param>
    /// <param name="now">Momento del cambio.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil guardado.</returns>
    Task<ProfileDocument> SetVisibilityAsync(ProfileOwner owner, ProfileVisibility visibility, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Suma una calificacion recibida (HU-27, HU-45) y recalcula el promedio. Crea el perfil si aun no existe.
    /// </summary>
    /// <param name="owner">Quien la recibe.</param>
    /// <param name="stars">De 1 a 5.</param>
    /// <param name="onTime">Si la marcaron "Puntual".</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La tarea.</returns>
    Task AddRatingAsync(ProfileOwner owner, int stars, bool onTime, DateTime now, CancellationToken cancellationToken);

    /// <summary>Suma una venta o una compra completada (HU-26: "Ventas" y "Compras" del Perfil). Crea el perfil si no existe.</summary>
    /// <param name="owner">La persona.</param>
    /// <param name="asSeller"><c>true</c> suma una venta; <c>false</c>, una compra.</param>
    /// <param name="now">Ahora (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La tarea.</returns>
    Task AddTradeAsync(ProfileOwner owner, bool asSeller, DateTime now, CancellationToken cancellationToken);

    /// <summary>Cambia o quita la foto y devuelve la llave anterior, para borrarla de S3 (R11).</summary>
    /// <param name="owner">Dueno.</param>
    /// <param name="avatarKey">Llave nueva o <c>null</c>.</param>
    /// <param name="now">Momento del cambio.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La llave anterior o <c>null</c>.</returns>
    Task<string?> SetAvatarAsync(ProfileOwner owner, string? avatarKey, DateTime now, CancellationToken cancellationToken);

    /// <summary>Borra el perfil (HU-46, R11). Idempotente.</summary>
    /// <param name="userId">Id del usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task DeleteAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>
/// Carreras con cache-aside (02-02): las lee cada consulta del perfil. Se guardan por comunidad durante
/// <see cref="Platform.Caching.CachingServiceCollectionExtensions.CatalogTtl"/>.
/// NOTE (HU-41): borrar la entrada al editar una carrera.
/// </summary>
internal sealed class CachedCareerRepository(ICareerRepository inner, Platform.Caching.AppCache cache) : ICareerRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CareerDocument>> ListActiveAsync(string communityId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(
            "careers:" + communityId,
            Platform.Caching.CachingServiceCollectionExtensions.CatalogTtl,
            ct => inner.ListActiveAsync(communityId, ct),
            cancellationToken);
}
