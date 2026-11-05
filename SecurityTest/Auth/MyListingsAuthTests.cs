using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Modules.Auth.Data;
using Musubi.Api.Modules.Listings.Data;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-07 / RNF-03 / OWASP API1 y API2: editar, pausar, reactivar y eliminar exige sesion, y solo el dueno
/// actua. A otra persona del mismo campus se le responde 404 (igual que si no existiera) y nada cambia.
/// </summary>
public sealed class MyListingsAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private const string AnyId = "6716a0000000000000000001";

    private static readonly object EditBody = new
    {
        title = "Me la quedo yo",
        description = "",
        category = "calculadoras",
        priceCents = 100,
        condition = "new",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> OwnerOnly() => new()
    {
        { "PUT", "/api/v1/products/{0}" },
        { "POST", "/api/v1/products/{0}/pause" },
        { "POST", "/api/v1/products/{0}/resume" },
        { "DELETE", "/api/v1/products/{0}" },
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

    private async Task<ProductDocument> ProductOfAsync(string sellerId)
    {
        var product = new ProductDocument
        {
            Id = AuthIds.New(),
            CommunityId = InMemoryCommunityRepository.UtttId,
            SellerId = sellerId,
            Seller = new ProductSeller { Name = "Ana R." },
            Title = "Calculadora Casio fx-991ES Plus",
            Description = "La usé un semestre.",
            CategorySlug = "calculadoras",
            PriceCents = 45_000,
            Condition = ProductConditions.Used,
            ConditionDetail = "good",
            Stock = 1,
            Status = ProductStatuses.Active,
            Photos = [$"media/products/{sellerId}/{Guid.NewGuid():N}.webp"],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await production.Services.GetRequiredService<IProductRepository>().InsertAsync(product, Ct).ConfigureAwait(false);
        return product;
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
        var product = await ProductOfAsync(await MemberAsync());
        using var outsider = ClientFor(await MemberAsync());
        using var request = Request(method, path, product.Id);

        var response = await outsider.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("listings.product_not_found", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        var stored = await production.Services.GetRequiredService<IProductRepository>().FindByIdAsync(product.Id, Ct);
        Assert.NotNull(stored);
        Assert.Equal(product.Title, stored.Title);
        Assert.Equal(ProductStatuses.Active, stored.Status);
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
    public async Task ASuspendedAccount_IsForbidden_EvenOnItsOwnProduct(string method, string path)
    {
        var seller = await MemberAsync(UserStatuses.Suspended);
        var product = await ProductOfAsync(seller);
        using var client = ClientFor(seller);
        using var request = Request(method, path, product.Id);

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotNull(await production.Services.GetRequiredService<IProductRepository>().FindByIdAsync(product.Id, Ct));
    }
}
