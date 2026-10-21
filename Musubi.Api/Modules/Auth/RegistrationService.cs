using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Errors;

namespace Musubi.Api.Modules.Auth;

/// <summary>Versiones vigentes de los documentos legales (seccion <c>Legal</c>, HU-47).</summary>
internal sealed class LegalOptions
{
    /// <summary>Seccion de configuracion.</summary>
    public const string Section = "Legal";

    /// <summary>Version de los terminos y condiciones.</summary>
    public string TermsVersion { get; set; } = "2026-10";

    /// <summary>Version del aviso de privacidad.</summary>
    public string PrivacyVersion { get; set; } = "2026-10";
}

/// <summary>
/// Registro (HU-01), consentimientos (HU-47) y verificacion del correo institucional (HU-03).
/// Mientras el correo no se verifica, el usuario no tiene sesion: no puede publicar, pedir, ofertar,
/// conversar ni comprar (RN-01).
/// </summary>
internal sealed class RegistrationService(
    ICommunityRepository communities,
    IUserRepository users,
    VerificationCodeService codes,
    SessionIssuer sessions,
    PasswordHashing passwords,
    IOptions<LegalOptions> legal,
    TimeProvider time)
{
    /// <summary>Campus del correo, o <c>auth.email_not_institutional</c>.</summary>
    /// <param name="rawEmail">Correo escrito por el usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>El campus o el error.</returns>
    public async Task<Result<CampusResponse>> ResolveCampusAsync(string rawEmail, CancellationToken cancellationToken)
    {
        if (!EmailAddresses.TryNormalize(rawEmail, out var normalized))
        {
            return AuthErrors.EmailNotInstitutional;
        }

        var community = await FindActiveCommunityAsync(normalized.Domain, cancellationToken).ConfigureAwait(false);
        return community is null
            ? AuthErrors.EmailNotInstitutional
            : Result<CampusResponse>.Ok(new CampusResponse(community.ShortName, community.Name));
    }

    /// <summary>Crea la cuenta pendiente de verificar y envia el codigo.</summary>
    /// <param name="request">Datos del registro.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Cuando vence el codigo, o el error del catalogo.</returns>
    public async Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AcceptTerms != true || request.AcceptPrivacy != true)
        {
            return AuthErrors.ConsentRequired;
        }

        if (!PasswordPolicy.IsValid(request.Password))
        {
            return AuthErrors.WeakPassword;
        }

        if (!EmailAddresses.TryNormalize(request.Email, out var normalized))
        {
            return AuthErrors.EmailNotInstitutional;
        }

        var community = await FindActiveCommunityAsync(normalized.Domain, cancellationToken).ConfigureAwait(false);
        if (community is null)
        {
            return AuthErrors.EmailNotInstitutional;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var versions = legal.Value;
        var user = new UserDocument
        {
            Id = AuthIds.New(),
            CommunityId = community.Id,
            Email = request.Email.Trim(),
            EmailNormalized = normalized.Address,
            PasswordHash = passwords.Hash(request.Password),
            FullName = request.FullName.Trim(),
            Status = UserStatuses.PendingVerification,
            Consents = new UserConsents { TermsVersion = versions.TermsVersion, PrivacyVersion = versions.PrivacyVersion, AcceptedAt = now },
            Roles = [UserRoles.Student],
            CreatedAt = now,
            UpdatedAt = now,
        };

        // El indice unico decide la carrera entre dos registros simultaneos del mismo correo.
        if (!await users.TryInsertAsync(user, cancellationToken).ConfigureAwait(false))
        {
            return AuthErrors.EmailAlreadyRegistered;
        }

        var expiresAt = await codes.SendAsync(user, VerificationPurposes.EmailVerification, cancellationToken).ConfigureAwait(false);
        return Result<RegisterResponse>.Ok(new RegisterResponse(user.EmailNormalized, expiresAt, now + VerificationCodes.ResendCooldown));
    }

    /// <summary>Reenvia el codigo. Responde igual exista o no la cuenta (no revela correos registrados).</summary>
    /// <param name="rawEmail">Correo escrito por el usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Desde cuando se puede volver a pedir, o <c>auth.resend_too_soon</c>.</returns>
    public async Task<Result<ResendCodeResponse>> ResendAsync(string rawEmail, CancellationToken cancellationToken)
    {
        var generic = Result<ResendCodeResponse>.Ok(new ResendCodeResponse(time.GetUtcNow().UtcDateTime + VerificationCodes.ResendCooldown));
        if (!EmailAddresses.TryNormalize(rawEmail, out var normalized))
        {
            return generic;
        }

        var user = await users.FindByEmailAsync(normalized.Address, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status != UserStatuses.PendingVerification)
        {
            return generic;
        }

        if (!await codes.CanSendAsync(user.Id, VerificationPurposes.EmailVerification, cancellationToken).ConfigureAwait(false))
        {
            return AuthErrors.ResendTooSoon;
        }

        await codes.SendAsync(user, VerificationPurposes.EmailVerification, cancellationToken).ConfigureAwait(false);
        return generic;
    }

    /// <summary>Verifica el correo con el codigo y abre la sesion (maqueta "Verificacion-lista").</summary>
    /// <param name="request">Correo y codigo.</param>
    /// <param name="userAgent">User-Agent del cliente.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>La sesion, o el error del catalogo.</returns>
    public async Task<Result<SessionResponse>> ConfirmAsync(ConfirmCodeRequest request, string? userAgent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!EmailAddresses.TryNormalize(request.Email, out var normalized))
        {
            return AuthErrors.InvalidCode;
        }

        var user = await users.FindByEmailAsync(normalized.Address, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status != UserStatuses.PendingVerification)
        {
            return AuthErrors.InvalidCode;
        }

        if (await codes.VerifyAsync(user.Id, VerificationPurposes.EmailVerification, request.Code, cancellationToken).ConfigureAwait(false) is { } error)
        {
            return error;
        }

        var now = time.GetUtcNow().UtcDateTime;
        if (!await users.MarkEmailVerifiedAsync(user.Id, now, cancellationToken).ConfigureAwait(false))
        {
            return AuthErrors.InvalidCode;
        }

        var verified = await users.FindByIdAsync(user.Id, cancellationToken).ConfigureAwait(false);
        return Result<SessionResponse>.Ok(await sessions.StartAsync(verified ?? user, userAgent, cancellationToken).ConfigureAwait(false));
    }

    private async Task<CommunityDocument?> FindActiveCommunityAsync(string domain, CancellationToken cancellationToken)
    {
        var community = await communities.FindByEmailDomainAsync(domain, cancellationToken).ConfigureAwait(false);
        return community?.Status == CommunityStatuses.Active ? community : null;
    }
}
