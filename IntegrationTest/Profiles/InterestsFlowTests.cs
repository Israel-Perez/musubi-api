using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Profiles.Data;

namespace IntegrationTest.Profiles;

/// <summary>HU-66: elegir, cambiar y borrar los intereses de la cuenta.</summary>
public sealed class InterestsFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Path = "/api/v1/profiles/me/interests";

    private static readonly string[] WithRepeats = ["libros", "laptops", "libros"];

    private static readonly string[] Drawing = ["dibujo"];

    private static readonly string[] Books = ["libros"];

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

    private static string[] Categories(JsonElement body) =>
        [.. body.GetProperty("categories").EnumerateArray().Select(c => c.GetString()!)];

    [Fact]
    public async Task Interests_StartEmpty_AndPersistWithoutRepeats()
    {
        var (_, client) = await SignedInAsync();
        using (client)
        {
            Assert.Empty(Categories(await client.GetFromJsonAsync<JsonElement>(Path, Ct)));

            var saved = await client.PutAsJsonAsync(Path, new { categories = WithRepeats }, Ct);

            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(["libros", "laptops"], Categories(await AuthFlow.JsonAsync(saved)));
            Assert.Equal(["libros", "laptops"], Categories(await client.GetFromJsonAsync<JsonElement>(Path, Ct)));
        }
    }

    [Fact]
    public async Task ChangingInterests_KeepsTheTheme_AndAnEmptyListClearsThem()
    {
        var (_, client) = await SignedInAsync();
        using (client)
        {
            await client.PutAsJsonAsync("/api/v1/profiles/me/preferences", new { theme = "dark" }, Ct);
            await client.PutAsJsonAsync(Path, new { categories = Drawing }, Ct);

            var cleared = await client.PutAsJsonAsync(Path, new { categories = Array.Empty<string>() }, Ct);
            var preferences = await client.GetFromJsonAsync<JsonElement>("/api/v1/profiles/me/preferences", Ct);

            Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
            Assert.Empty(Categories(await client.GetFromJsonAsync<JsonElement>(Path, Ct)));
            Assert.Equal("dark", preferences.GetProperty("theme").GetString());
        }
    }

    [Theory]
    [InlineData("motos")]
    [InlineData("")]
    public async Task UnknownCategory_ReturnsCataloguedError_AndDoesNotPersist(string slug)
    {
        var (userId, client) = await SignedInAsync();
        using (client)
        {
            await AuthFlow.AssertCodeAsync(
                await client.PutAsJsonAsync(Path, new { categories = new[] { "libros", slug } }, Ct),
                HttpStatusCode.BadRequest,
                "profiles.invalid_interest");

            Assert.Null(await factory.Services.GetRequiredService<IPreferenceRepository>().FindAsync(userId, Ct));
        }
    }

    [Fact]
    public async Task MissingOrTooLongList_FailsValidation()
    {
        var (_, client) = await SignedInAsync();
        using (client)
        {
            var tooMany = Enumerable.Repeat("libros", 21).ToArray();

            await AuthFlow.AssertCodeAsync(
                await client.PutAsJsonAsync(Path, new { categories = tooMany }, Ct), HttpStatusCode.BadRequest, "validation.failed");
            await AuthFlow.AssertCodeAsync(
                await client.PutAsJsonAsync(Path, new { }, Ct), HttpStatusCode.BadRequest, "validation.failed");
        }
    }

    [Fact]
    public async Task AccountDeletion_AlsoDeletesInterests()
    {
        var (userId, client) = await SignedInAsync();
        using (client)
        {
            await client.PutAsJsonAsync(Path, new { categories = Books }, Ct);
            var deleted = await client.PostAsJsonAsync("/api/v1/account/delete", new { password = AuthFlow.Password }, Ct);

            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Null(await factory.Services.GetRequiredService<IPreferenceRepository>().FindAsync(userId, Ct));
        }
    }
}
