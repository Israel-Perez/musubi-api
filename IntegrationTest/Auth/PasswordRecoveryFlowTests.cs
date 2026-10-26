using System.Net;

namespace IntegrationTest.Auth;

/// <summary>HU-56: recuperar la contrasena con un codigo al correo institucional, sin revelar cuentas.</summary>
public sealed class PasswordRecoveryFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private const string NewPassword = "nueva2026clave";

    private readonly AuthFlow flow = new(factory);

    private Task<HttpResponseMessage> ForgotAsync(string email) =>
        flow.PostAsync("/api/v1/auth/password/forgot", new { email });

    private Task<HttpResponseMessage> ResetAsync(string email, string code, string newPassword = NewPassword) =>
        flow.PostAsync("/api/v1/auth/password/reset", new { email, code, newPassword });

    private int EmailsTo(string email) => factory.Emails.Sent.Count(m => m.To == email);

    [Fact]
    public async Task ForgotThenReset_ChangesThePasswordAndClosesEverySession()
    {
        var email = await flow.RegisterActiveAsync();
        var openSession = await flow.SignInAsync(email);

        var forgot = await ForgotAsync(email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        var message = factory.Emails.Sent.Last(m => m.To == email);
        Assert.StartsWith("Recupera tu cuenta de Musubi", message.Subject, StringComparison.Ordinal);

        var reset = await ResetAsync(email, factory.Emails.LastCodeFor(email));

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        await AuthFlow.AssertCodeAsync(await flow.LoginAsync(email), HttpStatusCode.Unauthorized, "auth.invalid_credentials");
        Assert.Equal(HttpStatusCode.OK, (await flow.LoginAsync(email, NewPassword)).StatusCode);
        await AuthFlow.AssertCodeAsync(await flow.RefreshAsync(openSession), HttpStatusCode.Unauthorized, "auth.session_expired");
    }

    [Fact]
    public async Task Forgot_UnknownOrPendingAccount_AnswersTheSameWithoutSending()
    {
        var active = await flow.RegisterActiveAsync();
        var pending = await flow.RegisterPendingAsync();
        var unknown = AuthFlow.NewEmail();
        var pendingBefore = EmailsTo(pending);

        var responses = new[] { await ForgotAsync(active), await ForgotAsync(pending), await ForgotAsync(unknown), await ForgotAsync("no es un correo") };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(AuthFlow.JsonAsync));
        Assert.All(bodies, b => Assert.Equal(bodies[0].GetProperty("resendAvailableAt").GetDateTime(), b.GetProperty("resendAvailableAt").GetDateTime()));
        Assert.Equal(pendingBefore, EmailsTo(pending));
        Assert.Equal(0, EmailsTo(unknown));
    }

    [Fact]
    public async Task Forgot_TwiceWithinTheCooldown_SendsOneEmail()
    {
        var email = await flow.RegisterActiveAsync();
        var before = EmailsTo(email);

        await ForgotAsync(email);
        await ForgotAsync(email);

        Assert.Equal(before + 1, EmailsTo(email));
    }

    [Fact]
    public async Task Reset_WrongCode_FailsAndLocksAfterFiveAttempts()
    {
        var email = await flow.RegisterActiveAsync();
        await ForgotAsync(email);
        var code = factory.Emails.LastCodeFor(email);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 1; i < 5; i++)
        {
            await AuthFlow.AssertCodeAsync(await ResetAsync(email, wrong), HttpStatusCode.BadRequest, "auth.invalid_code");
        }

        await AuthFlow.AssertCodeAsync(await ResetAsync(email, wrong), HttpStatusCode.TooManyRequests, "auth.code_attempts_exceeded");
        await AuthFlow.AssertCodeAsync(await ResetAsync(email, code), HttpStatusCode.TooManyRequests, "auth.code_attempts_exceeded");
        Assert.Equal(HttpStatusCode.OK, (await flow.LoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Reset_CodeIsSingleUse()
    {
        var email = await flow.RegisterActiveAsync();
        await ForgotAsync(email);
        var code = factory.Emails.LastCodeFor(email);

        Assert.Equal(HttpStatusCode.NoContent, (await ResetAsync(email, code)).StatusCode);
        await AuthFlow.AssertCodeAsync(await ResetAsync(email, code, "tercera2026clave"), HttpStatusCode.BadRequest, "auth.invalid_code");
    }

    [Fact]
    public async Task Reset_ExpiredCode_IsInvalid()
    {
        var email = await flow.RegisterActiveAsync();
        await ForgotAsync(email);
        var code = factory.Emails.LastCodeFor(email);
        factory.Clock.Advance(TimeSpan.FromMinutes(11));
        try
        {
            await AuthFlow.AssertCodeAsync(await ResetAsync(email, code), HttpStatusCode.BadRequest, "auth.invalid_code");
        }
        finally
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(-11));
        }
    }

    [Fact]
    public async Task Reset_WeakPassword_KeepsTheOldOne()
    {
        var email = await flow.RegisterActiveAsync();
        await ForgotAsync(email);

        await AuthFlow.AssertCodeAsync(await ResetAsync(email, factory.Emails.LastCodeFor(email), "corta"), HttpStatusCode.BadRequest, "auth.weak_password");
        Assert.Equal(HttpStatusCode.OK, (await flow.LoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Reset_VerificationCodeOfAPendingAccount_DoesNotWork()
    {
        var email = await flow.RegisterPendingAsync();

        // El codigo de verificar el correo (HU-03) no sirve para cambiar la contrasena (otro proposito).
        await AuthFlow.AssertCodeAsync(await ResetAsync(email, factory.Emails.LastCodeFor(email)), HttpStatusCode.BadRequest, "auth.invalid_code");
    }

    [Fact]
    public async Task Reset_MalformedCode_IsAValidationError()
    {
        await AuthFlow.AssertCodeAsync(await ResetAsync(AuthFlow.NewEmail(), "12ab56"), HttpStatusCode.BadRequest, "validation.failed");
    }
}
