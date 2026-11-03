using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntegrationTest.Auth;
using SkiaSharp;

namespace IntegrationTest.Profiles;

/// <summary>
/// HU-04: editar mi perfil (maquetas <c>Perfil</c>, <c>Ajustes</c> y <c>Privacidad</c>) y ver el cambio al
/// instante en el perfil publico de otra persona del campus.
/// </summary>
public sealed class ProfileFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Png(SKColor color)
    {
        using var bitmap = new SKBitmap(600, 400);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Cuenta activa con sesion: id y cliente con el access token.</summary>
    private async Task<(string Id, HttpClient Client)> SignedInAsync()
    {
        // El JWT se emite con el reloj de la fabrica y JwtBearer lo valida con la hora real.
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

    private static Task<HttpResponseMessage> UpdateAsync(HttpClient client, object body) =>
        client.PutAsJsonAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), body, Ct);

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthFlow.JsonAsync(response).ConfigureAwait(false);
    }

    private static async Task<HttpResponseMessage> UploadPhotoAsync(HttpClient client, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        return await client.PutAsync(new Uri("/api/v1/profiles/me/photo", UriKind.Relative), form, Ct).ConfigureAwait(false);
    }

    [Fact]
    public async Task Edit_IsSeenAtOnceInThePublicProfile_OfSomeoneElseOfTheCampus()
    {
        var (anaId, ana) = await SignedInAsync();
        var (_, luis) = await SignedInAsync();
        using (ana)
        using (luis)
        {
            var before = await GetAsync(luis, $"/api/v1/profiles/{anaId}");
            var updated = await UpdateAsync(ana, new { career = "ing-mecatronica", term = 7 });
            var after = await GetAsync(luis, $"/api/v1/profiles/{anaId}");
            var me = await GetAsync(ana, "/api/v1/profiles/me");

            Assert.Equal(JsonValueKind.Null, before.GetProperty("career").ValueKind);
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            Assert.Equal("Ing. Mecatrónica", after.GetProperty("career").GetProperty("shortName").GetString());
            Assert.Equal(7, after.GetProperty("term").GetInt32());
            Assert.Equal("ing-mecatronica", me.GetProperty("career").GetProperty("slug").GetString());
            Assert.Equal("cuatrimestre", me.GetProperty("academicTerm").GetProperty("unit").GetString());
            Assert.False(after.TryGetProperty("email", out _));
        }
    }

    [Fact]
    public async Task TurningOffShowMyCareer_HidesCareerAndTerm()
    {
        var (anaId, ana) = await SignedInAsync();
        var (_, luis) = await SignedInAsync();
        using (ana)
        using (luis)
        {
            await UpdateAsync(ana, new { career = "tsu-ti-software", term = 4 });

            var saved = await ana.PutAsJsonAsync(
                new Uri("/api/v1/profiles/me/visibility", UriKind.Relative), new { career = false, sales = true, online = true }, Ct);
            var seen = await GetAsync(luis, $"/api/v1/profiles/{anaId}");

            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.False((await AuthFlow.JsonAsync(saved)).GetProperty("visibility").GetProperty("career").GetBoolean());
            Assert.Equal(JsonValueKind.Null, seen.GetProperty("career").ValueKind);
            Assert.Equal(JsonValueKind.Null, seen.GetProperty("term").ValueKind);
            Assert.Equal(0, seen.GetProperty("stats").GetProperty("sales").GetInt32());
        }
    }

    [Fact]
    public async Task Photo_UploadReplaceAndRemove_DeletesThePreviousFile()
    {
        var (_, ana) = await SignedInAsync();
        using (ana)
        {
            var first = await UploadPhotoAsync(ana, Png(SKColors.Teal));
            var firstPath = (await AuthFlow.JsonAsync(first)).GetProperty("photo").GetString()!;
            var second = await UploadPhotoAsync(ana, Png(SKColors.Orange));
            var secondPath = (await AuthFlow.JsonAsync(second)).GetProperty("photo").GetString()!;

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Matches("^/media/avatars/[0-9a-f]{24}/[0-9a-f]{32}\\.webp$", secondPath);
            Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync(new Uri(firstPath, UriKind.Relative), Ct)).StatusCode);
            var photo = await ana.GetAsync(new Uri(secondPath, UriKind.Relative), Ct);
            Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
            using (var decoded = SKBitmap.Decode(await photo.Content.ReadAsByteArrayAsync(Ct)))
            {
                Assert.Equal((512, 512), (decoded.Width, decoded.Height));
            }

            var removed = await ana.DeleteAsync(new Uri("/api/v1/profiles/me/photo", UriKind.Relative), Ct);
            var again = await ana.DeleteAsync(new Uri("/api/v1/profiles/me/photo", UriKind.Relative), Ct);

            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
            Assert.Equal(JsonValueKind.Null, (await GetAsync(ana, "/api/v1/profiles/me")).GetProperty("photo").ValueKind);
            Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync(new Uri(secondPath, UriKind.Relative), Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task DeletingTheAccount_AlsoDeletesTheProfilePhoto()
    {
        var (_, ana) = await SignedInAsync();
        using (ana)
        {
            var path = (await AuthFlow.JsonAsync(await UploadPhotoAsync(ana, Png(SKColors.Teal)))).GetProperty("photo").GetString()!;

            var deleted = await ana.PostAsJsonAsync(new Uri("/api/v1/account/delete", UriKind.Relative), new { password = AuthFlow.Password }, Ct);

            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync(new Uri(path, UriKind.Relative), Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task Validations_AnswerWithTheirCodes()
    {
        var (_, ana) = await SignedInAsync();
        using (ana)
        {
            await AuthFlow.AssertCodeAsync(await UpdateAsync(ana, new { career = "ing-sistemas", term = 6 }), HttpStatusCode.BadRequest, "profiles.invalid_career");
            await AuthFlow.AssertCodeAsync(await UpdateAsync(ana, new { career = "tsu-ti-software", term = 9 }), HttpStatusCode.BadRequest, "profiles.invalid_term");
            await AuthFlow.AssertCodeAsync(await UpdateAsync(ana, new { term = 25 }), HttpStatusCode.BadRequest, "validation.failed");
            await AuthFlow.AssertCodeAsync(
                await ana.PutAsJsonAsync(new Uri("/api/v1/profiles/me/visibility", UriKind.Relative), new { career = false }, Ct),
                HttpStatusCode.BadRequest,
                "validation.failed");
            await AuthFlow.AssertCodeAsync(await UploadPhotoAsync(ana, "no soy una foto"u8.ToArray()), HttpStatusCode.UnsupportedMediaType, "listings.photo_unsupported");
            await AuthFlow.AssertCodeAsync(
                await ana.GetAsync(new Uri("/api/v1/profiles/6716a0000000000000000abc", UriKind.Relative), Ct),
                HttpStatusCode.NotFound,
                "profiles.not_found");
        }
    }

    [Fact]
    public async Task Careers_AreTheThirtyEightOfTheUttt()
    {
        var (_, ana) = await SignedInAsync();
        using (ana)
        {
            var careers = await GetAsync(ana, "/api/v1/careers");

            Assert.Equal(11, careers.GetProperty("academicTerm").GetProperty("count").GetInt32());
            Assert.Equal(38, careers.GetProperty("careers").GetArrayLength());
        }
    }
}
