using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Profiles.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-04 / OWASP API1 y API3: el perfil y las carreras exigen sesion; el perfil publico solo existe dentro del
/// campus (de otro campus responde 404, igual que uno inexistente); nadie escribe el perfil de otra persona.
/// </summary>
public sealed class ProfilesAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string OtherCommunity = "000000000000000000000002";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> MemberOnly() => new()
    {
        { "GET", "/api/v1/profiles/me" },
        { "GET", "/api/v1/profiles/me/preferences" },
        { "PUT", "/api/v1/profiles/me/preferences" },
        { "PUT", "/api/v1/profiles/me" },
        { "PUT", "/api/v1/profiles/me/visibility" },
        { "PUT", "/api/v1/profiles/me/photo" },
        { "DELETE", "/api/v1/profiles/me/photo" },
        { "GET", "/api/v1/profiles/6716a0000000000000000001" },
        { "GET", "/api/v1/profiles/6716a0000000000000000001/ratings" },
        { "GET", "/api/v1/careers" },
        { "POST", "/api/v1/auth/password/change" },
    };

    [Theory]
    [MemberData(nameof(MemberOnly))]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PublicProfile_IsOnlyVisibleInsideTheCampus()
    {
        var member = await ActiveMemberAsync();
        using var sameCampus = ClientFor(AuthIds.New(), InMemoryCommunityRepository.UtttId);
        using var otherCampus = ClientFor(AuthIds.New(), OtherCommunity);

        var inside = await sameCampus.GetAsync(new Uri($"/api/v1/profiles/{member}", UriKind.Relative), Ct);
        var outside = await otherCampus.GetAsync(new Uri($"/api/v1/profiles/{member}", UriKind.Relative), Ct);
        var missing = await otherCampus.GetAsync(new Uri($"/api/v1/profiles/{AuthIds.New()}", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        // Misma respuesta que un perfil inexistente: no se puede saber si la cuenta existe en otro campus.
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("profiles.not_found", await CodeAsync(outside));
        Assert.Equal("profiles.not_found", await CodeAsync(missing));

        // HU-27/HU-45: las opiniones siguen la misma regla que el perfil.
        var opinionsInside = await sameCampus.GetAsync(new Uri($"/api/v1/profiles/{member}/ratings", UriKind.Relative), Ct);
        var opinionsOutside = await otherCampus.GetAsync(new Uri($"/api/v1/profiles/{member}/ratings", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, opinionsInside.StatusCode);
        Assert.Equal("profiles.not_found", await CodeAsync(opinionsOutside));
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("code").GetString();
    }

    [Theory]
    [InlineData("PUT", "/api/v1/profiles/6716a0000000000000000001")]
    [InlineData("PUT", "/api/v1/profiles/6716a0000000000000000001/photo")]
    [InlineData("DELETE", "/api/v1/profiles/6716a0000000000000000001/photo")]
    [InlineData("PUT", "/api/v1/profiles/6716a0000000000000000001/visibility")]
    [InlineData("PUT", "/api/v1/profiles/me/preferences/6716a0000000000000000001")]
    public async Task NoWriteRouteTakesAnId(string method, string path)
    {
        using var client = ClientFor(AuthIds.New(), InMemoryCommunityRepository.UtttId);
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(new { career = "ing-software", term = 7 }),
        };

        var response = await client.SendAsync(request, Ct);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"{method} {path} respondio {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Preferences_IgnoreAnyUserIdAndOnlyChangeTheTokenOwner()
    {
        var ownerId = await ActiveMemberAsync();
        var otherId = await ActiveMemberAsync();
        using var owner = ClientFor(ownerId, InMemoryCommunityRepository.UtttId);
        using var other = ClientFor(otherId, InMemoryCommunityRepository.UtttId);

        var saved = await owner.PutAsJsonAsync(
            new Uri("/api/v1/profiles/me/preferences", UriKind.Relative),
            new { theme = "dark", userId = otherId },
            Ct);
        var otherPreferences = await other.GetFromJsonAsync<JsonElement>(
            new Uri("/api/v1/profiles/me/preferences", UriKind.Relative),
            Ct);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("system", otherPreferences.GetProperty("theme").GetString());
        Assert.Null(await production.Services.GetRequiredService<IPreferenceRepository>().FindAsync(otherId, Ct));
    }

    private HttpClient ClientFor(string userId, string communityId)
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, communityId, ["student"]);
        var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    private async Task<string> ActiveMemberAsync()
    {
        var id = AuthIds.New();
        var inserted = await production.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"ana.{id}@uttt.edu.mx",
                EmailNormalized = $"ana.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Ana Sofía Ramírez",
                Status = UserStatuses.Active,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false);
        Assert.True(inserted);
        return id;
    }
}
