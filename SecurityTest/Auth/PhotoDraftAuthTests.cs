using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.GenAi;
using Musubi.Api.Modules.Listings;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-59: "Llenar con IA" exige sesion y solo lee una foto propia (OWASP API1): con la llave de otra persona, o con una
/// que no tiene la forma de una foto de producto, responde como si no existiera y no sale nada del servidor. En
/// Production sin llave de Gemini, la IA esta apagada (RNF-15) y comparte el tope por persona de la IA (OWASP API4).
/// </summary>
public sealed class PhotoDraftAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static readonly Uri DraftUri = new("/api/v1/products/photo-draft", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutSession_Is401()
    {
        using var client = production.CreateClient();

        var response = await client.PostAsJsonAsync(DraftUri, new { photo = ListingService.NewPhotoKey(AuthIds.New()) }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("ajena")]
    [InlineData("media/products/../../chat/secreto.webp")]
    [InlineData("chat/0123456789abcdef0123456789abcdef.webp")]
    [InlineData("https://evil.example/foto.webp")]
    public async Task APhotoThatIsNotMine_Is404(string photo)
    {
        var diego = await MemberAsync();
        using var client = ClientFor(diego);
        var key = photo == "ajena" ? ListingService.NewPhotoKey(await MemberAsync()) : photo;

        var response = await client.PostAsJsonAsync(DraftUri, new { photo = key }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("listings.photo_not_found", (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task WithoutTheKeyOrWithATooLongOne_Is400()
    {
        using var client = ClientFor(await MemberAsync());

        var empty = await client.PostAsJsonAsync(DraftUri, new { }, Ct);
        var huge = await client.PostAsJsonAsync(DraftUri, new { photo = new string('a', 5_000) }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, huge.StatusCode);
    }

    [Fact]
    public async Task InProduction_WithoutAGeminiKey_MyPhotoIs503_AndTheAiIsOff()
    {
        // Production sin llave de Gemini: no se lee la foto ni sale nada del servidor; se llena a mano (RNF-15).
        var diego = await MemberAsync();
        using var client = ClientFor(diego);

        var response = await client.PostAsJsonAsync(DraftUri, new { photo = ListingService.NewPhotoKey(diego) }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("listings.draft_unavailable", (await JsonAsync(response)).GetProperty("code").GetString());
        Assert.False(production.Services.GetRequiredService<IPhotoDraftWriter>().Enabled);
    }

    [Fact]
    public async Task ASuspendedAccount_IsForbidden()
    {
        var diego = await MemberAsync(UserStatuses.Suspended);
        using var client = ClientFor(diego);

        var response = await client.PostAsJsonAsync(DraftUri, new { photo = ListingService.NewPhotoKey(diego) }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FillingWithAi_HasThePerPersonLimitOfTheAi()
    {
        var diego = await MemberAsync();
        using var client = ClientFor(diego);
        var limit = new GenAiOptions().PermitLimit;

        for (var i = 0; i < limit; i++)
        {
            var allowed = await client.PostAsJsonAsync(DraftUri, new { photo = ListingService.NewPhotoKey(diego) }, Ct);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, allowed.StatusCode);
        }

        var rejected = await client.PostAsJsonAsync(DraftUri, new { photo = ListingService.NewPhotoKey(diego) }, Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("rate_limit.exceeded", await rejected.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return document.RootElement.Clone();
    }

    private async Task<string> MemberAsync(string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        Assert.True(await production.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"diego.{id}@uttt.edu.mx",
                EmailNormalized = $"diego.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Diego Hernández",
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
}
