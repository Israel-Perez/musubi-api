using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Email;
using Musubi.TestShared;

namespace ContractTest;

/// <summary>
/// HU-01 / HU-03: formas JSON de /api/v1/auth que consume musubi-app (fixtures en Fixtures/).
/// Cambiar un fixture = cambio de contrato: actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class AuthContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
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

    [Fact]
    public async Task CampusRegisterAndSession_MatchTheirFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        using var client = app.CreateClient();
        var email = $"ana.{Guid.NewGuid():N}@uttt.edu.mx";

        var campus = await client.PostAsJsonAsync(new Uri("/api/v1/auth/campus", UriKind.Relative), new { email }, Ct);
        var register = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = "Ana Sofía Ramírez", password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct);
        var confirm = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative),
            new { email, code = emails.LastCodeFor(email) },
            Ct);

        Assert.Equal(Shape(await FixtureAsync("campus.json")), Shape(await BodyAsync(campus)));
        Assert.Equal(Shape(await FixtureAsync("register.json")), Shape(await BodyAsync(register)));
        Assert.Equal(Shape(await FixtureAsync("session.json")), Shape(await BodyAsync(confirm)));
    }
}
