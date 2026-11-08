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
/// HU-28 y HU-57: formas JSON de /api/v1/reports y /api/v1/blocks que consume musubi-app. Cambiar un fixture =
/// cambio de contrato: actualizar docs/endpoints.md y avisar al front.
/// </summary>
public sealed class ReputationContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
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

    private static async Task<(HttpClient Client, string Id)> SignedInAsync(WebApplicationFactory<Program> app, RecordingEmailSender emails, string name)
    {
        var client = app.CreateClient();
        var email = $"ana.{Guid.NewGuid():N}@uttt.edu.mx";
        await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/register", UriKind.Relative),
            new { email, fullName = name, password = "musubi2026", acceptTerms = true, acceptPrivacy = true },
            Ct).ConfigureAwait(false);
        var confirmed = await client.PostAsJsonAsync(
            new Uri("/api/v1/auth/verification/confirm", UriKind.Relative), new { email, code = emails.LastCodeFor(email) }, Ct).ConfigureAwait(false);
        var session = await BodyAsync(confirmed).ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var me = await BodyAsync(await client.GetAsync(new Uri("/api/v1/profiles/me", UriKind.Relative), Ct).ConfigureAwait(false)).ConfigureAwait(false);
        return (client, me.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task ReportAndBlockedUsers_MatchTheirFixtures()
    {
        var emails = new RecordingEmailSender();
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(emails)));
        var valeria = await SignedInAsync(app, emails, "Valeria Cruz");
        var diego = await SignedInAsync(app, emails, "Diego Hernández");
        using var me = valeria.Client;
        using var other = diego.Client;
        var otherId = diego.Id;

        var reported = await me.PostAsJsonAsync(
            new Uri("/api/v1/reports", UriKind.Relative),
            new { targetType = "user", targetId = otherId, reason = "off_platform", details = "Me pidió depositarle antes de vernos." },
            Ct);
        var blocked = await me.PostAsJsonAsync(new Uri("/api/v1/blocks", UriKind.Relative), new { userId = otherId }, Ct);
        var list = await BodyAsync(await me.GetAsync(new Uri("/api/v1/blocks", UriKind.Relative), Ct));
        var listFixture = await FixtureAsync("blocked-users.json");

        Assert.Equal(HttpStatusCode.Created, reported.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
        Assert.Equal(Shape(await FixtureAsync("report.json")), Shape(await BodyAsync(reported)));
        Assert.Equal(Shape(listFixture), Shape(list));
        Assert.Equal(Shape(listFixture.GetProperty("items")[0]), Shape(list.GetProperty("items")[0]));
    }
}
