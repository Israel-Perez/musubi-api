using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Musubi.Api.Modules.Auth.Data;

namespace IntegrationTest.Auth;

/// <summary>HU-01 / HU-03 / HU-47: registro con correo institucional, consentimientos y verificacion con codigo.</summary>
public sealed class RegistrationFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string Password = "musubi2026";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"ana.{Guid.NewGuid():N}@uttt.edu.mx";

    private static object Register(string email, bool terms = true, bool privacy = true, string password = Password) => new
    {
        email,
        fullName = "Ana Sofía Prueba",
        password,
        acceptTerms = terms,
        acceptPrivacy = privacy,
    };

    private async Task<HttpResponseMessage> PostAsync(string path, object body)
    {
        using var client = factory.CreateClient();
        return await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct).ConfigureAwait(false);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response).ConfigureAwait(false)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Campus_InstitutionalEmail_DetectsUttt()
    {
        var response = await PostAsync("/api/v1/auth/campus", new { email = "Tu.Nombre@UTTT.edu.mx" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("UTTT", body.GetProperty("shortName").GetString());
        Assert.Equal("Universidad Tecnológica de Tula-Tepeji", body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Campus_TeamReviewDomain_BelongsToUttt()
    {
        // SCRUM-88: la cuenta de los revisores de Google Play (revision@musubiapp.com.mx).
        var response = await PostAsync("/api/v1/auth/campus", new { email = "revision@musubiapp.com.mx" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("UTTT", (await JsonAsync(response)).GetProperty("shortName").GetString());
    }

    [Theory]
    [InlineData("ana.ramirez@gmail.com")]
    [InlineData("ana.ramirez@correo.com")]
    [InlineData("Ana <ana@uttt.edu.mx>")]
    public async Task Campus_PersonalOrMalformedEmail_IsNotInstitutional(string email)
    {
        await AssertCodeAsync(await PostAsync("/api/v1/auth/campus", new { email }), HttpStatusCode.UnprocessableEntity, "auth.email_not_institutional");
    }

    [Fact]
    public async Task Register_ThenConfirm_OpensSessionAndActivatesTheUser()
    {
        var email = NewEmail();

        var registered = await PostAsync("/api/v1/auth/register", Register(email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var registration = await JsonAsync(registered);
        Assert.Equal(email, registration.GetProperty("email").GetString());
        Assert.Equal(AuthApiFactory.Start.UtcDateTime.AddMinutes(10), registration.GetProperty("codeExpiresAt").GetDateTime());

        var message = Assert.Single(factory.Emails.Sent, m => m.To == email);
        Assert.Contains("Ana", message.TextBody, StringComparison.Ordinal);
        var code = factory.Emails.LastCodeFor(email);

        var confirmed = await PostAsync("/api/v1/auth/verification/confirm", new { email, code });

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var session = await JsonAsync(confirmed);
        var user = session.GetProperty("user");
        Assert.Equal("Ana Sofía Prueba", user.GetProperty("fullName").GetString());
        Assert.True(user.GetProperty("emailVerified").GetBoolean());
        Assert.Equal("UTTT", user.GetProperty("campus").GetProperty("shortName").GetString());
        Assert.Equal(user.GetProperty("id").GetString(), new JsonWebToken(session.GetProperty("accessToken").GetString()).Subject);
        Assert.False(string.IsNullOrEmpty(session.GetProperty("refreshToken").GetString()));

        var stored = await factory.Services.GetRequiredService<IUserRepository>().FindByEmailAsync(email, Ct);
        Assert.Equal(UserStatuses.Active, stored!.Status);
        Assert.Equal("2026-10", stored.Consents.PrivacyVersion);
        Assert.Equal("2026-10", stored.Consents.TermsVersion);
        Assert.NotEqual(Password, stored.PasswordHash);
    }

    [Fact]
    public async Task Responses_NeverExposeHashesOrTheCode()
    {
        var email = NewEmail();
        var registered = await (await PostAsync("/api/v1/auth/register", Register(email))).Content.ReadAsStringAsync(Ct);
        var code = factory.Emails.LastCodeFor(email);
        var confirmed = await (await PostAsync("/api/v1/auth/verification/confirm", new { email, code })).Content.ReadAsStringAsync(Ct);

        foreach (var body in new[] { registered, confirmed })
        {
            Assert.DoesNotContain(code, body, StringComparison.Ordinal);
            Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
            Assert.DoesNotContain("hash", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Register_WithoutBothConsents_IsRejected(bool terms, bool privacy)
    {
        await AssertCodeAsync(
            await PostAsync("/api/v1/auth/register", Register(NewEmail(), terms, privacy)),
            HttpStatusCode.BadRequest,
            "auth.consent_required");
    }

    [Fact]
    public async Task Register_OmittingConsents_IsAValidationError()
    {
        await AssertCodeAsync(
            await PostAsync("/api/v1/auth/register", new { email = NewEmail(), fullName = "Ana", password = Password }),
            HttpStatusCode.BadRequest,
            "validation.failed");
    }

    [Theory]
    [InlineData("abcdefgh")]
    [InlineData("12345678")]
    [InlineData("abc123")]
    public async Task Register_WeakPassword_IsRejected(string password)
    {
        await AssertCodeAsync(
            await PostAsync("/api/v1/auth/register", Register(NewEmail(), password: password)),
            HttpStatusCode.BadRequest,
            "auth.weak_password");
    }

    [Fact]
    public async Task Register_PersonalEmail_IsNotInstitutional()
    {
        await AssertCodeAsync(
            await PostAsync("/api/v1/auth/register", Register($"ana.{Guid.NewGuid():N}@gmail.com")),
            HttpStatusCode.UnprocessableEntity,
            "auth.email_not_institutional");
    }

    [Fact]
    public async Task Register_SameEmailInAnyCase_IsAConflict()
    {
        var email = NewEmail();
        await PostAsync("/api/v1/auth/register", Register(email));

        await AssertCodeAsync(
            await PostAsync("/api/v1/auth/register", Register($"  {email.ToUpperInvariant()} ")),
            HttpStatusCode.Conflict,
            "auth.email_already_registered");
    }

    [Fact]
    public async Task Confirm_WrongCode_FailsAndLocksAfterFiveAttempts()
    {
        var email = NewEmail();
        await PostAsync("/api/v1/auth/register", Register(email));
        var code = factory.Emails.LastCodeFor(email);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 1; i < 5; i++)
        {
            await AssertCodeAsync(await PostAsync("/api/v1/auth/verification/confirm", new { email, code = wrong }), HttpStatusCode.BadRequest, "auth.invalid_code");
        }

        await AssertCodeAsync(await PostAsync("/api/v1/auth/verification/confirm", new { email, code = wrong }), HttpStatusCode.TooManyRequests, "auth.code_attempts_exceeded");
        // Bloqueado aunque ahora llegue el codigo correcto: hay que pedir uno nuevo.
        await AssertCodeAsync(await PostAsync("/api/v1/auth/verification/confirm", new { email, code }), HttpStatusCode.TooManyRequests, "auth.code_attempts_exceeded");
    }

    [Fact]
    public async Task Confirm_ExpiredCode_IsInvalid()
    {
        var email = NewEmail();
        await PostAsync("/api/v1/auth/register", Register(email));
        var code = factory.Emails.LastCodeFor(email);
        factory.Clock.Advance(TimeSpan.FromMinutes(11));
        try
        {
            await AssertCodeAsync(await PostAsync("/api/v1/auth/verification/confirm", new { email, code }), HttpStatusCode.BadRequest, "auth.invalid_code");
        }
        finally
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(-11));
        }
    }

    [Fact]
    public async Task Confirm_UnknownEmail_LooksLikeAnInvalidCode()
    {
        await AssertCodeAsync(
            await PostAsync("/api/v1/auth/verification/confirm", new { email = NewEmail(), code = "123456" }),
            HttpStatusCode.BadRequest,
            "auth.invalid_code");
    }

    [Fact]
    public async Task Resend_RespectsTheCooldownAndReplacesTheCode()
    {
        var email = NewEmail();
        await PostAsync("/api/v1/auth/register", Register(email));
        var first = factory.Emails.LastCodeFor(email);

        await AssertCodeAsync(await PostAsync("/api/v1/auth/verification/resend", new { email }), HttpStatusCode.TooManyRequests, "auth.resend_too_soon");

        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        try
        {
            var resent = await PostAsync("/api/v1/auth/verification/resend", new { email });
            Assert.Equal(HttpStatusCode.Accepted, resent.StatusCode);
            var second = factory.Emails.LastCodeFor(email);
            Assert.Equal(2, factory.Emails.Sent.Count(m => m.To == email));

            if (first != second)
            {
                await AssertCodeAsync(await PostAsync("/api/v1/auth/verification/confirm", new { email, code = first }), HttpStatusCode.BadRequest, "auth.invalid_code");
            }

            Assert.Equal(HttpStatusCode.OK, (await PostAsync("/api/v1/auth/verification/confirm", new { email, code = second })).StatusCode);
        }
        finally
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(-61));
        }
    }

    [Fact]
    public async Task Resend_ForUnknownEmail_AnswersTheSameWithoutSending()
    {
        var email = NewEmail();

        var response = await PostAsync("/api/v1/auth/verification/resend", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.DoesNotContain(factory.Emails.Sent, m => m.To == email);
    }
}
