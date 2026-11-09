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
/// HU-13: forma JSON de /api/v1/wanted que consume musubi-app. Cambiar el fixture = cambio de contrato:
/// actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class WantedContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
{
    private static readonly string[] Features = ["Modelo fx-991ES Plus", "Con tapa"];

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
}
