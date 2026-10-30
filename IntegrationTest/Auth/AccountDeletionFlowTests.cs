using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SkiaSharp;

namespace IntegrationTest.Auth;

/// <summary>
/// HU-46: eliminar mi cuenta desde la app (maqueta <c>Privacidad</c>, "Eliminar mi cuenta"). Borrado real:
/// la cuenta, las sesiones, las publicaciones y las fotos desaparecen, y el correo se puede volver a usar.
/// </summary>
public sealed class AccountDeletionFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Teal);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Cuenta activa con sesion: correo, refresh token y cliente con el access token.</summary>
    private async Task<(string Email, string RefreshToken, HttpClient Client)> SignedInAsync()
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
        return (email, login.GetProperty("refreshToken").GetString()!, client);
    }

    private static async Task<string> PublishWithPhotoAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var upload = await client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var key = (await AuthFlow.JsonAsync(upload).ConfigureAwait(false)).GetProperty("key").GetString()!;

        var published = await client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new
            {
                title = "Calculadora científica Casio fx-991ES Plus",
                description = "La usé un semestre.",
                category = "calculadoras",
                priceCents = 45_000,
                condition = "used",
                conditionDetail = "likeNew",
                stock = 1,
                photos = new[] { key },
            },
            Ct).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        return key;
    }

    private static Task<HttpResponseMessage> DeleteAccountAsync(HttpClient client, string password) =>
        client.PostAsJsonAsync(new Uri("/api/v1/account/delete", UriKind.Relative), new { password }, Ct);

    [Fact]
    public async Task WrongPassword_Is400AndKeepsTheAccount()
    {
        var (email, _, client) = await SignedInAsync();
        using (client)
        {
            await AuthFlow.AssertCodeAsync(await DeleteAccountAsync(client, "otra-clave-1"), HttpStatusCode.BadRequest, "auth.wrong_password");
        }

        Assert.Equal(HttpStatusCode.OK, (await flow.LoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Delete_ErasesAccountSessionsListingsAndPhotos_AndTheEmailCanRegisterAgain()
    {
        var (email, refreshToken, client) = await SignedInAsync();
        string photo;
        using (client)
        {
            photo = await PublishWithPhotoAsync(client);

            var deleted = await DeleteAccountAsync(client, AuthFlow.Password);

            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        // Sin cuenta: no entra, la sesion no se renueva.
        await AuthFlow.AssertCodeAsync(await flow.LoginAsync(email), HttpStatusCode.Unauthorized, "auth.invalid_credentials");
        await AuthFlow.AssertCodeAsync(await flow.RefreshAsync(refreshToken), HttpStatusCode.Unauthorized, "auth.session_expired");

        // Sin publicaciones ni fotos.
        using var anonymous = factory.CreateClient();
        var showcase = await AuthFlow.JsonAsync(await anonymous.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct));
        Assert.DoesNotContain(showcase.GetProperty("items").EnumerateArray(), p => p.GetProperty("photos")[0].GetString() == "/" + photo);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(new Uri("/" + photo, UriKind.Relative), Ct)).StatusCode);

        // El correo queda libre (borrado real, R11).
        var again = await flow.PostAsync(
            "/api/v1/auth/register",
            new { email, fullName = "Ana Sofía Prueba", password = AuthFlow.Password, acceptTerms = true, acceptPrivacy = true });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task MissingPassword_IsValidationError()
    {
        var (_, _, client) = await SignedInAsync();
        using (client)
        {
            var response = await client.PostAsJsonAsync(new Uri("/api/v1/account/delete", UriKind.Relative), new { }, Ct);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("validation.failed", (await AuthFlow.JsonAsync(response)).GetProperty("code").GetString());
        }
    }
}
