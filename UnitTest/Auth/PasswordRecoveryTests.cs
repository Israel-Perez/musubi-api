using Microsoft.Extensions.Options;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;
using Musubi.TestShared;

namespace UnitTest.Auth;

/// <summary>
/// HU-56 (SCRUM-120: faltaba su prueba unitaria): "Olvidé mi contraseña". Pedir el codigo nunca revela si la cuenta
/// existe; con el codigo correcto se cambia la contrasena, el codigo no se reutiliza y se cierran todas las sesiones
/// (refresh tokens en la base y access tokens en la lista de revocacion).
/// </summary>
public sealed class PasswordRecoveryTests
{
    private const string OldPassword = "musubi2026";
    private const string NewPassword = "nueva2027clave";
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider time = new(Start);
    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryRefreshTokenRepository tokens = new();
    private readonly RecordingEmailSender emails = new();
    private readonly PasswordHashing passwords = new();
    private readonly List<string> revokedUsers = [];
    private readonly PasswordRecoveryService service;

    public PasswordRecoveryTests() =>
        service = new PasswordRecoveryService(
            users,
            tokens,
            new VerificationCodeService(new InMemoryVerificationCodeRepository(), emails, time),
            passwords,
            new RecordingRevocations(revokedUsers),
            time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<UserDocument> UserAsync(string status = UserStatuses.Active)
    {
        var email = $"ana.{Guid.NewGuid():N}@uttt.edu.mx";
        var user = new UserDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            Email = email,
            EmailNormalized = email,
            PasswordHash = passwords.Hash(OldPassword),
            FullName = "Ana Sofía Prueba",
            Status = status,
            Roles = [UserRoles.Student],
            CreatedAt = Start.UtcDateTime,
            UpdatedAt = Start.UtcDateTime,
        };
        await users.TryInsertAsync(user, Ct).ConfigureAwait(false);
        return user;
    }

    [Fact]
    public async Task Start_UnknownMalformedOrPending_GetTheSameAnswer_AndNoEmail()
    {
        var pending = await UserAsync(UserStatuses.PendingVerification);

        var unknown = await service.StartAsync(new RecoveryRequest("nadie@uttt.edu.mx"), Ct);
        var malformed = await service.StartAsync(new RecoveryRequest("no-es-correo"), Ct);
        var notActive = await service.StartAsync(new RecoveryRequest(pending.Email), Ct);

        Assert.Equal(unknown, malformed);
        Assert.Equal(unknown, notActive);
        Assert.Empty(emails.Sent);
    }

    [Fact]
    public async Task Start_AnActiveAccount_GetsASixDigitCode()
    {
        var ana = await UserAsync();

        await service.StartAsync(new RecoveryRequest(ana.Email.ToUpperInvariant()), Ct);

        Assert.Matches("^[0-9]{6}$", emails.LastCodeFor(ana.Email));
    }

    [Fact]
    public async Task Complete_RejectsAWeakPasswordOrAWrongCode_WithoutChangingAnything()
    {
        var ana = await UserAsync();
        await service.StartAsync(new RecoveryRequest(ana.Email), Ct);
        var code = emails.LastCodeFor(ana.Email);

        var weak = await service.CompleteAsync(new RecoveryResetRequest(ana.Email, code, "corta"), Ct);
        var wrong = await service.CompleteAsync(new RecoveryResetRequest(ana.Email, code == "000000" ? "111111" : "000000", NewPassword), Ct);
        var stranger = await service.CompleteAsync(new RecoveryResetRequest("nadie@uttt.edu.mx", code, NewPassword), Ct);

        Assert.Equal(AuthErrors.WeakPassword, weak.Error);
        Assert.Equal(AuthErrors.InvalidCode, wrong.Error);
        Assert.Equal(AuthErrors.InvalidCode, stranger.Error);
        Assert.NotEqual(PasswordCheck.Failed, passwords.Verify((await users.FindByIdAsync(ana.Id, Ct))!.PasswordHash, OldPassword));
        Assert.Empty(revokedUsers);
    }

    [Fact]
    public async Task Complete_ChangesThePassword_ClosesEverySession_AndTheCodeIsSingleUse()
    {
        var ana = await UserAsync();
        var refresh = new RefreshTokenService(tokens, Options.Create(new JwtOptions { SigningKey = Convert.ToBase64String(new byte[32]) }), time);
        var session = await refresh.IssueAsync(ana.Id, familyId: null, userAgent: "pruebas", Ct);
        await service.StartAsync(new RecoveryRequest(ana.Email), Ct);
        var code = emails.LastCodeFor(ana.Email);

        var done = await service.CompleteAsync(new RecoveryResetRequest(ana.Email, code, NewPassword), Ct);
        var reused = await service.CompleteAsync(new RecoveryResetRequest(ana.Email, code, "otra2028clave"), Ct);

        Assert.Null(done.Error);
        var hash = (await users.FindByIdAsync(ana.Id, Ct))!.PasswordHash;
        Assert.NotEqual(PasswordCheck.Failed, passwords.Verify(hash, NewPassword));
        Assert.Equal(PasswordCheck.Failed, passwords.Verify(hash, OldPassword));
        Assert.NotNull((await tokens.FindByHashAsync(RefreshTokenService.Hash(session.Token), Ct))!.RevokedAt);
        Assert.Equal([ana.Id], revokedUsers);
        Assert.Equal(AuthErrors.InvalidCode, reused.Error);
    }

    private sealed class RecordingRevocations(List<string> users) : IAccessTokenRevocations
    {
        public Task RevokeTokenAsync(string tokenId, DateTime expiresAt, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RevokeUserAsync(string userId, CancellationToken cancellationToken)
        {
            users.Add(userId);
            return Task.CompletedTask;
        }

        public Task<bool> IsRevokedAsync(string? tokenId, string? userId, long issuedAtMs, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
