using System;
using System.Threading;
using System.Threading.Tasks;

namespace Musubi.Api.Modules.Auth.Data;

// Contratos de persistencia de M-01 (02-01). Hay dos implementaciones con el mismo comportamiento:
// Mongo* (nube y local) e InMemory* (UseInMemoryDatabase=true). La misma bateria de pruebas
// (TestShared/Auth) corre contra ambas. "now" llega como parametro: el reloj vive en el servicio.

/// <summary>Comunidades (campus).</summary>
internal interface ICommunityRepository
{
    /// <summary>Busca la comunidad activa o inactiva que tiene ese dominio de correo.</summary>
    /// <param name="emailDomain">Dominio en minusculas (<c>uttt.edu.mx</c>).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La comunidad o <c>null</c>.</returns>
    Task<CommunityDocument?> FindByEmailDomainAsync(string emailDomain, CancellationToken cancellationToken);

    /// <summary>Busca por id.</summary>
    /// <param name="id">Id de la comunidad.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La comunidad o <c>null</c>.</returns>
    Task<CommunityDocument?> FindByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Busca por identificador legible (<c>uttt</c>): la vitrina del visitante (HU-48).</summary>
    /// <param name="slug">Identificador en minusculas.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La comunidad o <c>null</c>.</returns>
    Task<CommunityDocument?> FindBySlugAsync(string slug, CancellationToken cancellationToken);
}

/// <summary>Usuarios (identidad y credenciales).</summary>
internal interface IUserRepository
{
    /// <summary>Inserta si el correo normalizado no existe (indice unico).</summary>
    /// <param name="user">Usuario nuevo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>false</c> si el correo ya estaba registrado.</returns>
    Task<bool> TryInsertAsync(UserDocument user, CancellationToken cancellationToken);

    /// <summary>Busca por id.</summary>
    /// <param name="id">Id del usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El usuario o <c>null</c>.</returns>
    Task<UserDocument?> FindByIdAsync(string id, CancellationToken cancellationToken);

    /// <summary>Busca por correo normalizado.</summary>
    /// <param name="emailNormalized">Correo en minusculas y sin espacios.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El usuario o <c>null</c>.</returns>
    Task<UserDocument?> FindByEmailAsync(string emailNormalized, CancellationToken cancellationToken);

    /// <summary>Pasa de <c>pendingVerification</c> a <c>active</c> de forma atomica (R7).</summary>
    /// <param name="id">Id del usuario.</param>
    /// <param name="verifiedAt">Momento de la verificacion (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si cambio el estado; <c>false</c> si no existia o ya estaba verificado.</returns>
    Task<bool> MarkEmailVerifiedAsync(string id, DateTime verifiedAt, CancellationToken cancellationToken);

    /// <summary>Reemplaza el hash de la contrasena (HU-56 o rehash al iniciar sesion).</summary>
    /// <param name="id">Id del usuario.</param>
    /// <param name="passwordHash">Hash nuevo.</param>
    /// <param name="updatedAt">Momento del cambio (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si el usuario existia.</returns>
    Task<bool> UpdatePasswordHashAsync(string id, string passwordHash, DateTime updatedAt, CancellationToken cancellationToken);

    /// <summary>Borrado real (HU-46, R11).</summary>
    /// <param name="id">Id del usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns><c>true</c> si existia.</returns>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken);
}

/// <summary>Codigos de verificacion (uno vigente por usuario y proposito).</summary>
internal interface IVerificationCodeRepository
{
    /// <summary>Crea o reemplaza el codigo del usuario para ese proposito (conserva el id si ya existia).</summary>
    /// <param name="code">Codigo nuevo (hash, vencimiento, intentos en 0).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task UpsertAsync(VerificationCodeDocument code, CancellationToken cancellationToken);

    /// <summary>Busca el codigo vigente (no vencido en <paramref name="now"/>).</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="purpose">Proposito.</param>
    /// <param name="now">Momento actual (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El codigo o <c>null</c>.</returns>
    Task<VerificationCodeDocument?> FindActiveAsync(string userId, string purpose, DateTime now, CancellationToken cancellationToken);

    /// <summary>Suma un intento fallido de forma atomica.</summary>
    /// <param name="id">Id del codigo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Los intentos despues de sumar, o <c>null</c> si el codigo ya no existe.</returns>
    Task<int?> IncrementAttemptsAsync(string id, CancellationToken cancellationToken);

    /// <summary>Borra el codigo (al usarse con exito).</summary>
    /// <param name="id">Id del codigo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task DeleteAsync(string id, CancellationToken cancellationToken);
}

/// <summary>Refresh tokens (rotacion con deteccion de reutilizacion, 02-03).</summary>
internal interface IRefreshTokenRepository
{
    /// <summary>Guarda un token nuevo.</summary>
    /// <param name="token">Token (solo hash).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task InsertAsync(RefreshTokenDocument token, CancellationToken cancellationToken);

    /// <summary>
    /// Marca como usado un token valido (sin usar, sin revocar y vigente) en una sola operacion atomica (R7):
    /// dos peticiones simultaneas con el mismo token nunca rotan las dos.
    /// </summary>
    /// <param name="tokenHash">Hash del token presentado.</param>
    /// <param name="now">Momento actual (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El token tal como estaba antes de marcarlo, o <c>null</c> si no era valido.</returns>
    Task<RefreshTokenDocument?> TryConsumeAsync(string tokenHash, DateTime now, CancellationToken cancellationToken);

    /// <summary>Busca por hash sin importar su estado (para detectar la reutilizacion).</summary>
    /// <param name="tokenHash">Hash del token.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El token o <c>null</c>.</returns>
    Task<RefreshTokenDocument?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Revoca todos los tokens vigentes de una familia (logout o reutilizacion detectada).</summary>
    /// <param name="familyId">Familia.</param>
    /// <param name="now">Momento actual (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se revocaron.</returns>
    Task<long> RevokeFamilyAsync(string familyId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Revoca todas las sesiones del usuario (cambio de contrasena, suspension, borrado).</summary>
    /// <param name="userId">Usuario.</param>
    /// <param name="now">Momento actual (UTC).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuantos se revocaron.</returns>
    Task<long> RevokeAllForUserAsync(string userId, DateTime now, CancellationToken cancellationToken);
}
