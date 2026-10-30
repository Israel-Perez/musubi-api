using Microsoft.Extensions.Logging.Abstractions;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Media;

namespace UnitTest.Auth;

/// <summary>HU-46: orden del borrado y que un fallo a medias no deje la cuenta a medio borrar sin poder reintentar.</summary>
public sealed class AccountDeletionTests
{
    private const string Password = "musubi2026";
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryUserRepository users = new();
    private readonly InMemoryVerificationCodeRepository codes = new();
    private readonly InMemoryRefreshTokenRepository tokens = new();
    private readonly PasswordHashing passwords = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingEraser(bool fail = false) : IAccountDataEraser
    {
        public List<string> Erased { get; } = [];

        public Task EraseAsync(string userId, CancellationToken cancellationToken)
        {
            if (fail)
            {
                throw new InvalidOperationException("Proveedor caido");
            }

            Erased.Add(userId);
            return Task.CompletedTask;
        }
    }

    private AccountDeletionService Service(params IAccountDataEraser[] erasers) =>
        new(users, codes, tokens, erasers, passwords, NullLogger<AccountDeletionService>.Instance);

    private async Task<UserDocument> ActiveUserAsync()
    {
        var user = new UserDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            Email = "ana@uttt.edu.mx",
            EmailNormalized = "ana@uttt.edu.mx",
            PasswordHash = passwords.Hash(Password),
            FullName = "Ana Prueba",
            Status = UserStatuses.Active,
            Consents = new UserConsents { TermsVersion = "2026-10", PrivacyVersion = "2026-10", AcceptedAt = Now },
            CreatedAt = Now,
        };
        Assert.True(await users.TryInsertAsync(user, Ct).ConfigureAwait(false));
        await tokens.InsertAsync(
            new RefreshTokenDocument
            {
                Id = AuthIds.New(),
                UserId = user.Id,
                FamilyId = AuthIds.New(),
                TokenHash = Guid.NewGuid().ToString("N"),
                ExpiresAt = Now.AddDays(60),
                CreatedAt = Now,
            },
            Ct).ConfigureAwait(false);
        return user;
    }

    [Fact]
    public async Task RightPassword_ErasesModulesThenSessionsThenTheUser()
    {
        var user = await ActiveUserAsync();
        var listings = new RecordingEraser();

        var result = await Service(listings).DeleteAsync(user.Id, new DeleteAccountRequest(Password), Ct);

        Assert.Null(result.Error);
        Assert.Equal([user.Id], listings.Erased);
        Assert.Null(await users.FindByIdAsync(user.Id, Ct));
        Assert.Equal(0, await tokens.DeleteAllForUserAsync(user.Id, Ct));
    }

    [Fact]
    public async Task WrongPassword_TouchesNothing()
    {
        var user = await ActiveUserAsync();
        var listings = new RecordingEraser();

        var result = await Service(listings).DeleteAsync(user.Id, new DeleteAccountRequest("otra-clave-1"), Ct);

        Assert.Equal("auth.wrong_password", result.Error?.Code);
        Assert.Empty(listings.Erased);
        Assert.NotNull(await users.FindByIdAsync(user.Id, Ct));
    }

    [Fact]
    public async Task ModuleFails_TheAccountStaysAndCanRetry()
    {
        var user = await ActiveUserAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(new RecordingEraser(fail: true)).DeleteAsync(user.Id, new DeleteAccountRequest(Password), Ct));

        Assert.NotNull(await users.FindByIdAsync(user.Id, Ct));
        var retry = await Service(new RecordingEraser()).DeleteAsync(user.Id, new DeleteAccountRequest(Password), Ct);
        Assert.Null(retry.Error);
    }

    [Fact]
    public async Task AccountAlreadyGone_IsSessionExpired()
    {
        var result = await Service().DeleteAsync(AuthIds.New(), new DeleteAccountRequest(Password), Ct);

        Assert.Equal("auth.session_expired", result.Error?.Code);
    }

    [Fact]
    public async Task InMemoryMedia_DeletePrefix_RemovesOnlyThatUser()
    {
        var media = new InMemoryMediaStorage();
        await media.PutAsync("media/products/aaa/1.webp", [1], "image/webp", Ct);
        await media.PutAsync("media/products/aaa/2.webp", [1], "image/webp", Ct);
        await media.PutAsync("media/products/aab/3.webp", [1], "image/webp", Ct);

        Assert.Equal(2, await media.DeletePrefixAsync("media/products/aaa/", Ct));
        Assert.False(await media.ExistsAsync("media/products/aaa/1.webp", Ct));
        Assert.True(await media.ExistsAsync("media/products/aab/3.webp", Ct));
    }
}
