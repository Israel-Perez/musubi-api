using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;

namespace ContractTest;

/// <summary>
/// HU-13 y HU-15: forma JSON de /api/v1/wanted (publicar, "Mis solicitudes" y el tablero) que consume musubi-app. Cambiar el fixture = cambio de contrato:
/// actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class WantedContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
{
    private static readonly string[] Features = ["Modelo fx-991ES Plus", "Con tapa"];

    private static readonly string[] Size = ["Talla M"];

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

    private static async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> app, RecordingEmailSender emails)
    {
        var client = app.CreateClient();
        var email = $"ana.{Guid.NewGuid():N}@uttt.edu.mx";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Ana Sofía Ramírez", password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        var confirmed = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await BodyAsync(confirmed).ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task WantedAndMine_MatchTheFixture()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        using var ana = await SignedInAsync(app, emails);
        var neededBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var published = await ana.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new
            {
                title = "Calculadora Casio fx-991ES Plus",
                category = "calculadoras",
                description = "La necesito para Cálculo Integral y Física.",
                features = Features,
                budgetCents = 60_000,
                quantity = 1,
                neededBy,
            },
            Ct);
        var mine = await BodyAsync(await ana.GetAsync(new Uri("/api/v1/wanted/mine", UriKind.Relative), Ct));
        var fixture = await FixtureAsync("wanted.json");

        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        Assert.Equal(Shape(fixture), Shape(await BodyAsync(published)));
        Assert.Equal(Shape(fixture), Shape(mine.GetProperty("items")[0]));
    }

    [Fact]
    public async Task Board_MatchesTheFixture_WithSessionAndAsAVisitor()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        using var ana = await SignedInAsync(app, emails);
        using var diego = await SignedInAsync(app, emails);
        using var visitor = app.CreateClient();
        var neededBy = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        await ana.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Bata de laboratorio talla M", category = "laboratorio", description = "De algodón.", features = Size, budgetCents = 30_000, quantity = 1, neededBy },
            Ct);
        var fixture = await FixtureAsync("wanted-page.json");

        var member = await BodyAsync(await diego.GetAsync(new Uri("/api/v1/wanted?category=laboratorio", UriKind.Relative), Ct));
        var guest = await BodyAsync(await visitor.GetAsync(new Uri("/api/v1/wanted?category=laboratorio", UriKind.Relative), Ct));

        Assert.Equal(Shape(fixture), Shape(member));
        Assert.Equal(Shape(fixture.GetProperty("items")[0]), Shape(member.GetProperty("items")[0]));
        // Visitante: misma forma, con id en null (RNF-05).
        var guestItem = guest.GetProperty("items")[0];
        Assert.Equal(
            Shape(fixture.GetProperty("items")[0]).Select(p => p == "requester.id:String" ? "requester.id:Null" : p),
            Shape(guestItem));
    }

    [Fact]
    public async Task Offer_MatchesTheFixture()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        using var fernanda = await SignedInAsync(app, emails);
        using var valeria = await SignedInAsync(app, emails);
        var request = await BodyAsync(await fernanda.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Bata de laboratorio talla M", category = "laboratorio", budgetCents = 30_000, quantity = 1 },
            Ct));
        var fixture = await FixtureAsync("offer.json");

        var sent = await valeria.PutAsJsonAsync(
            new Uri($"/api/v1/wanted/{request.GetProperty("id").GetString()}/offers/mine", UriKind.Relative),
            new { description = "Bata blanca de algodón, talla M", priceCents = 28_000, condition = "used", message = "Te la llevo." },
            Ct);
        var body = await BodyAsync(sent);

        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        // Oferta directa sin foto ni publicacion: productId y photo van en null.
        Assert.Equal(
            Shape(fixture).Select(p => p is "productId:String" or "photo:String" ? p.Replace("String", "Null", StringComparison.Ordinal) : p),
            Shape(body));
    }

    [Fact]
    public async Task ReceivedOffers_AndAccept_MatchTheFixture()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        using var fernanda = await SignedInAsync(app, emails);
        using var valeria = await SignedInAsync(app, emails);
        var requestId = (await BodyAsync(await fernanda.PostAsJsonAsync(
            new Uri("/api/v1/wanted", UriKind.Relative),
            new { title = "Bata de laboratorio talla M", category = "laboratorio", budgetCents = 30_000, quantity = 1 },
            Ct))).GetProperty("id").GetString();
        var offerId = (await BodyAsync(await valeria.PutAsJsonAsync(
            new Uri($"/api/v1/wanted/{requestId}/offers/mine", UriKind.Relative),
            new { description = "Bata blanca de algodón, talla M", priceCents = 28_000, condition = "used", message = "Te la llevo." },
            Ct))).GetProperty("id").GetString();
        var fixture = await FixtureAsync("received-offers.json");

        var list = await BodyAsync(await fernanda.GetAsync(new Uri($"/api/v1/wanted/{requestId}/offers", UriKind.Relative), Ct));
        var accepted = await BodyAsync(await fernanda.PostAsync(new Uri($"/api/v1/wanted/{requestId}/offers/{offerId}/accept", UriKind.Relative), null, Ct));

        // Oferta directa, sin publicacion ni foto, y quien ofrece aun sin perfil, carrera ni calificaciones.
        string[] nulls = ["productId", "conditionDetail", "photo", "offerer.photo", "offerer.career", "offerer.ratingAvg"];
        Assert.Equal(
            Shape(fixture.GetProperty("items")[0]).Select(p => nulls.Any(n => p.StartsWith(n + ":", StringComparison.Ordinal)) ? p[..p.IndexOf(':', StringComparison.Ordinal)] + ":Null" : p),
            Shape(list.GetProperty("items")[0]));
        Assert.Equal(["conversationId:String"], Shape(accepted));
    }
}
