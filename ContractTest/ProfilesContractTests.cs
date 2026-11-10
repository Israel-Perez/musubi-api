using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;
using SkiaSharp;

namespace ContractTest;

/// <summary>
/// HU-04: formas JSON de /api/v1/profiles, /api/v1/careers y /api/v1/auth/password/change que consume musubi-app.
/// Cambiar un fixture = cambio de contrato: actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class ProfilesContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> FixtureAsync(string name)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    /// <summary>Nombres y tipos, recursivo en los objetos anidados.</summary>
    private static List<string> Shape(JsonElement element, string prefix = "") =>
        [.. element.EnumerateObject()
            .SelectMany(p => p.Value.ValueKind == JsonValueKind.Object
                ? Shape(p.Value, $"{prefix}{p.Name}.")
                : [$"{prefix}{p.Name}:{p.Value.ValueKind}"])
            .Order(StringComparer.Ordinal)];

    private static ByteArrayContent Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Orange);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var content = new ByteArrayContent(data.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    /// <summary>Registra y verifica una cuenta; devuelve el cliente con su sesion y su id.</summary>
    private static async Task<(HttpClient Client, string Id)> SignedInAsync(WebApplicationFactory<Program> app, RecordingEmailSender emails)
    {
        var client = app.CreateClient();
        var email = $"ana.{Guid.NewGuid():N}@uttt.edu.mx";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Ana Sofía Ramírez", password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        var confirmed = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await BodyAsync(confirmed).ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return (client, session.GetProperty("user").GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task MyProfilePublicProfileCareersAndPasswordChange_MatchTheirFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (ana, anaId) = await SignedInAsync(app, emails);
        var (luis, _) = await SignedInAsync(app, emails);
        using (ana)
        using (luis)
        {
            await ana.PutAsJsonAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), new { career = "ing-mecatronica", term = 7 }, Ct);
            using var form = new MultipartFormDataContent();
            using var file = Png();
            form.Add(file, "photo", "foto.png");
            var withPhoto = await BodyAsync(await ana.PutAsync(new Uri("/api/v1/profiles/me/photo", UriKind.Relative), form, Ct));
            var me = await BodyAsync(await ana.GetAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), Ct));
            var seen = await BodyAsync(await luis.GetAsync(new Uri($"/api/v1/profiles/{anaId}", UriKind.Relative), Ct));
            var careers = await BodyAsync(await ana.GetAsync(new Uri("/api/v1/careers", UriKind.Relative), Ct));
            var changed = await BodyAsync(await luis.PostAsJsonAsync(
                new Uri("/api/v1/auth/password/change", UriKind.Relative), new { currentPassword = "musubi2026", newPassword = "musubi2027" }, Ct));
            var careersFixture = await FixtureAsync("careers.json");

            Assert.Equal(Shape(await FixtureAsync("my-profile.json")), Shape(me));
            Assert.Equal(Shape(await FixtureAsync("my-profile.json")), Shape(withPhoto));
            Assert.Equal(Shape(await FixtureAsync("public-profile.json")), Shape(seen));
            Assert.Equal(Shape(careersFixture), Shape(careers));
            Assert.Equal(Shape(careersFixture.GetProperty("careers")[0]), Shape(careers.GetProperty("careers")[0]));
            Assert.Equal(Shape(await FixtureAsync("session.json")), Shape(changed));
        }
    }

    [Fact]
    public async Task Preferences_MatchFixturesForDefaultAndUpdate()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var (client, _) = await SignedInAsync(app, emails);
        using (client)
        {
            var initial = await BodyAsync(await client.GetAsync(new Uri("/api/v1/profiles/me/preferences", UriKind.Relative), Ct));
            var requestFixture = await FixtureAsync("update-preferences.json");
            var savedResponse = await client.PutAsJsonAsync(
                new Uri("/api/v1/profiles/me/preferences", UriKind.Relative), requestFixture, Ct);
            var saved = await BodyAsync(savedResponse);

            Assert.Equal(Shape(await FixtureAsync("preferences.json")), Shape(initial));
            Assert.Equal("system", initial.GetProperty("theme").GetString());
            Assert.Equal(Shape(await FixtureAsync("preferences.json")), Shape(saved));
            Assert.Equal("dark", saved.GetProperty("theme").GetString());
        }
    }
}
