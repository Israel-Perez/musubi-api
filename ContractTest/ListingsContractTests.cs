using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;
using SkiaSharp;

namespace ContractTest;

/// <summary>
/// HU-06 / HU-48 fase 2: formas JSON de /api/v1/products y /api/v1/categories que consume musubi-app.
/// Cambiar un fixture = cambio de contrato: actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class ListingsContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> FixtureAsync(string name)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    /// <summary>Nombres y tipos, recursivo en los objetos anidados.</summary>
    private static List<string> Shape(JsonElement element, string prefix = "") =>
        [.. element.EnumerateObject()
            .SelectMany(p => p.Value.ValueKind == JsonValueKind.Object
                ? Shape(p.Value, $"{prefix}{p.Name}.")
                : [$"{prefix}{p.Name}:{p.Value.ValueKind}"])
            .Order(StringComparer.Ordinal)];

    private static ByteArrayContent Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Orange);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var content = new ByteArrayContent(data.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return content;
    }

    [Fact]
    public async Task PhotoProductPageAndCategories_MatchTheirFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        using var client = app.CreateClient();
        var email = $"ana.{Guid.NewGuid():N}@uttt.edu.mx";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Ana Sofía Ramírez", password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct);
        var session = await BodyAsync(await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());

        using var form = new MultipartFormDataContent();
        using var file = Png();
        form.Add(file, "photo", "foto.png");
        var photo = await BodyAsync(await client.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct));
        var product = await BodyAsync(await client.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new
            {
                title = "Calculadora científica Casio fx-991ES Plus",
                description = "La usé un semestre para Cálculo y Física.",
                category = "calculadoras",
                priceCents = 45_000,
                condition = "used",
                conditionDetail = "likeNew",
                stock = 1,
                photos = new[] { photo.GetProperty("key").GetString() },
            },
            Ct));
        var page = await BodyAsync(await client.GetAsync(new Uri("/api/v1/products?category=calculadoras", UriKind.Relative), Ct));
        var categories = await BodyAsync(await client.GetAsync(new Uri("/api/v1/categories", UriKind.Relative), Ct));

        Assert.Equal(Shape(await FixtureAsync("photo.json")), Shape(photo));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(product));
        Assert.Equal(Shape(await FixtureAsync("product-page.json")), Shape(page));
        Assert.Equal(Shape(await FixtureAsync("product.json")), Shape(page.GetProperty("items")[0]));
        Assert.Equal(Shape(await FixtureAsync("category.json")), Shape(categories[0]));
    }
}
