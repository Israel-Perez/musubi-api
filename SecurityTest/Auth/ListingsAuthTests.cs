using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;
using SecurityTest.Common;

namespace SecurityTest.Auth;

/// <summary>
/// HU-06 / OWASP API1, API2 y API5: publicar, subir fotos y ver "mis productos" exige sesion; la vitrina es
/// publica; un token valido de alguien que no es miembro activo no publica; en la nube la API no sirve /media.
/// </summary>
public sealed class ListingsAuthTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static readonly string[] SomeoneElsesPhoto = ["media/products/6716a00000000000000000ff/0123456789abcdef0123456789abcdef.webp"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> MemberOnly() => new()
    {
        { "POST", "/api/v1/products" },
        { "POST", "/api/v1/products/photos" },
        { "GET", "/api/v1/products/mine" },
        { "GET", "/api/v1/products/price-range?category=libros" },
    };

    [Theory]
    [MemberData(nameof(MemberOnly))]
    public async Task WithoutSession_Is401(string method, string path)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ShowcaseAndCategories_ArePublic()
    {
        using var client = production.CreateClient();

        var showcase = await client.GetAsync(new Uri("/api/v1/products", UriKind.Relative), Ct);
        var categories = await client.GetAsync(new Uri("/api/v1/categories", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, showcase.StatusCode);
        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
    }

    [Fact]
    public async Task ValidTokenOfSomeoneWhoIsNotAMember_CannotPublish()
    {
        var token = production.Services.GetRequiredService<AccessTokenIssuer>()
            .Issue("6716a00000000000000000ff", "000000000000000000000001", ["student"]);
        using var client = production.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new
            {
                title = "Calculadora",
                category = "calculadoras",
                priceCents = 45_000,
                condition = "new",
                stock = 1,
                photos = SomeoneElsesPhoto,
            },
            Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InTheCloud_TheApiDoesNotServeMedia()
    {
        // En produccion las fotos viven en S3 y CloudFront atiende /media/*; la API nunca las expone.
        using var client = production.CreateClient();

        var response = await client.GetAsync(new Uri("/media/products/x/y.webp", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
