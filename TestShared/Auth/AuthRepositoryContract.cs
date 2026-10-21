using Musubi.Api.Modules.Auth.Data;

namespace Musubi.TestShared.Auth;

/// <summary>
/// 02-01: comportamiento esperado de los repositorios de identidad. La misma bateria corre contra los fakes
/// en memoria (UnitTest) y contra MongoDB real con los scripts de db/ aplicados (DatabaseTest), para que el
/// fake nunca se aleje de la base. Cada prueba usa datos unicos: la base de Mongo se comparte en la clase.
/// </summary>
public abstract class AuthRepositoryContract
{
    /// <summary>Instante fijo en segundos enteros (Mongo guarda milisegundos).</summary>
    protected static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    internal abstract ICommunityRepository Communities { get; }

    internal abstract IUserRepository Users { get; }

    internal abstract IVerificationCodeRepository Codes { get; }

    internal abstract IRefreshTokenRepository Tokens { get; }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string UniqueEmail() => $"a{Guid.NewGuid():N}@uttt.edu.mx";

    private async Task<UserDocument> NewUserAsync(string? email = null)
    {
        var community = await Communities.FindByEmailDomainAsync("uttt.edu.mx", Ct).ConfigureAwait(false);
        var normalized = email ?? UniqueEmail();
        return new UserDocument
        {
            Id = AuthIds.New(),
            CommunityId = community!.Id,
            Email = normalized,
            EmailNormalized = normalized,
            PasswordHash = "AQAAAAIAAYagAAAAEHashDePruebaNoReal==",
            FullName = "Persona de Prueba",
            Status = UserStatuses.PendingVerification,
            Consents = new UserConsents { TermsVersion = "2026-10", PrivacyVersion = "2026-10", AcceptedAt = Now },
            Roles = [UserRoles.Student],
            CreatedAt = Now,
            UpdatedAt = Now,
        };
    }

    private static VerificationCodeDocument NewCode(string userId, string hash, DateTime expiresAt) => new()
    {
        Id = AuthIds.New(),
        UserId = userId,
        Purpose = VerificationPurposes.EmailVerification,
        CodeHash = hash,
        LastSentAt = Now,
        ExpiresAt = expiresAt,
        CreatedAt = Now,
    };

    private static RefreshTokenDocument NewToken(string userId, string familyId, DateTime expiresAt) => new()
    {
        Id = AuthIds.New(),
        UserId = userId,
        FamilyId = familyId,
        TokenHash = $"{Guid.NewGuid():N}{Guid.NewGuid():N}",
        ExpiresAt = expiresAt,
        Device = new RefreshTokenDevice { UserAgent = "pruebas" },
        CreatedAt = Now,
    };

    [Fact]
    public async Task Communities_FindByUtttDomain_ReturnsSeededCampus()
    {
        var community = await Communities.FindByEmailDomainAsync("uttt.edu.mx", Ct);

        Assert.NotNull(community);
        Assert.Equal("uttt", community.Slug);
        Assert.Equal("UTTT", community.ShortName);
        Assert.Equal(CommunityStatuses.Active, community.Status);
        Assert.Equal(community.Id, (await Communities.FindByIdAsync(community.Id, Ct))?.Id);
    }

    [Fact]
    public async Task Communities_FindByPersonalDomain_ReturnsNull()
    {
        Assert.Null(await Communities.FindByEmailDomainAsync("gmail.com", Ct));
    }

    [Fact]
    public async Task Users_Insert_ThenFindByEmailAndId_RoundTripsAllFields()
    {
        var user = await NewUserAsync();

        Assert.True(await Users.TryInsertAsync(user, Ct));
        var byEmail = await Users.FindByEmailAsync(user.EmailNormalized, Ct);
        var byId = await Users.FindByIdAsync(user.Id, Ct);

        Assert.NotNull(byEmail);
        Assert.Equal(user.Id, byId?.Id);
        Assert.Equal(user.CommunityId, byEmail.CommunityId);
        Assert.Equal(user.FullName, byEmail.FullName);
        Assert.Equal(UserStatuses.PendingVerification, byEmail.Status);
        Assert.Null(byEmail.EmailVerifiedAt);
        Assert.Equal("2026-10", byEmail.Consents.PrivacyVersion);
        Assert.Equal(Now, byEmail.Consents.AcceptedAt);
        Assert.Equal([UserRoles.Student], byEmail.Roles);
    }

    [Fact]
    public async Task Users_InsertSameEmailTwice_SecondReturnsFalse()
    {
        var email = UniqueEmail();

        Assert.True(await Users.TryInsertAsync(await NewUserAsync(email), Ct));
        Assert.False(await Users.TryInsertAsync(await NewUserAsync(email), Ct));
    }

    [Fact]
    public async Task Users_MarkEmailVerified_ActivatesOnlyOnce()
    {
        var user = await NewUserAsync();
        await Users.TryInsertAsync(user, Ct);
        var verifiedAt = Now.AddMinutes(3);

        Assert.True(await Users.MarkEmailVerifiedAsync(user.Id, verifiedAt, Ct));
        Assert.False(await Users.MarkEmailVerifiedAsync(user.Id, verifiedAt.AddMinutes(1), Ct));

        var stored = await Users.FindByIdAsync(user.Id, Ct);
        Assert.Equal(UserStatuses.Active, stored?.Status);
        Assert.Equal(verifiedAt, stored?.EmailVerifiedAt);
    }

    [Fact]
    public async Task Users_UpdatePasswordHash_ReplacesOnlyTheHash()
    {
        var user = await NewUserAsync();
        await Users.TryInsertAsync(user, Ct);

        Assert.True(await Users.UpdatePasswordHashAsync(user.Id, "AQAAAAIAAYagAAAAEHashNuevoNoReal==", Now.AddDays(1), Ct));
        Assert.False(await Users.UpdatePasswordHashAsync(AuthIds.New(), "AQAAAAIAAYagAAAAEHashNuevoNoReal==", Now, Ct));

        var stored = await Users.FindByIdAsync(user.Id, Ct);
        Assert.Equal("AQAAAAIAAYagAAAAEHashNuevoNoReal==", stored?.PasswordHash);
        Assert.Equal(Now.AddDays(1), stored?.UpdatedAt);
        Assert.Equal(user.FullName, stored?.FullName);
    }

    [Fact]
    public async Task Users_Delete_RemovesTheUser()
    {
        var user = await NewUserAsync();
        await Users.TryInsertAsync(user, Ct);

        Assert.True(await Users.DeleteAsync(user.Id, Ct));
        Assert.Null(await Users.FindByIdAsync(user.Id, Ct));
        Assert.False(await Users.DeleteAsync(user.Id, Ct));
    }

    [Fact]
    public async Task Codes_FindActive_HonorsExpirationEvenBeforeTtlRuns()
    {
        var userId = AuthIds.New();
        await Codes.UpsertAsync(NewCode(userId, new string('a', 64), Now.AddMinutes(10)), Ct);

        Assert.NotNull(await Codes.FindActiveAsync(userId, VerificationPurposes.EmailVerification, Now, Ct));
        Assert.Null(await Codes.FindActiveAsync(userId, VerificationPurposes.EmailVerification, Now.AddMinutes(10), Ct));
        Assert.Null(await Codes.FindActiveAsync(userId, VerificationPurposes.PasswordReset, Now, Ct));
    }

    [Fact]
    public async Task Codes_UpsertTwice_KeepsOneCodeWithSameIdAndResetsAttempts()
    {
        var userId = AuthIds.New();
        await Codes.UpsertAsync(NewCode(userId, new string('a', 64), Now.AddMinutes(10)), Ct);
        var first = await Codes.FindActiveAsync(userId, VerificationPurposes.EmailVerification, Now, Ct);
        await Codes.IncrementAttemptsAsync(first!.Id, Ct);

        await Codes.UpsertAsync(NewCode(userId, new string('b', 64), Now.AddMinutes(20)), Ct);
        var second = await Codes.FindActiveAsync(userId, VerificationPurposes.EmailVerification, Now, Ct);

        Assert.Equal(first.Id, second?.Id);
        Assert.Equal(new string('b', 64), second?.CodeHash);
        Assert.Equal(0, second?.Attempts);
        Assert.Equal(Now.AddMinutes(20), second?.ExpiresAt);
    }

    [Fact]
    public async Task Codes_IncrementAttempts_CountsAndReturnsNullWhenMissing()
    {
        var userId = AuthIds.New();
        await Codes.UpsertAsync(NewCode(userId, new string('c', 64), Now.AddMinutes(10)), Ct);
        var code = await Codes.FindActiveAsync(userId, VerificationPurposes.EmailVerification, Now, Ct);

        Assert.Equal(1, await Codes.IncrementAttemptsAsync(code!.Id, Ct));
        Assert.Equal(2, await Codes.IncrementAttemptsAsync(code.Id, Ct));

        await Codes.DeleteAsync(code.Id, Ct);
        Assert.Null(await Codes.IncrementAttemptsAsync(code.Id, Ct));
        Assert.Null(await Codes.FindActiveAsync(userId, VerificationPurposes.EmailVerification, Now, Ct));
    }

    [Fact]
    public async Task Tokens_TryConsume_WorksOnlyOnceAndOnlyWhileValid()
    {
        var userId = AuthIds.New();
        var token = NewToken(userId, AuthIds.New(), Now.AddDays(60));
        var expired = NewToken(userId, AuthIds.New(), Now);
        await Tokens.InsertAsync(token, Ct);
        await Tokens.InsertAsync(expired, Ct);

        var consumed = await Tokens.TryConsumeAsync(token.TokenHash, Now, Ct);

        Assert.Equal(token.Id, consumed?.Id);
        Assert.Null(consumed?.UsedAt);
        Assert.Null(await Tokens.TryConsumeAsync(token.TokenHash, Now, Ct));
        Assert.Equal(Now, (await Tokens.FindByHashAsync(token.TokenHash, Ct))?.UsedAt);
        Assert.Null(await Tokens.TryConsumeAsync(expired.TokenHash, Now, Ct));
        Assert.Null(await Tokens.TryConsumeAsync("no-existe", Now, Ct));
    }

    [Fact]
    public async Task Tokens_ConcurrentConsume_OnlyOneWins()
    {
        var token = NewToken(AuthIds.New(), AuthIds.New(), Now.AddDays(60));
        await Tokens.InsertAsync(token, Ct);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Tokens.TryConsumeAsync(token.TokenHash, Now, Ct)));

        Assert.Single(results, r => r is not null);
    }

    [Fact]
    public async Task Tokens_RevokeFamily_BlocksOnlyThatFamily()
    {
        var userId = AuthIds.New();
        var family = AuthIds.New();
        var a = NewToken(userId, family, Now.AddDays(60));
        var b = NewToken(userId, family, Now.AddDays(60));
        var other = NewToken(userId, AuthIds.New(), Now.AddDays(60));
        foreach (var t in new[] { a, b, other })
        {
            await Tokens.InsertAsync(t, Ct);
        }

        Assert.Equal(2, await Tokens.RevokeFamilyAsync(family, Now, Ct));
        Assert.Equal(0, await Tokens.RevokeFamilyAsync(family, Now, Ct));

        Assert.Null(await Tokens.TryConsumeAsync(a.TokenHash, Now, Ct));
        Assert.Equal(Now, (await Tokens.FindByHashAsync(b.TokenHash, Ct))?.RevokedAt);
        Assert.NotNull(await Tokens.TryConsumeAsync(other.TokenHash, Now, Ct));
    }

    [Fact]
    public async Task Tokens_RevokeAllForUser_EndsEverySession()
    {
        var userId = AuthIds.New();
        var tokens = new[] { NewToken(userId, AuthIds.New(), Now.AddDays(60)), NewToken(userId, AuthIds.New(), Now.AddDays(60)) };
        foreach (var t in tokens)
        {
            await Tokens.InsertAsync(t, Ct);
        }

        Assert.Equal(2, await Tokens.RevokeAllForUserAsync(userId, Now, Ct));
        foreach (var t in tokens)
        {
            Assert.Null(await Tokens.TryConsumeAsync(t.TokenHash, Now, Ct));
        }
    }
}
