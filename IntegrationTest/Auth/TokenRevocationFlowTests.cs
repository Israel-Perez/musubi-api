using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace IntegrationTest.Auth;

/// <summary>
/// SCRUM-120: el access token deja de servir en cuanto la sesion se cierra, sin esperar sus 15 minutos (lista de
/// revocacion en Redis; en las pruebas, la cache en memoria). La suspension esta en <c>Admin/ModerationFlowTests</c>.
/// </summary>
public sealed class TokenRevocationFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static readonly Uri Me = new("/api/v1/profiles/me", UriKind.Relative);
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Session(HttpClient Client, string AccessToken, string RefreshToken);

    private async Task<Session> SignInAsync(string email, string password = AuthFlow.Password)
    {
        // El JWT se emite con el reloj de la fabrica y JwtBearer valida su vigencia con la hora real.
        var behind = DateTimeOffset.UtcNow - factory.Clock.Now;
        if (behind > TimeSpan.Zero)
        {
            factory.Clock.Advance(behind);
        }

        var login = await AuthFlow.JsonAsync(await flow.LoginAsync(email, password).ConfigureAwait(false)).ConfigureAwait(false);
        var accessToken = login.GetProperty("accessToken").GetString()!;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        // La revocacion compara milisegundos: lo que pase despues ocurre despues de esta sesion.
        factory.Clock.Advance(TimeSpan.FromMilliseconds(5));
        return new Session(client, accessToken, login.GetProperty("refreshToken").GetString()!);
    }

    private static async Task<HttpStatusCode> MeAsync(HttpClient client) => (await client.GetAsync(Me, Ct).ConfigureAwait(false)).StatusCode;

    [Fact]
    public async Task Logout_TheAccessTokenStopsWorkingRightAway_OnlyOnThatDevice()
    {
        var email = await flow.RegisterActiveAsync();
        var phone = await SignInAsync(email);
        var laptop = await SignInAsync(email);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(phone.Client));

        var logout = await phone.Client.PostAsJsonAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), new { refreshToken = phone.RefreshToken }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(phone.Client));
        await AuthFlow.AssertCodeAsync(await flow.RefreshAsync(phone.RefreshToken), HttpStatusCode.Unauthorized, "auth.session_expired");
        // La otra sesion sigue: el logout es por dispositivo.
        Assert.Equal(HttpStatusCode.OK, await MeAsync(laptop.Client));
    }

    [Fact]
    public async Task PasswordChange_EveryOldAccessTokenStops_AndTheNewSessionWorks()
    {
        var email = await flow.RegisterActiveAsync();
        var other = await SignInAsync(email);
        var current = await SignInAsync(email);

        var changed = await current.Client.PostAsJsonAsync(new Uri("/api/v1/auth/password/change", UriKind.Relative), new { currentPassword = AuthFlow.Password, newPassword = "musubi2027" }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var renewed = factory.CreateClient();
        renewed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await AuthFlow.JsonAsync(changed)).GetProperty("accessToken").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(other.Client));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(current.Client));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(renewed));
    }

    [Fact]
    public async Task PasswordReset_EveryOldAccessTokenStops()
    {
        var email = await flow.RegisterActiveAsync();
        var session = await SignInAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, (await flow.PostAsync("/api/v1/auth/password/forgot", new { email })).StatusCode);
        var reset = await flow.PostAsync("/api/v1/auth/password/reset", new { email, code = factory.Emails.LastCodeFor(email), newPassword = "musubi2027" });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(session.Client));
    }

    [Fact]
    public async Task RefreshTokenReuse_AlsoCutsTheAccessTokensAlreadyOut()
    {
        var email = await flow.RegisterActiveAsync();
        var session = await SignInAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await flow.RefreshAsync(session.RefreshToken)).StatusCode);
        factory.Clock.Advance(TimeSpan.FromMilliseconds(5));

        // Alguien presenta otra vez el refresh token ya usado: posible robo.
        var reused = await flow.RefreshAsync(session.RefreshToken);

        await AuthFlow.AssertCodeAsync(reused, HttpStatusCode.Unauthorized, "auth.session_expired");
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(session.Client));
    }

    [Fact]
    public async Task AccountDeletion_TheAccessTokenStopsWorking()
    {
        var email = await flow.RegisterActiveAsync();
        var session = await SignInAsync(email);

        var deleted = await session.Client.PostAsJsonAsync(new Uri("/api/v1/account/delete", UriKind.Relative), new { password = AuthFlow.Password }, Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(session.Client));
    }
}
