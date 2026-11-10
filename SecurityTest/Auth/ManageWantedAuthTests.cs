using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.PurchaseRequests.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-14 / RNF-03 / OWASP API1 y API2: editar, cerrar y cancelar una solicitud exige sesion, y solo quien la publico
/// actua. A otra persona del mismo campus se le responde 404 (igual que si no existiera) y nada cambia.
/// </summary>
public sealed class ManageWantedAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string AnyId = "6716a0000000000000000001";

    private static readonly object EditBody = new
    {
        title = "Me la quedo yo",
        category = "otro",
        budgetCents = 100,
        quantity = 1,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> OwnerOnly() => new()
    {
        { "PUT", "/api/v1/wanted/{0}" },
        { "POST", "/api/v1/wanted/{0}/close" },
        { "POST", "/api/v1/wanted/{0}/cancel" },
    };

    private static HttpRequestMessage Request(string method, string path, string id) =>
        new(new HttpMethod(method), new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, path, id), UriKind.Relative))
        {
            Content = method == "PUT" ? JsonContent.Create(EditBody) : null,
        };

    private async Task<string> MemberAsync(string status = UserStatuses.Active)
    {
        var id = AuthIds.New();
        Assert.True(await production.Services.GetRequiredService<IUserRepository>().TryInsertAsync(
            new UserDocument
            {
                Id = id,
                CommunityId = InMemoryCommunityRepository.UtttId,
                Email = $"ana.{id}@uttt.edu.mx",
                EmailNormalized = $"ana.{id}@uttt.edu.mx",
                PasswordHash = "hash-que-no-se-usa-en-estas-pruebas",
                FullName = "Ana Sofía Ramírez",
                Status = status,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            },
            Ct).ConfigureAwait(false));
        return id;
    }

    private async Task<WantedRequestDocument> RequestOfAsync(string requesterId)
    {
        var request = new WantedRequestDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            RequesterId = requesterId,
            RequesterName = "Ana R.",
            Title = "Calculadora Casio fx-991ES Plus",
            CategorySlug = "calculadoras",
            CategoryName = "Calculadoras",
            BudgetCents = 60_000,
            ExpiresAt = DateTime.UtcNow.AddDays(10),
            CreatedAt = DateTime.UtcNow,
        };
        await production.Services.GetRequiredService<IWantedRepository>().InsertAsync(request, Ct).ConfigureAwait(false);
        return request;
    }

    private HttpClient ClientFor(string userId)
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>().Issue(userId, InMemoryCommunityRepository.UtttId, ["student"]);
        var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    [Theory]
    [MemberData(nameof(OwnerOnly))]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = Request(method, path, AnyId);

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(OwnerOnly))]
    public async Task SomeoneElseOfTheSameCampus_Gets404_AndNothingChanges(string method, string path)
    {
        var wanted = await RequestOfAsync(await MemberAsync());
        using var outsider = ClientFor(await MemberAsync());
        using var request = Request(method, path, wanted.Id);

        var response = await outsider.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("wanted.not_found", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var stored = await production.Services.GetRequiredService<IWantedRepository>().FindByIdAsync(wanted.Id, Ct);
        Assert.NotNull(stored);
        Assert.Equal((wanted.Title, WantedStatuses.Active, 60_000L), (stored.Title, stored.Status, stored.BudgetCents));
    }

    [Theory]
    [MemberData(nameof(OwnerOnly))]
    public async Task AnIdThatIsNotAnObjectId_Is404(string method, string path)
    {
        using var member = ClientFor(await MemberAsync());
        using var request = Request(method, path, "no-es-un-id");

        var response = await member.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(OwnerOnly))]
    public async Task ASuspendedAccount_IsForbidden_EvenOnItsOwnRequest(string method, string path)
    {
        var requester = await MemberAsync(UserStatuses.Suspended);
        var wanted = await RequestOfAsync(requester);
        using var client = ClientFor(requester);
        using var request = Request(method, path, wanted.Id);

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(WantedStatuses.Active, (await production.Services.GetRequiredService<IWantedRepository>().FindByIdAsync(wanted.Id, Ct))!.Status);
    }

    [Fact]
    public void TheEditBody_HasItsOwnSizeLimit()
    {
        // Kestrel aplica el tope en la nube; TestServer no lo aplica (lee el cuerpo completo), asi que se
        // verifica el atributo: sin el, el PUT aceptaria hasta 10 MB (el tope global, OWASP API4).
        var update = typeof(Musubi.Api.Modules.PurchaseRequests.WantedController).GetMethod("Update")!;
        var limit = update.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute), inherit: false);

        Assert.Single(limit);
        Assert.Equal(16 * 1024, Musubi.Api.Modules.PurchaseRequests.WantedController.MaxEditBytes);
    }
}
