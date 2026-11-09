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
/// HU-20 y HU-22: formas JSON de /api/v1/conversations que consume musubi-app. <c>message.json</c> es tambien la
/// carga del evento <c>messageCreated</c> del hub. Cambiar un fixture = cambio de contrato: actualizar
/// docs/endpoints.md y avisar al front.
/// </summary>
public sealed class ChatContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
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

    private static async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> app, RecordingEmailSender emails)
    {
        var client = app.CreateClient();
        var email = $"ana.{Guid.NewGuid():N}@uttt.edu.mx";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Valeria Cruz", password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        var confirmed = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await BodyAsync(confirmed).ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task ConversationMessageMessagePageAndInbox_MatchTheirFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        using var seller = await SignedInAsync(app, emails);
        using var buyer = await SignedInAsync(app, emails);
        using var form = new MultipartFormDataContent();
        using var file = Png();
        form.Add(file, "photo", "foto.png");
        var photo = await BodyAsync(await seller.PostAsync(new Uri("/api/v1/products/photos", UriKind.Relative), form, Ct));
        var product = await BodyAsync(await seller.PostAsJsonAsync(
            new Uri("/api/v1/products", UriKind.Relative),
            new
            {
                title = "Calculadora Casio fx-991ES Plus",
                description = "La usé un semestre.",
                category = "calculadoras",
                priceCents = 45_000,
                condition = "used",
                conditionDetail = "good",
                stock = 1,
                photos = new[] { photo.GetProperty("key").GetString() },
            },
            Ct));

        var started = await buyer.PostAsJsonAsync(new Uri("/api/v1/conversations", UriKind.Relative), new { productId = product.GetProperty("id").GetString() }, Ct);
        var conversation = await BodyAsync(started);
        var id = conversation.GetProperty("id").GetString();
        var sent = await buyer.PostAsJsonAsync(
            new Uri($"/api/v1/conversations/{id}/messages", UriKind.Relative), new { text = "¡Hola, Valeria! ¿Sigue disponible la calculadora?" }, Ct);
        var page = await BodyAsync(await seller.GetAsync(new Uri($"/api/v1/conversations/{id}/messages", UriKind.Relative), Ct));
        var pageFixture = await FixtureAsync("message-page.json");
        var inbox = await BodyAsync(await seller.GetAsync(new Uri("/api/v1/conversations", UriKind.Relative), Ct));
        var inboxFixture = await FixtureAsync("inbox-page.json");

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.Equal(Shape(await FixtureAsync("conversation.json")), Shape(conversation));
        Assert.Equal(Shape(await FixtureAsync("message.json")), Shape(await BodyAsync(sent)));
        Assert.Equal(Shape(pageFixture), Shape(page));
        Assert.Equal(Shape(pageFixture.GetProperty("items")[0]), Shape(page.GetProperty("items")[0]));
        Assert.Equal(Shape(inboxFixture), Shape(inbox));
        Assert.Equal(Shape(inboxFixture.GetProperty("items")[0]), Shape(inbox.GetProperty("items")[0]));
    }
}
