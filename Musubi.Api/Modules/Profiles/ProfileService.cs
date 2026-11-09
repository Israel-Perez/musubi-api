using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Modules.Reputation;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Profiles;

/// <summary>
/// Mi perfil, "Datos personales", "Quien te ve" y el perfil publico (HU-04, RF-04). Solo se editan la carrera
/// y el periodo: el nombre y el correo vienen de la cuenta verificada (RNF-05). La visibilidad se aplica aqui,
/// no en la app (OWASP API3).
/// </summary>
internal sealed class ProfileService(
    IProfileRepository profiles,
    ICareerRepository careers,
    IMemberDirectory members,
    IBlockDirectory blocks,
    TimeProvider time)
{
    /// <summary>Prefijo de las fotos de perfil de un usuario (HU-46 las borra por prefijo).</summary>
    /// <param name="userId">Id del usuario.</param>
    /// <returns>El prefijo, terminado en <c>/</c>.</returns>
    public static string AvatarPrefix(string userId) => "media/avatars/" + userId + "/";

    /// <summary>Llave nueva para una foto de perfil (nunca se reutiliza: CloudFront la guarda en cache).</summary>
    /// <param name="userId">Id del usuario.</param>
    /// <returns>La llave.</returns>
    public static string NewAvatarKey(string userId) =>
        string.Create(CultureInfo.InvariantCulture, $"{AvatarPrefix(userId)}{Guid.NewGuid():N}.webp");

    /// <summary>La cuenta activa del usuario, o <c>null</c> si no existe o no esta activa.</summary>
    /// <param name="userId">Id del usuario (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La cuenta o <c>null</c>.</returns>
    public async Task<MemberAccount?> ActiveAccountAsync(string userId, CancellationToken cancellationToken)
    {
        var account = string.IsNullOrEmpty(userId) ? null : await members.FindAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        return account is { Active: true } ? account : null;
    }

    /// <summary>Mi perfil. Sin documento responde los valores por defecto y no crea nada.</summary>
    /// <param name="userId">Id del usuario (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil o el error.</returns>
    public async Task<Result<MyProfileResponse>> MeAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var profile = await profiles.FindAsync(account.Id, cancellationToken).ConfigureAwait(false);
        var list = await careers.ListActiveAsync(account.CommunityId, cancellationToken).ConfigureAwait(false);
        return Result<MyProfileResponse>.Ok(ToMine(account, profile, list));
    }

    /// <summary>Guarda carrera y periodo. El periodo cae dentro de la carrera, o del campus si no hay carrera.</summary>
    /// <param name="userId">Id del usuario (del token).</param>
    /// <param name="request">Carrera y periodo.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil o el error.</returns>
    public async Task<Result<MyProfileResponse>> UpdateAsync(string userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = await ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var list = await careers.ListActiveAsync(account.CommunityId, cancellationToken).ConfigureAwait(false);
        var career = request.Career is null ? null : list.FirstOrDefault(c => string.Equals(c.Slug, request.Career, StringComparison.Ordinal));
        if (request.Career is not null && career is null)
        {
            return ProfilesErrors.InvalidCareer;
        }

        var (first, last) = career is null ? (1, account.Campus.TermCount) : (career.FirstTerm, career.LastTerm);
        if (request.Term is { } term && (term < first || term > last))
        {
            return ProfilesErrors.InvalidTerm;
        }

        var saved = await profiles.SetDetailsAsync(Owner(account), career?.Slug, request.Term, Now(), cancellationToken).ConfigureAwait(false);
        return Result<MyProfileResponse>.Ok(ToMine(account, saved, list));
    }

    /// <summary>Guarda "Quien te ve".</summary>
    /// <param name="userId">Id del usuario (del token).</param>
    /// <param name="request">Interruptores.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil o el error.</returns>
    public async Task<Result<MyProfileResponse>> SetVisibilityAsync(string userId, VisibilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = await ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var visibility = new ProfileVisibility
        {
            Career = request.Career ?? true,
            Sales = request.Sales ?? true,
            Online = request.Online ?? false,
        };
        var saved = await profiles.SetVisibilityAsync(Owner(account), visibility, Now(), cancellationToken).ConfigureAwait(false);
        var list = await careers.ListActiveAsync(account.CommunityId, cancellationToken).ConfigureAwait(false);
        return Result<MyProfileResponse>.Ok(ToMine(account, saved, list));
    }

    /// <summary>
    /// Perfil de otra persona del mismo campus. Inexistente, inactiva, de otro campus o con un bloqueo entre las
    /// dos (HU-57) responde lo mismo (404), sin revelar si la cuenta existe. Lee el documento directo: un cambio
    /// se ve en la siguiente consulta.
    /// </summary>
    /// <param name="viewerId">Id del token de quien consulta.</param>
    /// <param name="viewerCommunityId">Comunidad del token de quien consulta.</param>
    /// <param name="id">Id del perfil.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El perfil o el error.</returns>
    public async Task<Result<PublicProfileResponse>> PublicAsync(string? viewerId, string? viewerCommunityId, string id, CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return ProfilesErrors.NotFound;
        }

        var account = await members.FindAccountAsync(id, cancellationToken).ConfigureAwait(false);
        if (account is not { Active: true }
            || !string.Equals(account.CommunityId, viewerCommunityId, StringComparison.Ordinal)
            || await blocks.BetweenAsync(viewerId ?? string.Empty, account.Id, cancellationToken).ConfigureAwait(false))
        {
            return ProfilesErrors.NotFound;
        }

        var profile = await profiles.FindAsync(account.Id, cancellationToken).ConfigureAwait(false);
        var visibility = profile?.Visibility ?? new ProfileVisibility();
        var stats = profile?.Stats ?? new ProfileStats();
        CareerResponse? career = null;
        int? term = null;
        if (visibility.Career)
        {
            var list = await careers.ListActiveAsync(account.CommunityId, cancellationToken).ConfigureAwait(false);
            career = FindCareer(list, profile?.CareerSlug);
            term = profile?.Term;
        }

        return Result<PublicProfileResponse>.Ok(new PublicProfileResponse(
            account.Id,
            account.FullName,
            new CampusResponse(account.Campus.ShortName, account.Campus.Name),
            PhotoPath(profile?.AvatarKey),
            account.EmailVerifiedAt ?? account.CreatedAt,
            career,
            term,
            new PublicStatsResponse(stats.RatingAvg, stats.RatingCount, visibility.Sales ? stats.Sales : null)));
    }

    /// <summary>Carreras activas del campus del usuario y su periodo.</summary>
    /// <param name="userId">Id del usuario (del token).</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Las carreras o el error.</returns>
    public async Task<Result<CareersResponse>> CareersAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await ActiveAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return ApiErrors.Forbidden;
        }

        var list = await careers.ListActiveAsync(account.CommunityId, cancellationToken).ConfigureAwait(false);
        return Result<CareersResponse>.Ok(new CareersResponse(
            new AcademicTermResponse(account.Campus.TermUnit, account.Campus.TermCount),
            list.Select(c => new CareerOption(c.Slug, c.Name, c.ShortName, c.Level, c.FirstTerm, c.LastTerm)).ToList()));
    }

    /// <summary>Dueno del perfil para un upsert.</summary>
    /// <param name="account">Cuenta.</param>
    /// <returns>El dueno.</returns>
    public static ProfileOwner Owner(MemberAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new ProfileOwner(account.Id, account.CommunityId, account.FullName);
    }

    /// <summary>Arma "mi perfil".</summary>
    /// <param name="account">Cuenta.</param>
    /// <param name="profile">Perfil o <c>null</c> (valores por defecto).</param>
    /// <param name="list">Carreras del campus.</param>
    /// <returns>La respuesta.</returns>
    public static MyProfileResponse ToMine(MemberAccount account, ProfileDocument? profile, IReadOnlyList<CareerDocument> list)
    {
        ArgumentNullException.ThrowIfNull(account);
        var visibility = profile?.Visibility ?? new ProfileVisibility();
        var stats = profile?.Stats ?? new ProfileStats();
        return new MyProfileResponse(
            account.Id,
            account.FullName,
            account.Email,
            account.EmailVerifiedAt,
            new CampusResponse(account.Campus.ShortName, account.Campus.Name),
            new AcademicTermResponse(account.Campus.TermUnit, account.Campus.TermCount),
            FindCareer(list, profile?.CareerSlug),
            profile?.Term,
            PhotoPath(profile?.AvatarKey),
            new VisibilityResponse(visibility.Career, visibility.Sales, visibility.Online),
            new MyStatsResponse(stats.RatingAvg, stats.RatingCount, stats.Sales, stats.Purchases));
    }

    private static CareerResponse? FindCareer(IReadOnlyList<CareerDocument> list, string? slug)
    {
        var career = slug is null ? null : list.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.Ordinal));
        return career is null ? null : new CareerResponse(career.Slug, career.Name, career.ShortName, career.FirstTerm, career.LastTerm);
    }

    /// <summary>Ruta publica de una foto de perfil (<c>/media/avatars/...</c>), o <c>null</c>.</summary>
    /// <param name="key">Llave en el bucket.</param>
    /// <returns>La ruta o <c>null</c>.</returns>
    public static string? PhotoPath(string? key) => key is null ? null : "/" + key;

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
