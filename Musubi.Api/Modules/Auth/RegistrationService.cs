using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Email;
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
    IVerificationCodeRepository codes,
    IEmailSender email,
    PasswordHashing passwords,
    AccessTokenIssuer accessTokens,
    RefreshTokenService refreshTokens,
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

        var now = Now();
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

        var expiresAt = await SendCodeAsync(user, now, cancellationToken).ConfigureAwait(false);
        return Result<RegisterResponse>.Ok(new RegisterResponse(user.EmailNormalized, expiresAt, now + VerificationCodes.ResendCooldown));
    }

    /// <summary>Reenvia el codigo. Responde igual exista o no la cuenta (no revela correos registrados).</summary>
    /// <param name="rawEmail">Correo escrito por el usuario.</param>
    /// <param name="cancellationToken">Cancelacion.</param>
    /// <returns>Desde cuando se puede volver a pedir, o <c>auth.resend_too_soon</c>.</returns>
    public async Task<Result<ResendCodeResponse>> ResendAsync(string rawEmail, CancellationToken cancellationToken)
    {
        var now = Now();
        var generic = Result<ResendCodeResponse>.Ok(new ResendCodeResponse(now + VerificationCodes.ResendCooldown));
        if (!EmailAddresses.TryNormalize(rawEmail, out var normalized))
        {
            return generic;
        }

        var user = await users.FindByEmailAsync(normalized.Address, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status != UserStatuses.PendingVerification)
        {
            return generic;
        }

        var current = await codes.FindActiveAsync(user.Id, VerificationPurposes.EmailVerification, now, cancellationToken).ConfigureAwait(false);
        if (current is not null && now < current.LastSentAt + VerificationCodes.ResendCooldown)
        {
            return AuthErrors.ResendTooSoon;
        }

        await SendCodeAsync(user, now, cancellationToken).ConfigureAwait(false);
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
        var now = Now();
        if (!EmailAddresses.TryNormalize(request.Email, out var normalized))
        {
            return AuthErrors.InvalidCode;
        }

        var user = await users.FindByEmailAsync(normalized.Address, cancellationToken).ConfigureAwait(false);
        if (user is null || user.Status != UserStatuses.PendingVerification)
        {
            return AuthErrors.InvalidCode;
        }

        var code = await codes.FindActiveAsync(user.Id, VerificationPurposes.EmailVerification, now, cancellationToken).ConfigureAwait(false);
        if (code is null)
        {
            return AuthErrors.InvalidCode;
        }

        if (code.Attempts >= VerificationCodes.MaxAttempts)
        {
            return AuthErrors.TooManyCodeAttempts;
        }

        if (!VerificationCodes.Matches(code.CodeHash, user.Id, request.Code))
        {
            var attempts = await codes.IncrementAttemptsAsync(code.Id, cancellationToken).ConfigureAwait(false);
            return attempts >= VerificationCodes.MaxAttempts ? AuthErrors.TooManyCodeAttempts : AuthErrors.InvalidCode;
        }

        await codes.DeleteAsync(code.Id, cancellationToken).ConfigureAwait(false);
        if (!await users.MarkEmailVerifiedAsync(user.Id, now, cancellationToken).ConfigureAwait(false))
        {
            return AuthErrors.InvalidCode;
        }

        var community = await communities.FindByIdAsync(user.CommunityId, cancellationToken).ConfigureAwait(false);
        var access = accessTokens.Issue(user.Id, user.CommunityId, user.Roles);
        var refresh = await refreshTokens.IssueAsync(user.Id, familyId: null, userAgent, cancellationToken).ConfigureAwait(false);
        return Result<SessionResponse>.Ok(new SessionResponse(
            access.Token,
            access.ExpiresAt,
            refresh.Token,
            refresh.ExpiresAt,
            new UserSummary(
                user.Id,
                user.FullName,
                user.EmailNormalized,
                EmailVerified: true,
                new CampusResponse(community?.ShortName ?? string.Empty, community?.Name ?? string.Empty))));
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;

    private async Task<CommunityDocument?> FindActiveCommunityAsync(string domain, CancellationToken cancellationToken)
    {
        var community = await communities.FindByEmailDomainAsync(domain, cancellationToken).ConfigureAwait(false);
        return community?.Status == CommunityStatuses.Active ? community : null;
    }

    private async Task<DateTime> SendCodeAsync(UserDocument user, DateTime now, CancellationToken cancellationToken)
    {
        var plain = VerificationCodes.Generate();
        var expiresAt = now + VerificationCodes.Lifetime;
        await codes.UpsertAsync(
            new VerificationCodeDocument
            {
                Id = AuthIds.New(),
                UserId = user.Id,
                Purpose = VerificationPurposes.EmailVerification,
                CodeHash = VerificationCodes.Hash(user.Id, plain),
                LastSentAt = now,
                ExpiresAt = expiresAt,
                CreatedAt = now,
            },
            cancellationToken).ConfigureAwait(false);
        await email.SendAsync(AuthEmails.VerificationCode(user.EmailNormalized, user.FullName, plain), cancellationToken).ConfigureAwait(false);
        return expiresAt;
    }
}
