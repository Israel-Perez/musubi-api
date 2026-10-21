using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>02-03 / OWASP API2: solo se aceptan JWT HS256 vigentes, firmados con la llave, del emisor y audiencia esperados.</summary>
public sealed class JwtTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string UserId = "6716a0000000000000000001";
    private const string CommunityId = "000000000000000000000001";
    private static readonly Uri Protected = new("/api/v1/test-auth/protected", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Token artesanal para los casos de ataque (el bueno lo emite AccessTokenIssuer).</summary>
    private static string Craft(
        string? keyBase64 = null,
        string algorithm = SecurityAlgorithms.HmacSha256,
        string issuer = "musubi-api",
        string audience = "musubi-app",
        DateTime? expires = null,
        bool signed = true)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = now.AddHours(-2),
            NotBefore = now.AddHours(-2),
            Expires = expires ?? now.AddMinutes(10),
            Claims = new Dictionary<string, object> { [MusubiClaims.Subject] = UserId, [MusubiClaims.Role] = "admin" },
        };
        if (signed)
        {
            descriptor.SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Convert.FromBase64String(keyBase64 ?? SecurityApiFactory.SigningKey)), algorithm);
        }

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, string? token)
    {
        using var client = production.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.GetAsync(uri, Ct).ConfigureAwait(false);
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task EndpointWithoutAuthAttribute_IsProtectedByFallbackPolicy()
    {
        var response = await GetAsync(new Uri("/api/v1/test-auth/fallback", UriKind.Relative), token: null);

        await AssertProblemCodeAsync(response, HttpStatusCode.Unauthorized, "auth.unauthorized");
    }

    [Fact]
    public async Task ValidToken_IsAcceptedAndCarriesMinimalClaims()
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>().Issue(UserId, CommunityId, ["student"]);

        var response = await GetAsync(Protected, token.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(UserId, doc.RootElement.GetProperty("sub").GetString());
        Assert.Equal(CommunityId, doc.RootElement.GetProperty("community").GetString());
        Assert.Equal("student", doc.RootElement.GetProperty("roles")[0].GetString());
        var claims = new JsonWebToken(token.Token).Claims.Select(c => c.Type).ToHashSet();
        Assert.DoesNotContain("email", claims);
        Assert.DoesNotContain("name", claims);
    }

    [Fact]
    public async Task StudentToken_OnAdminEndpoint_Returns403WithCode()
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>().Issue(UserId, CommunityId, ["student"]);

        var response = await GetAsync(new Uri("/api/v1/test-auth/admin", UriKind.Relative), token.Token);

        await AssertProblemCodeAsync(response, HttpStatusCode.Forbidden, "auth.forbidden");
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var response = await GetAsync(Protected, Craft(expires: DateTime.UtcNow.AddMinutes(-5)));

        await AssertProblemCodeAsync(response, HttpStatusCode.Unauthorized, "auth.unauthorized");
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_IsRejected()
    {
        var response = await GetAsync(Protected, Craft(keyBase64: Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnsignedToken_AlgNone_IsRejected()
    {
        var token = Craft(signed: false);
        Assert.Equal("none", new JsonWebToken(token).Alg);

        var response = await GetAsync(Protected, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenWithOtherAlgorithmAndSameKey_IsRejected()
    {
        // Misma llave, HS512: solo se acepta HS256 (sin confusion de algoritmos).
        var response = await GetAsync(Protected, Craft(algorithm: SecurityAlgorithms.HmacSha512));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("otro-emisor", "musubi-app")]
    [InlineData("musubi-api", "otra-app")]
    public async Task TokenForAnotherIssuerOrAudience_IsRejected(string issuer, string audience)
    {
        var response = await GetAsync(Protected, Craft(issuer: issuer, audience: audience));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthProbes_StayAnonymous()
    {
        var live = await GetAsync(new Uri("/health", UriKind.Relative), token: null);
        var ready = await GetAsync(new Uri("/health/ready", UriKind.Relative), token: null);

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }
}
