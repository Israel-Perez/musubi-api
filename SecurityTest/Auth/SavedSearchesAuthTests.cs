using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-12: las busquedas guardadas exigen sesion y son privadas (RNF-07, OWASP API1): nadie ve ni quita las de otra
/// persona, y una cuenta suspendida no guarda.
/// </summary>
public sealed class SavedSearchesAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<string> MemberAsync(string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        Assert.True(await production.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"ana.{id}@uttt.edu.mx",
                EmailNormalized = $"ana.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Ana Sofía Ramírez",
                Status = status,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private HttpClient ClientFor(string userId)
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, InMemoryCommunityRepository.UtttId, ["student"]);
        var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    [Theory]
    [InlineData("GET", "/api/v1/saved-searches")]
    [InlineData("POST", "/api/v1/saved-searches")]
    [InlineData("DELETE", "/api/v1/saved-searches/6716a0000000000000000001")]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = method == "POST" ? JsonContent.Create(new { q = "calculadora" }) : null,
        };

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SomeoneElse_NeitherSeesNorRemovesMine()
    {
        using var ana = ClientFor(await MemberAsync());
        using var luis = ClientFor(await MemberAsync());
        var saved = await AuthFlowJson(await ana.PostAsJsonAsync(new Uri("/api/v1/saved-searches", UriKind.Relative), new { q = "calculadora" }, Ct));
        var id = saved.GetProperty("id").GetString();

        var removed = await luis.DeleteAsync(new Uri($"/api/v1/saved-searches/{id}", UriKind.Relative), Ct);
        var luisSees = await AuthFlowJson(await luis.GetAsync(new Uri("/api/v1/saved-searches", UriKind.Relative), Ct));
        var anaSees = await AuthFlowJson(await ana.GetAsync(new Uri("/api/v1/saved-searches", UriKind.Relative), Ct));

        // Idempotente: no revela si existe.
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(0, luisSees.GetProperty("items").GetArrayLength());
        Assert.Equal(1, anaSees.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ASuspendedAccount_IsForbidden()
    {
        using var suspended = ClientFor(await MemberAsync(UserStatuses.Suspended));

        var response = await suspended.PostAsJsonAsync(new Uri("/api/v1/saved-searches", UriKind.Relative), new { q = "calculadora" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<System.Text.Json.JsonElement> AuthFlowJson(HttpResponseMessage response)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }
}
