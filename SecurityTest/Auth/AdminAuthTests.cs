using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-39/40 / RNF-03, OWASP API5 (autorizacion por funcion): moderar exige sesion y el rol <c>admin</c>; un
/// estudiante recibe 403 en cada ruta, y un token con el rol pero sin el en la base (o de una cuenta suspendida)
/// tampoco modera: la base manda.
/// </summary>
public sealed class AdminAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string AnyId = "6716a0000000000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> AdminOnly() => new()
    {
        { "GET", "/api/v1/admin/reports" },
        { "POST", $"/api/v1/admin/reports/{AnyId}/resolution" },
        { "GET", "/api/v1/admin/accounts/suspended" },
        { "GET", $"/api/v1/admin/accounts/{AnyId}" },
        { "POST", $"/api/v1/admin/accounts/{AnyId}/suspension" },
        { "DELETE", $"/api/v1/admin/accounts/{AnyId}/suspension" },
    };

    private static HttpRequestMessage Request(string method, string path, string? token)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = JsonContent.Create(new { action = "dismiss", reason = "Prueba de seguridad." }),
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return doc.RootElement.GetProperty("code").GetString();
    }

    private string Token(string userId, params string[] roles) =>
        production.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, InMemoryCommunityRepository.UtttId, roles).Token;

    private async Task<string> UserAsync(string status, params string[] roles)
    {
        var email = $"admin.{Guid.NewGuid():N}@uttt.edu.mx";
        var now = DateTime.UtcNow;
        var user = new UserDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            Email = email,
            EmailNormalized = email,
            PasswordHash = "AQAAAAIAAYagAAAAEHashDePruebaNoReal==",
            FullName = "Persona de Prueba",
            Status = status,
            EmailVerifiedAt = now,
            Roles = roles,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await production.Services.GetRequiredService<IUserRepository>().TryInsertAsync(user, Ct).ConfigureAwait(false);
        return user.Id;
    }

    [Theory]
    [MemberData(nameof(AdminOnly))]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = Request(method, path, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request, Ct)).StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnly))]
    public async Task StudentToken_Is403(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = Request(method, path, Token(AuthIds.New(), UserRoles.Student));

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("auth.forbidden", await CodeAsync(response));
    }

    [Theory]
    [MemberData(nameof(AdminOnly))]
    public async Task AdminToken_WithoutTheRoleInTheDatabase_Is403(string method, string path)
    {
        // El token aun dice admin (15 minutos), pero ya le quitaron el rol en la base.
        var userId = await UserAsync(UserStatuses.Active, UserRoles.Student);
        using var client = production.CreateClient();
        using var request = Request(method, path, Token(userId, UserRoles.Student, UserRoles.Admin));

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("auth.forbidden", await CodeAsync(response));
    }

    [Fact]
    public async Task SuspendedAdmin_CannotModerate()
    {
        var userId = await UserAsync(UserStatuses.Suspended, UserRoles.Student, UserRoles.Admin);
        using var client = production.CreateClient();
        using var request = Request("GET", "/api/v1/admin/reports", Token(userId, UserRoles.Student, UserRoles.Admin));

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("auth.forbidden", await CodeAsync(response));
    }

    [Fact]
    public async Task Admin_OfAnotherCampus_DoesNotSeeTheAccount()
    {
        var admin = await UserAsync(UserStatuses.Active, UserRoles.Student, UserRoles.Admin);
        var member = await UserAsync(UserStatuses.Active, UserRoles.Student);
        var users = production.Services.GetRequiredService<IUserRepository>();
        var stored = await users.FindByIdAsync(member, Ct);
        await users.DeleteAsync(member, Ct);
        var elsewhere = AuthIds.New();
        await users.TryInsertAsync(
            new UserDocument
            {
                Id = member,
                CommunityId = elsewhere,
                Email = stored!.Email,
                EmailNormalized = stored.EmailNormalized,
                PasswordHash = stored.PasswordHash,
                FullName = stored.FullName,
                Status = stored.Status,
                EmailVerifiedAt = stored.EmailVerifiedAt,
                Roles = stored.Roles,
                CreatedAt = stored.CreatedAt,
                UpdatedAt = stored.UpdatedAt,
            },
            Ct);
        using var client = production.CreateClient();
        using var get = Request("GET", $"/api/v1/admin/accounts/{member}", Token(admin, UserRoles.Student, UserRoles.Admin));
        using var suspend = Request("POST", $"/api/v1/admin/accounts/{member}/suspension", Token(admin, UserRoles.Student, UserRoles.Admin));

        var seen = await client.SendAsync(get, Ct);
        var suspended = await client.SendAsync(suspend, Ct);

        Assert.Equal(HttpStatusCode.NotFound, seen.StatusCode);
        Assert.Equal("admin.account_not_found", await CodeAsync(seen));
        Assert.Equal(HttpStatusCode.NotFound, suspended.StatusCode);
        Assert.Equal(UserStatuses.Active, (await users.FindByIdAsync(member, Ct))!.Status);
    }
}
