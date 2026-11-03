using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace IntegrationTest.Auth;

/// <summary>
/// HU-04: cambiar la contrasena desde <c>Ajustes</c>. Pide la actual, cierra todas las sesiones y devuelve
/// una nueva para el dispositivo que la cambio (OWASP ASVS V3.3).
/// </summary>
public sealed class PasswordChangeTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string NewPassword = "musubi2027";

    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Cuenta activa con dos sesiones: correo, refresh token de la otra sesion y cliente de esta.</summary>
    private async Task<(string Email, string OtherRefreshToken, HttpClient Client)> TwoSessionsAsync()
    {
        // El JWT se emite con el reloj de la fabrica y JwtBearer lo valida con la hora real.
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var email = await flow.RegisterActiveAsync().ConfigureAwait(false);
        var other = await flow.SignInAsync(email).ConfigureAwait(false);
        var login = await AuthFlow.JsonAsync(await flow.LoginAsync(email).ConfigureAwait(false)).ConfigureAwait(false);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return (email, other, client);
    }

    private static Task<HttpResponseMessage> ChangeAsync(HttpClient client, string currentPassword, string newPassword) =>
        client.PostAsJsonAsync(new Uri("/api/v1/auth/password/change", UriKind.Relative), new { currentPassword, newPassword }, Ct);

    [Fact]
    public async Task Change_ClosesEveryOtherSession_AndReturnsANewOne()
    {
        var (email, otherRefresh, client) = await TwoSessionsAsync();
        using (client)
        {
            var changed = await ChangeAsync(client, AuthFlow.Password, NewPassword);

            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            var session = await AuthFlow.JsonAsync(changed);
            Assert.Equal(HttpStatusCode.OK, (await flow.RefreshAsync(session.GetProperty("refreshToken").GetString()!)).StatusCode);
        }

        await AuthFlow.AssertCodeAsync(await flow.RefreshAsync(otherRefresh), HttpStatusCode.Unauthorized, "auth.session_expired");
        await AuthFlow.AssertCodeAsync(await flow.LoginAsync(email), HttpStatusCode.Unauthorized, "auth.invalid_credentials");
        Assert.Equal(HttpStatusCode.OK, (await flow.LoginAsync(email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task WrongCurrentPassword_Is400_AndChangesNothing()
    {
        var (email, otherRefresh, client) = await TwoSessionsAsync();
        using (client)
        {
            await AuthFlow.AssertCodeAsync(await ChangeAsync(client, "otra-clave-1", NewPassword), HttpStatusCode.BadRequest, "auth.wrong_password");
        }

        Assert.Equal(HttpStatusCode.OK, (await flow.RefreshAsync(otherRefresh)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await flow.LoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task WeakNewPassword_Is400()
    {
        var (_, _, client) = await TwoSessionsAsync();
        using (client)
        {
            await AuthFlow.AssertCodeAsync(await ChangeAsync(client, AuthFlow.Password, "corta"), HttpStatusCode.BadRequest, "auth.weak_password");
        }
    }
}
