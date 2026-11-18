using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Musubi.Api.Platform.Email;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// Production detras de CloudFront (cada prueba usa su propia IP de cliente) y sin SES: los codigos se descartan,
/// las pruebas nunca los necesitan.
/// </summary>
public sealed class AuthRoutesApiFactory : SecurityApiFactory
{
    public AuthRoutesApiFactory()
        : base("Production")
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [CloudFrontForwardedHeaders.TrustKey] = "true",
        }));
        builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(new DiscardingEmailSender()));
    }

    private sealed class DiscardingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

/// <summary>
/// SCRUM-120 / OWASP API2 y API4: las 9 rutas anonimas de <c>/auth</c> (HU-01, HU-02, HU-03, HU-56), probadas en su
/// ruta real. Son publicas a proposito, asi que lo que se cuida es: limite de intentos por cliente, que no
/// revelen si una cuenta existe, que no acepten campos de mas (asignacion masiva) y que no filtren detalle.
/// </summary>
public sealed class AuthRoutesSecurityTests(AuthRoutesApiFactory factory) : IClassFixture<AuthRoutesApiFactory>
{
    private const string Password = "musubi2026";
    private static readonly string[] AdminRole = ["admin"];
    private static int nextViewer;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"seg.{Guid.NewGuid():N}@uttt.edu.mx";

    /// <summary>Una IP de cliente nueva por llamada: cada prueba tiene su propio limite de intentos.</summary>
    private static string NewViewer() => $"198.18.{Interlocked.Increment(ref nextViewer) / 250}.{(nextViewer % 250) + 1}:44321";

    private async Task<HttpResponseMessage> PostAsync(string path, object? body, string? viewer = null, string? bearer = null)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(body ?? new { }),
        };
        request.Headers.Add(CloudFrontForwardedHeaders.ViewerAddressHeader, viewer ?? NewViewer());
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return await client.SendAsync(request, Ct).ConfigureAwait(false);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false)).RootElement.Clone();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response).ConfigureAwait(false)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private async Task<string> RegisterPendingAsync()
    {
        var email = NewEmail();
        var response = await PostAsync("/api/v1/auth/register", new { email, fullName = "Prueba Seguridad", password = Password, acceptTerms = true, acceptPrivacy = true }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return email;
    }

    public static TheoryData<string> AnonymousRoutes =>
    [
        "/api/v1/auth/campus",
        "/api/v1/auth/register",
        "/api/v1/auth/verification/resend",
        "/api/v1/auth/verification/confirm",
        "/api/v1/auth/login",
        "/api/v1/auth/refresh",
        "/api/v1/auth/logout",
        "/api/v1/auth/password/forgot",
        "/api/v1/auth/password/reset",
    ];

    [Theory]
    [MemberData(nameof(AnonymousRoutes))]
    public async Task EachAnonymousRoute_IsPublic_ButLimitedPerClient(string path)
    {
        var viewer = NewViewer();
        for (var i = 0; i < SecurityApiFactory.AuthPermitLimit; i++)
        {
            using var allowed = await PostAsync(path, new { }, viewer);
            // Publica (nunca 401) y sin errores internos: un cuerpo vacio es un 400 de validacion.
            Assert.NotEqual(HttpStatusCode.Unauthorized, allowed.StatusCode);
            Assert.True((int)allowed.StatusCode < 500, $"{path} respondio {allowed.StatusCode}");
        }

        using var rejected = await PostAsync(path, new { }, viewer);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        Assert.Equal("rate_limit.exceeded", await CodeAsync(rejected));
    }

    [Fact]
    public async Task Campus_NonInstitutionalEmail_DoesNotListTheAcceptedDomains()
    {
        var response = await PostAsync("/api/v1/auth/campus", new { email = "alguien@gmail.com" });
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("auth.email_not_institutional", (await JsonAsync(response)).GetProperty("code").GetString());
        Assert.DoesNotContain("musubiapp.com.mx", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Register_ExtraFields_CannotMakeAnAdminOrSkipVerification()
    {
        var email = NewEmail();

        var response = await PostAsync("/api/v1/auth/register", new
        {
            email,
            fullName = "Quiero Ser Admin",
            password = Password,
            acceptTerms = true,
            acceptPrivacy = true,
            roles = AdminRole,
            status = "active",
            emailVerified = true,
        });
        var login = await PostAsync("/api/v1/auth/login", new { email, password = Password });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False((await JsonAsync(response)).TryGetProperty("roles", out _));
        // Sigue pendiente de verificar el correo: los campos de mas se ignoraron.
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("auth.email_not_verified", await CodeAsync(login));
    }

    [Fact]
    public async Task Resend_KnownOrUnknownEmail_AnswersTheSameWay()
    {
        var unknown = await PostAsync("/api/v1/auth/verification/resend", new { email = NewEmail() });
        var malformed = await PostAsync("/api/v1/auth/verification/resend", new { email = "no-es-correo" });

        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, malformed.StatusCode);
        Assert.Equal(
            (await JsonAsync(unknown)).EnumerateObject().Select(p => p.Name),
            (await JsonAsync(malformed)).EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Confirm_WrongCode_LooksTheSameForAPendingAccountAndAnUnknownEmail()
    {
        var pending = await RegisterPendingAsync();

        var known = await PostAsync("/api/v1/auth/verification/confirm", new { email = pending, code = "000000" });
        var unknown = await PostAsync("/api/v1/auth/verification/confirm", new { email = NewEmail(), code = "000000" });

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal("auth.invalid_code", await CodeAsync(known));
        Assert.Equal("auth.invalid_code", await CodeAsync(unknown));
    }

    [Fact]
    public async Task Login_OversizedOrMissingPassword_IsAValidationError_NeverA500()
    {
        var oversized = await PostAsync("/api/v1/auth/login", new { email = NewEmail(), password = new string('x', 129) });
        var missing = await PostAsync("/api/v1/auth/login", new { email = NewEmail() });

        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task Refresh_ForgedOrAccessToken_IsTheSameSessionExpired()
    {
        var accessToken = factory.Services.GetRequiredService<AccessTokenIssuer>().Issue("6716a0000000000000000001", "000000000000000000000001", ["student"]).Token;

        var forged = await PostAsync("/api/v1/auth/refresh", new { refreshToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) });
        var wrongKind = await PostAsync("/api/v1/auth/refresh", new { refreshToken = accessToken[..Math.Min(200, accessToken.Length)] });

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal("auth.session_expired", await CodeAsync(forged));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongKind.StatusCode);
        Assert.Equal("auth.session_expired", await CodeAsync(wrongKind));
    }

    [Fact]
    public async Task Logout_UnknownToken_Is204_SoItCannotBeUsedAsAnOracle()
    {
        var response = await PostAsync("/api/v1/auth/logout", new { refreshToken = "no-existe" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithTheAccessToken_RevokesItRightAway()
    {
        var token = factory.Services.GetRequiredService<AccessTokenIssuer>().Issue(Guid.NewGuid().ToString("N")[..24], "000000000000000000000001", ["student"]).Token;
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var protectedUri = new Uri("/api/v1/test-auth/protected", UriKind.Relative);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(protectedUri, Ct)).StatusCode);

        var logout = await PostAsync("/api/v1/auth/logout", new { refreshToken = "no-existe" }, bearer: token);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(protectedUri, Ct)).StatusCode);
    }

    [Fact]
    public async Task Forgot_KnownOrUnknownEmail_AnswersTheSameWay()
    {
        var pending = await RegisterPendingAsync();

        var known = await PostAsync("/api/v1/auth/password/forgot", new { email = pending });
        var unknown = await PostAsync("/api/v1/auth/password/forgot", new { email = NewEmail() });

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(
            (await JsonAsync(known)).EnumerateObject().Select(p => p.Name),
            (await JsonAsync(unknown)).EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task Reset_WrongCode_LooksTheSameForAnAccountAndAnUnknownEmail()
    {
        var pending = await RegisterPendingAsync();

        var known = await PostAsync("/api/v1/auth/password/reset", new { email = pending, code = "000000", newPassword = "nueva2026clave" });
        var unknown = await PostAsync("/api/v1/auth/password/reset", new { email = NewEmail(), code = "000000", newPassword = "nueva2026clave" });

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal("auth.invalid_code", await CodeAsync(known));
        Assert.Equal("auth.invalid_code", await CodeAsync(unknown));
    }
}
