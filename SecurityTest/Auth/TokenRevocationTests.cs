using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// SCRUM-120 / OWASP API2: un access token valido por firma deja de servir en cuanto se revoca (lista en Redis),
/// sin esperar sus 15 minutos. Los flujos reales (logout, suspension, contrasena) estan en IntegrationTest.
/// </summary>
public sealed class TokenRevocationTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string CommunityId = "000000000000000000000001";
    private static readonly Uri Protected = new("/api/v1/test-auth/protected", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewUserId() => Guid.NewGuid().ToString("N")[..24];

    private string Issue(string userId) =>
        production.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, CommunityId, ["student"]).Token;

    private static async Task<HttpStatusCode> CallAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Protected);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Ct).ConfigureAwait(false);
        return response.StatusCode;
    }

    [Fact]
    public async Task RevokedTokenId_Is401_AndAnotherTokenOfTheSamePersonStillWorks()
    {
        using var client = production.CreateClient();
        var userId = NewUserId();
        var revoked = Issue(userId);
        var other = Issue(userId);
        Assert.Equal(HttpStatusCode.OK, await CallAsync(client, revoked));

        var jwt = new JsonWebToken(revoked);
        await production.Services.GetRequiredService<IAccessTokenRevocations>().RevokeTokenAsync(jwt.Id, jwt.ValidTo, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync(client, revoked));
        Assert.Equal(HttpStatusCode.OK, await CallAsync(client, other));
    }

    [Fact]
    public async Task RevokedPerson_EveryOlderTokenIs401_AndANewSessionWorks()
    {
        using var client = production.CreateClient();
        var userId = NewUserId();
        var first = Issue(userId);
        var second = Issue(userId);
        await Task.Delay(5, Ct);

        await production.Services.GetRequiredService<IAccessTokenRevocations>().RevokeUserAsync(userId, Ct);
        await Task.Delay(5, Ct);
        var afterwards = Issue(userId);

        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync(client, first));
        Assert.Equal(HttpStatusCode.Unauthorized, await CallAsync(client, second));
        Assert.Equal(HttpStatusCode.OK, await CallAsync(client, afterwards));
        Assert.Equal(HttpStatusCode.OK, await CallAsync(client, Issue(NewUserId())));
    }

    [Fact]
    public async Task RevokedToken_GetsTheGeneric401Problem_WithoutSayingWhy()
    {
        using var client = production.CreateClient();
        var token = Issue(NewUserId());
        var jwt = new JsonWebToken(token);
        await production.Services.GetRequiredService<IAccessTokenRevocations>().RevokeTokenAsync(jwt.Id, jwt.ValidTo, Ct);

        using var request = new HttpRequestMessage(HttpMethod.Get, Protected);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("revoc", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RedisDown_TheTokenIsStillAccepted_TheApiDoesNotGoDown()
    {
        using var broken = production.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IDistributedCache>(new BrokenCache())));
        using var client = broken.CreateClient();
        var token = broken.Services.GetRequiredService<AccessTokenIssuer>().Issue(NewUserId(), CommunityId, ["student"]).Token;

        Assert.Equal(HttpStatusCode.OK, await CallAsync(client, token));
    }

    private sealed class BrokenCache : IDistributedCache
    {
        private static TimeoutException Down() => new("Redis no responde");

        public byte[]? Get(string key) => throw Down();

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromException<byte[]?>(Down());

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.FromException(Down());

        public void Refresh(string key) => throw Down();

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.FromException(Down());

        public void Remove(string key) => throw Down();

        public Task RemoveAsync(string key, CancellationToken token = default) => Task.FromException(Down());
    }
}
