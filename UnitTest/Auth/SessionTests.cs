using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Musubi.Api.Modules.Auth;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;
using Musubi.TestShared;

namespace UnitTest.Auth;

/// <summary>02-03 / L-10, L-16g: access token corto y refresh token rotativo con deteccion de reutilizacion.</summary>
public sealed class SessionTests
{
    private const string UserId = "6716a0000000000000000001";
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider time = new(Start);
    private readonly InMemoryRefreshTokenRepository repository = new();
    private readonly IOptions<JwtOptions> options = Options.Create(new JwtOptions
    {
        SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
    });

    private RefreshTokenService Service() => new(repository, options, time);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AccessToken_LastsFifteenMinutesAndCarriesMinimalClaims()
    {
        var token = new AccessTokenIssuer(options, time).Issue(UserId, "000000000000000000000001", ["student"]);

        var jwt = new JsonWebToken(token.Token);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal(UserId, jwt.Subject);
        Assert.Equal(Start.UtcDateTime.AddMinutes(15), token.ExpiresAt);
        Assert.Equal(token.ExpiresAt, jwt.ValidTo);
        Assert.Equal("musubi-api", jwt.Issuer);
        Assert.Contains("musubi-app", jwt.Audiences);
    }

    [Fact]
    public async Task Refresh_StoresOnlyTheHash()
    {
        var issued = await Service().IssueAsync(UserId, familyId: null, userAgent: "pruebas", Ct);

        Assert.Null(await repository.FindByHashAsync(issued.Token, Ct));
        Assert.NotNull(await repository.FindByHashAsync(RefreshTokenService.Hash(issued.Token), Ct));
        Assert.Equal(Start.UtcDateTime.AddDays(60), issued.ExpiresAt);
    }

    [Fact]
    public async Task Rotate_GivesANewTokenInTheSameFamilyAndSlidesExpiration()
    {
        var service = Service();
        var first = await service.IssueAsync(UserId, null, null, Ct);
        time.Advance(TimeSpan.FromDays(30));

        var rotation = await service.RotateAsync(first.Token, null, Ct);

        Assert.Equal(RotationStatus.Rotated, rotation.Status);
        Assert.Equal(UserId, rotation.UserId);
        Assert.Equal(first.FamilyId, rotation.Next!.FamilyId);
        Assert.NotEqual(first.Token, rotation.Next.Token);
        Assert.Equal(time.GetUtcNow().UtcDateTime.AddDays(60), rotation.Next.ExpiresAt);
    }

    [Fact]
    public async Task ReusingAnOldToken_RevokesTheWholeFamily()
    {
        var service = Service();
        var first = await service.IssueAsync(UserId, null, null, Ct);
        var second = (await service.RotateAsync(first.Token, null, Ct)).Next!;

        var reuse = await service.RotateAsync(first.Token, null, Ct);

        Assert.Equal(RotationStatus.ReuseDetected, reuse.Status);
        // El token legitimo mas reciente tambien queda inutil: la sesion robada y la real se cierran.
        Assert.Equal(RotationStatus.Invalid, (await service.RotateAsync(second.Token, null, Ct)).Status);
    }

    [Fact]
    public async Task ExpiredOrUnknownToken_IsInvalid()
    {
        var service = Service();
        var issued = await service.IssueAsync(UserId, null, null, Ct);
        time.Advance(TimeSpan.FromDays(61));

        Assert.Equal(RotationStatus.Invalid, (await service.RotateAsync(issued.Token, null, Ct)).Status);
        Assert.Equal(RotationStatus.Invalid, (await service.RotateAsync("no-existe", null, Ct)).Status);
        Assert.Equal(RotationStatus.Invalid, (await service.RotateAsync(string.Empty, null, Ct)).Status);
    }

    [Fact]
    public async Task Logout_RevokesTheSessionOfThatDeviceOnly()
    {
        var service = Service();
        var phone = await service.IssueAsync(UserId, null, "telefono", Ct);
        var tablet = await service.IssueAsync(UserId, null, "tableta", Ct);

        Assert.True(await service.RevokeAsync(phone.Token, Ct));

        Assert.Equal(RotationStatus.Invalid, (await service.RotateAsync(phone.Token, null, Ct)).Status);
        Assert.Equal(RotationStatus.Rotated, (await service.RotateAsync(tablet.Token, null, Ct)).Status);
        Assert.False(await service.RevokeAsync("no-existe", Ct));
    }

    [Fact]
    public async Task LongUserAgent_IsTrimmedToTheSchemaLimit()
    {
        var issued = await Service().IssueAsync(UserId, null, new string('u', 500), Ct);

        var stored = await repository.FindByHashAsync(RefreshTokenService.Hash(issued.Token), Ct);
        Assert.Equal(RefreshTokenService.MaxUserAgentLength, stored!.Device!.UserAgent!.Length);
    }

    [Theory]
    [InlineData("Development", "", true)]
    [InlineData("Production", "", false)]
    [InlineData("Production", "c2hvcnQ=", false)]
    public void JwtOptions_RequireAStrongKeyOutsideDevelopment(string environment, string key, bool valid)
    {
        var setup = new JwtOptionsSetup(new FakeEnvironment(environment));
        var jwt = new JwtOptions { SigningKey = key };

        setup.PostConfigure(null, jwt);

        Assert.Equal(valid, setup.Validate(null, jwt).Succeeded);
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Musubi.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
