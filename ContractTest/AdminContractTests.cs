using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;
using SkiaSharp;

namespace ContractTest;

/// <summary>
/// HU-39, HU-40 y HU-41: formas JSON de /api/v1/admin que consume musubi-app ("Administracion" en Ajustes). Cambiar un
/// fixture = cambio de contrato: actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class AdminContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
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

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(400, 300);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Teal);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<(HttpClient Client, string Id)> SignedInAsync(WebApplicationFactory<Program> app, RecordingEmailSender emails, string email, string name)
    {
        var client = app.CreateClient();
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = name, password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        var confirmed = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await BodyAsync(confirmed).ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return (client, session.GetProperty("user").GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task ReportsAndAccounts_MatchTheirFixtures()
    {
        var emails = new RecordingEmailSender();
        var adminEmail = $"equipo.{Guid.NewGuid():N}@uttt.edu.mx";
        using var app = factory.WithWebHostBuilder(b => b
            .UseSetting("Admin:Emails:0", adminEmail)
            .ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var admin = await SignedInAsync(app, emails, adminEmail, "Equipo Musubi");
        var seller = await SignedInAsync(app, emails, $"luis.{Guid.NewGuid():N}@uttt.edu.mx", "Luis Martínez");
        var buyer = await SignedInAsync(app, emails, $"ana.{Guid.NewGuid():N}@uttt.edu.mx", "Ana Sofía Ramírez");
        using var adminClient = admin.Client;
        using var sellerClient = seller.Client;
        using var buyerClient = buyer.Client;

        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var photo = await BodyAsync(await sellerClient.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct));
        var product = await BodyAsync(await sellerClient.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title = "Calculadora Casio fx-991", description = "Original.", category = "calculadoras", priceCents = 45_000, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { photo.GetProperty("key").GetString() } },
            Ct));
        var report = await BodyAsync(await buyerClient.PostAsJsonAsync(
            new Uri("/api/v1/reports", UriKind.Relative),
            new { targetType = "product", targetId = product.GetProperty("id").GetString(), reason = "not_as_described", details = "Las fotos son de otra tienda." },
            Ct));

        var resolved = await adminClient.PostAsJsonAsync(
            new Uri($"/api/v1/admin/reports/{report.GetProperty("id").GetString()}/resolution", UriKind.Relative),
            new { action = "uphold", note = "Fotos de internet." },
            Ct);
        var closed = await BodyAsync(await adminClient.GetAsync(new Uri("/api/v1/admin/reports?status=closed", UriKind.Relative), Ct));
        var suspended = await adminClient.PostAsJsonAsync(
            new Uri($"/api/v1/admin/accounts/{seller.Id}/suspension", UriKind.Relative), new { reason = "Pidió pagos fuera de Musubi." }, Ct);
        var list = await BodyAsync(await adminClient.GetAsync(new Uri("/api/v1/admin/accounts/suspended", UriKind.Relative), Ct));

        var reportFixture = await FixtureAsync("admin-report-page.json");
        var accountFixture = await FixtureAsync("admin-account.json");
        var listFixture = await FixtureAsync("admin-account-page.json");
        var account = await BodyAsync(suspended);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal(Shape(reportFixture.GetProperty("items")[0]), Shape(await BodyAsync(resolved)));
        Assert.Equal(Shape(reportFixture), Shape(closed));
        Assert.Equal(Shape(reportFixture.GetProperty("items")[0]), Shape(closed.GetProperty("items")[0]));
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);
        Assert.Equal(Shape(accountFixture), Shape(account));
        Assert.Equal(Shape(accountFixture.GetProperty("history")[0]), Shape(account.GetProperty("history")[0]));
        Assert.Equal(Shape(listFixture), Shape(list));
        Assert.Equal(Shape(listFixture.GetProperty("items")[0]), Shape(list.GetProperty("items")[0]));
    }

    [Fact]
    public async Task Categories_MatchTheirFixtures()
    {
        var emails = new RecordingEmailSender();
        var adminEmail = $"equipo.{Guid.NewGuid():N}@uttt.edu.mx";
        using var app = factory.WithWebHostBuilder(b => b
            .UseSetting("Admin:Emails:0", adminEmail)
            .ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var admin = await SignedInAsync(app, emails, adminEmail, "Equipo Musubi");
        using var adminClient = admin.Client;

        var created = await adminClient.PostAsJsonAsync(new Uri("/api/v1/admin/categories", UriKind.Relative), new { name = "Ropa y batas" }, Ct);
        var changed = await adminClient.PatchAsJsonAsync(new Uri("/api/v1/admin/categories/ropa-y-batas", UriKind.Relative), new { active = false }, Ct);
        var list = await BodyAsync(await adminClient.GetAsync(new Uri("/api/v1/admin/categories", UriKind.Relative), Ct));

        var categoryFixture = await FixtureAsync("admin-category.json");
        var pageFixture = await FixtureAsync("admin-category-page.json");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(Shape(categoryFixture), Shape(await BodyAsync(created)));
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        // Shape distingue true de false: la desactivada se compara con la del fixture que tambien lo esta.
        var inactiveFixture = pageFixture.GetProperty("items")[1];
        Assert.Equal(Shape(inactiveFixture), Shape(await BodyAsync(changed)));
        Assert.Equal(Shape(pageFixture), Shape(list));
        Assert.Equal(Shape(pageFixture.GetProperty("items")[0]), Shape(list.GetProperty("items")[0]));
        Assert.Equal(Shape(inactiveFixture), Shape(list.GetProperty("items").EnumerateArray().Last()));
    }

    [Fact]
    public async Task Indicators_MatchTheirFixture()
    {
        // HU-42: la forma de los indicadores (un area, un indicador sin desglose y uno con desglose).
        var emails = new RecordingEmailSender();
        var adminEmail = $"equipo.{Guid.NewGuid():N}@uttt.edu.mx";
        using var app = factory.WithWebHostBuilder(b => b
            .UseSetting("Admin:Emails:0", adminEmail)
            .ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var admin = await SignedInAsync(app, emails, adminEmail, "Equipo Musubi");
        using var adminClient = admin.Client;
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        var photo = await BodyAsync(await adminClient.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct));
        await adminClient.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new { title = "Calculadora Casio fx-991", category = "calculadoras", priceCents = 45_000, condition = "used", conditionDetail = "good", stock = 1, photos = new[] { photo.GetProperty("key").GetString() } },
            Ct);

        var page = await BodyAsync(await adminClient.GetAsync(new Uri("/api/v1/admin/indicators", UriKind.Relative), Ct));

        var fixture = await FixtureAsync("admin-indicators.json");
        var plain = fixture.GetProperty("areas")[0].GetProperty("items")[0];
        var withBreakdown = fixture.GetProperty("areas")[1].GetProperty("items")[0];
        Assert.Equal(Shape(fixture), Shape(page));
        Assert.Equal(Shape(fixture.GetProperty("areas")[0]), Shape(page.GetProperty("areas")[0]));
        Assert.Equal(Shape(plain), Shape(page.GetProperty("areas")[0].GetProperty("items")[0]));
        Assert.Equal(Shape(withBreakdown), Shape(page.GetProperty("areas")[1].GetProperty("items")[0]));
        Assert.Equal(Shape(withBreakdown.GetProperty("breakdown")[0]), Shape(page.GetProperty("areas")[1].GetProperty("items")[0].GetProperty("breakdown")[0]));
    }
}
