using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Profiles.Data;

namespace IntegrationTest.Profiles;

/// <summary>HU-54: persistencia de apariencia por cuenta.</summary>
public sealed class PreferencesFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(string Id, HttpClient Client)> SignedInAsync()
    {
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var email = await flow.RegisterActiveAsync().ConfigureAwait(false);
        var login = await AuthFlow.JsonAsync(await flow.LoginAsync(email).ConfigureAwait(false)).ConfigureAwait(false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return (login.GetProperty("user").GetProperty("id").GetString()!, client);
    }

    [Fact]
    public async Task Theme_DefaultsToSystem_AndPersistsAcrossReads()
    {
        var (userId, client) = await SignedInAsync();
        using (client)
        {
            var initial = await client.GetFromJsonAsync<JsonElement>("/api/v1/profiles/me/preferences", Ct);
            Assert.Equal("system", initial.GetProperty("theme").GetString());

            foreach (var theme in new[] { "light", "dark", "system" })
            {
                var saved = await client.PutAsJsonAsync("/api/v1/profiles/me/preferences", new { theme }, Ct);
                Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
                Assert.Equal(theme, (await AuthFlow.JsonAsync(saved)).GetProperty("theme").GetString());

                var reloaded = await client.GetFromJsonAsync<JsonElement>("/api/v1/profiles/me/preferences", Ct);
                Assert.Equal(theme, reloaded.GetProperty("theme").GetString());
            }

            var repository = factory.Services.GetRequiredService<IPreferenceRepository>();
            Assert.Equal("system", (await repository.FindAsync(userId, Ct))?.Theme);
        }
    }

    [Fact]
    public async Task UnknownTheme_ReturnsCataloguedError_AndDoesNotPersist()
    {
        var (userId, client) = await SignedInAsync();
        using (client)
        {
            await AuthFlow.AssertCodeAsync(
                await client.PutAsJsonAsync("/api/v1/profiles/me/preferences", new { theme = "sepia" }, Ct),
                HttpStatusCode.BadRequest,
                "profiles.invalid_theme");

            var repository = factory.Services.GetRequiredService<IPreferenceRepository>();
            Assert.Null(await repository.FindAsync(userId, Ct));
        }
    }

    [Fact]
    public async Task AccountDeletion_AlsoDeletesThemePreference()
    {
        var (userId, client) = await SignedInAsync();
        using (client)
        {
            await client.PutAsJsonAsync("/api/v1/profiles/me/preferences", new { theme = "dark" }, Ct);
            var deleted = await client.PostAsJsonAsync("/api/v1/account/delete", new { password = AuthFlow.Password }, Ct);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

            var repository = factory.Services.GetRequiredService<IPreferenceRepository>();
            Assert.Null(await repository.FindAsync(userId, Ct));
        }
    }
}
