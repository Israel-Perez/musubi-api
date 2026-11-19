using System.Security.Claims;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Musubi.Api.Platform.Auth;
using Musubi.TestShared;

namespace UnitTest.Auth;

/// <summary>SCRUM-120: lista de revocacion de access tokens en Redis (logout, suspension, contrasena, cuenta borrada).</summary>
public sealed class AccessTokenRevocationTests
{
    private const string UserId = "6716a0000000000000000001";
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider time = new(Start);
    private readonly RecordingCache cache = new();
    private readonly IOptions<JwtOptions> options = Options.Create(new JwtOptions
    {
        SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
    });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DistributedAccessTokenRevocations Revocations(IDistributedCache? store = null) =>
        new(store ?? cache, options, time, NullLogger<DistributedAccessTokenRevocations>.Instance);

    private long NowMs => time.GetUtcNow().ToUnixTimeMilliseconds();

    [Fact]
    public async Task RevokeToken_OnlyThatTokenIsRevoked_AndTheEntryLivesUntilItExpires()
    {
        var revocations = Revocations();
        var expiresAt = Start.UtcDateTime.AddMinutes(10);

        await revocations.RevokeTokenAsync("jti-1", expiresAt, Ct);

        Assert.True(await revocations.IsRevokedAsync("jti-1", UserId, NowMs, Ct));
        Assert.False(await revocations.IsRevokedAsync("jti-2", UserId, NowMs, Ct));
        var entry = cache.Options[DistributedAccessTokenRevocations.TokenKeyPrefix + "jti-1"];
        Assert.Equal(TimeSpan.FromMinutes(10) + AuthPlatformServiceCollectionExtensions.ClockSkew, entry.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task RevokeToken_AlreadyExpired_StoresNothing()
    {
        await Revocations().RevokeTokenAsync("jti-viejo", Start.UtcDateTime.AddMinutes(-5), Ct);

        Assert.Empty(cache.Options);
    }

    [Fact]
    public async Task RevokeUser_RevokesTokensIssuedBefore_ButNotTheOnesIssuedAfter()
    {
        var revocations = Revocations();
        var before = NowMs - 1;

        await revocations.RevokeUserAsync(UserId, Ct);
        var sameInstant = NowMs;
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.True(await revocations.IsRevokedAsync("jti-a", UserId, before, Ct));
        // La sesion nueva del cambio de contrasena se emite en el mismo instante o despues: sigue valida.
        Assert.False(await revocations.IsRevokedAsync("jti-b", UserId, sameInstant, Ct));
        Assert.False(await revocations.IsRevokedAsync("jti-c", UserId, NowMs, Ct));
        Assert.False(await revocations.IsRevokedAsync("jti-a", "otra-persona", before, Ct));
    }

    [Fact]
    public async Task RevokeUser_LivesAsLongAsAnAccessToken()
    {
        await Revocations().RevokeUserAsync(UserId, Ct);

        var entry = cache.Options[DistributedAccessTokenRevocations.UserKeyPrefix + UserId];
        Assert.Equal(TimeSpan.FromMinutes(options.Value.AccessTokenMinutes) + AuthPlatformServiceCollectionExtensions.ClockSkew, entry.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task CacheDown_NeverBreaksTheRequest()
    {
        var revocations = Revocations(new BrokenCache());

        await revocations.RevokeTokenAsync("jti-1", Start.UtcDateTime.AddMinutes(10), Ct);
        await revocations.RevokeUserAsync(UserId, Ct);

        // RNF-15: si Redis no responde se acepta el token; el refresh token (MongoDB) ya se revoco.
        Assert.False(await revocations.IsRevokedAsync("jti-1", UserId, NowMs, Ct));
    }

    [Fact]
    public async Task TokenWithoutIdOrSubject_IsNotRevokedByAccident()
    {
        var revocations = Revocations();
        await revocations.RevokeUserAsync(UserId, Ct);

        Assert.False(await revocations.IsRevokedAsync(null, null, 0, Ct));
    }

    [Fact]
    public void Issuer_StampsTheIssueTimeInMilliseconds()
    {
        time.Advance(TimeSpan.FromMilliseconds(437));

        var jwt = new JsonWebToken(new AccessTokenIssuer(options, time).Issue(UserId, "000000000000000000000001", ["student"]).Token);

        Assert.Equal(NowMs, jwt.GetPayloadValue<long>(MusubiClaims.IssuedAtMs));
        Assert.False(string.IsNullOrEmpty(jwt.Id));
    }

    [Fact]
    public void IssuedAt_PrefersMilliseconds_AndFallsBackToTheStartOfTheSecond()
    {
        var precise = new ClaimsPrincipal(new ClaimsIdentity([new Claim(MusubiClaims.IssuedAtMs, "1763380800437"), new Claim(JwtRegisteredClaimNames.Iat, "1763380800")]));
        var legacy = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Iat, "1763380800")]));
        var none = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Equal(1763380800437, AccessTokenClaims.IssuedAtMs(precise));
        Assert.Equal(1763380800000, AccessTokenClaims.IssuedAtMs(legacy));
        Assert.Equal(0, AccessTokenClaims.IssuedAtMs(none));
    }

    [Fact]
    public void ExpiresAt_ReadsExp()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Exp, "1763381700")]));

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1763381700).UtcDateTime, AccessTokenClaims.ExpiresAt(principal));
        Assert.Null(AccessTokenClaims.ExpiresAt(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private sealed class RecordingCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> values = new(StringComparer.Ordinal);

        public Dictionary<string, DistributedCacheEntryOptions> Options { get; } = new(StringComparer.Ordinal);

        public byte[]? Get(string key) => values.GetValueOrDefault(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            values[key] = value;
            Options[key] = options;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key)
        {
        }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => values.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class BrokenCache : IDistributedCache
    {
        private static TimeoutException Down() => new("Redis no responde");

        public byte[]? Get(string key) => throw Down();

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();

        public void Refresh(string key) => throw Down();

        public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();

        public void Remove(string key) => throw Down();

        public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();
    }
}
