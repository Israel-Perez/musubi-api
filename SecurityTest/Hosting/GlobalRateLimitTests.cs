using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Hosting;
using SecurityTest.Common;

namespace SecurityTest.Hosting;

/// <summary>Production con el tope global bajo, para alcanzarlo en una prueba.</summary>
public sealed class GlobalRateLimitApiFactory : SecurityApiFactory
{
    /// <summary>Peticiones por ventana en esta fabrica (con y sin sesion).</summary>
    public const int PermitLimit = 5;

    public GlobalRateLimitApiFactory()
        : base("Production")
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.ConfigureWebHost(builder);
        var limit = PermitLimit.ToString(CultureInfo.InvariantCulture);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [GlobalRateLimit.UserPermitLimitKey] = limit,
            [GlobalRateLimit.AnonymousPermitLimitKey] = limit,
            [GlobalRateLimit.WindowSecondsKey] = "3600",
        }));
    }
}

/// <summary>
/// SCRUM-125 / OWASP API4: ademas de los topes por ruta, cada cliente tiene un tope global con 429, Retry-After y
/// <c>rate_limit.exceeded</c>. Con sesion cuenta por persona, no por IP (el campus comparte IP), y las sondas del
/// balanceador nunca se limitan. Cada prueba usa su propia fabrica: los contadores viven en el proceso.
/// </summary>
public sealed class GlobalRateLimitTests
{
    private static readonly Uri Ping = new("/api/v1/ping", UriKind.Relative);
    private static readonly Uri Protected = new("/api/v1/test-auth/protected", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Token(GlobalRateLimitApiFactory factory, string userId) =>
        factory.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, InMemoryCommunityRepository.UtttId, []).Token;

    private static async Task<HttpStatusCode> GetAsync(HttpClient client, Uri uri, string? token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request, Ct).ConfigureAwait(false);
        return response.StatusCode;
    }

    [Fact]
    public async Task Anonymous_OverTheGlobalLimit_Returns429ProblemWithRetryAfter()
    {
        using var factory = new GlobalRateLimitApiFactory();
        using var client = factory.CreateClient();
        for (var i = 0; i < GlobalRateLimitApiFactory.PermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await GetAsync(client, Ping));
        }

        using var rejected = await client.GetAsync(Ping, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        using var doc = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync(Ct));
        Assert.Equal("rate_limit.exceeded", doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SignedIn_CountsPerPerson_SoAnotherPersonOnTheSameIpStillGetsIn()
    {
        using var factory = new GlobalRateLimitApiFactory();
        using var client = factory.CreateClient();
        var ana = Token(factory, "000000000000000000000a01");
        var beto = Token(factory, "000000000000000000000b02");
        for (var i = 0; i < GlobalRateLimitApiFactory.PermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await GetAsync(client, Protected, ana));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await GetAsync(client, Protected, ana));
        // Misma IP, otra persona: su propio tope. Y la IP sin sesion tampoco se gasto.
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, Protected, beto));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(client, Ping));
    }

    [Fact]
    public async Task AnInvalidToken_CountsAgainstTheIp_NotAsAFreshPerson()
    {
        using var factory = new GlobalRateLimitApiFactory();
        using var client = factory.CreateClient();
        for (var i = 0; i < GlobalRateLimitApiFactory.PermitLimit; i++)
        {
            // Un token inventado por peticion no abre una particion nueva cada vez.
            Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(client, Protected, $"inventado.{i}.token"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await GetAsync(client, Protected, "inventado.otro.token"));
    }

    [Fact]
    public async Task HealthProbes_AreNeverLimited()
    {
        using var factory = new GlobalRateLimitApiFactory();
        using var client = factory.CreateClient();
        var health = new Uri(PlatformPipelineExtensions.HealthPath, UriKind.Relative);

        for (var i = 0; i < GlobalRateLimitApiFactory.PermitLimit * 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await GetAsync(client, health));
        }
    }
}
