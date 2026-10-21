using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;

namespace IntegrationTest.Auth;

/// <summary>HU-02: iniciar sesion, renovarla con el refresh token rotativo y cerrarla (02-03, L-10, L-16g).</summary>
public sealed class SessionFlowTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly AuthFlow flow = new(factory);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_ActiveUser_OpensSession()
    {
        var email = await flow.RegisterActiveAsync();

        var response = await flow.LoginAsync(email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await AuthFlow.JsonAsync(response);
        var user = session.GetProperty("user");
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.True(user.GetProperty("emailVerified").GetBoolean());
        Assert.Equal("UTTT", user.GetProperty("campus").GetProperty("shortName").GetString());
        Assert.Equal(user.GetProperty("id").GetString(), new JsonWebToken(session.GetProperty("accessToken").GetString()).Subject);
        Assert.False(string.IsNullOrEmpty(session.GetProperty("refreshToken").GetString()));
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownEmail_LookExactlyTheSame()
    {
        var email = await flow.RegisterActiveAsync();

        var wrongPassword = await flow.LoginAsync(email, "otra2026clave");
        var unknownEmail = await flow.LoginAsync(AuthFlow.NewEmail());

        await AuthFlow.AssertCodeAsync(wrongPassword, HttpStatusCode.Unauthorized, "auth.invalid_credentials");
        await AuthFlow.AssertCodeAsync(unknownEmail, HttpStatusCode.Unauthorized, "auth.invalid_credentials");
        var first = await AuthFlow.JsonAsync(wrongPassword);
        var second = await AuthFlow.JsonAsync(unknownEmail);
        Assert.Equal(first.GetProperty("title").GetString(), second.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Login_PendingUser_IsAskedToVerifyOnlyWithTheRightPassword()
    {
        var email = await flow.RegisterPendingAsync();

        await AuthFlow.AssertCodeAsync(await flow.LoginAsync(email), HttpStatusCode.Forbidden, "auth.email_not_verified");
        // Con la contrasena equivocada no se revela que la cuenta existe y esta pendiente.
        await AuthFlow.AssertCodeAsync(await flow.LoginAsync(email, "otra2026clave"), HttpStatusCode.Unauthorized, "auth.invalid_credentials");
    }

    [Fact]
    public async Task Login_SuspendedUser_IsRejected()
    {
        var email = AuthFlow.NewEmail();
        var community = await factory.Services.GetRequiredService<ICommunityRepository>().FindByEmailDomainAsync("uttt.edu.mx", Ct);
        var now = AuthApiFactory.Start.UtcDateTime;
        await factory.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = AuthIds.New(),
                CommunityId = community!.Id,
                Email = email,
                EmailNormalized = email,
                PasswordHash = factory.Services.GetRequiredService<PasswordHashing>().Hash(AuthFlow.Password),
                FullName = "Ana Sofía Prueba",
                Status = UserStatuses.Suspended,
                EmailVerifiedAt = now,
                Roles = [UserRoles.Student],
                CreatedAt = now,
                UpdatedAt = now,
            },
            Ct);

        await AuthFlow.AssertCodeAsync(await flow.LoginAsync(email), HttpStatusCode.Forbidden, "auth.account_suspended");
    }

    [Fact]
    public async Task Login_MissingPassword_IsAValidationError()
    {
        await AuthFlow.AssertCodeAsync(
            await flow.PostAsync("/api/v1/auth/login", new { email = AuthFlow.NewEmail() }),
            HttpStatusCode.BadRequest,
            "validation.failed");
    }

    [Fact]
    public async Task Refresh_RotatesTheToken_AndReusingTheOldOneRevokesTheFamily()
    {
        var first = await flow.SignInAsync(await flow.RegisterActiveAsync());

        var rotated = await flow.RefreshAsync(first);

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var second = (await AuthFlow.JsonAsync(rotated)).GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(first, second);

        // Alguien reutiliza el token ya rotado (robo): se cierra la sesion completa.
        await AuthFlow.AssertCodeAsync(await flow.RefreshAsync(first), HttpStatusCode.Unauthorized, "auth.session_expired");
        await AuthFlow.AssertCodeAsync(await flow.RefreshAsync(second), HttpStatusCode.Unauthorized, "auth.session_expired");
    }

    [Fact]
    public async Task Refresh_UnknownToken_SessionExpired()
    {
        await AuthFlow.AssertCodeAsync(await flow.RefreshAsync("no-es-un-token"), HttpStatusCode.Unauthorized, "auth.session_expired");
    }

    [Fact]
    public async Task Refresh_AfterTheAccountIsDeleted_SessionExpired()
    {
        var email = await flow.RegisterActiveAsync();
        var refresh = await flow.SignInAsync(email);
        var users = factory.Services.GetRequiredService<IUserRepository>();
        await users.DeleteAsync((await users.FindByEmailAsync(email, Ct))!.Id, Ct);

        await AuthFlow.AssertCodeAsync(await flow.RefreshAsync(refresh), HttpStatusCode.Unauthorized, "auth.session_expired");
    }

    [Fact]
    public async Task Logout_ClosesOnlyThisDevice_AndIsIdempotent()
    {
        var email = await flow.RegisterActiveAsync();
        var phone = await flow.SignInAsync(email);
        var tablet = await flow.SignInAsync(email);

        Assert.Equal(HttpStatusCode.NoContent, (await flow.PostAsync("/api/v1/auth/logout", new { refreshToken = phone })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await flow.PostAsync("/api/v1/auth/logout", new { refreshToken = phone })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await flow.PostAsync("/api/v1/auth/logout", new { refreshToken = "desconocido" })).StatusCode);

        await AuthFlow.AssertCodeAsync(await flow.RefreshAsync(phone), HttpStatusCode.Unauthorized, "auth.session_expired");
        Assert.Equal(HttpStatusCode.OK, (await flow.RefreshAsync(tablet)).StatusCode);
    }
}
