using System.Text.Json;

namespace ContractTest;

/// <summary>
/// Contrato que consume musubi-app: la forma JSON (nombres camelCase y tipos) de cada respuesta
/// coincide con su fixture versionado en <c>Fixtures/</c>. Cambiar un fixture = cambio de contrato
/// (actualizar docs/endpoints.md y avisar al front).
/// </summary>
public sealed class PingContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
{
    [Fact]
    public async Task PingV1_MatchesFixtureShape()
    {
        using var client = factory.CreateClient();
        var body = await client.GetStringAsync(new Uri("/api/v1/ping", UriKind.Relative), TestContext.Current.CancellationToken);
        var fixture = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ping.json"), TestContext.Current.CancellationToken);

        using var actual = JsonDocument.Parse(body);
        using var expected = JsonDocument.Parse(fixture);

        Assert.Equal(Shape(expected.RootElement), Shape(actual.RootElement));
    }

    [Fact]
    public async Task PingV1_PropertyNamesAreCamelCase()
    {
        using var client = factory.CreateClient();
        var body = await client.GetStringAsync(new Uri("/api/v1/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        using var doc = JsonDocument.Parse(body);

        Assert.All(doc.RootElement.EnumerateObject(), p => Assert.True(char.IsLower(p.Name[0]), $"'{p.Name}' no es camelCase."));
    }

    private static List<string> Shape(JsonElement element) =>
        [.. element.EnumerateObject().Select(p => $"{p.Name}:{p.Value.ValueKind}").Order(StringComparer.Ordinal)];
}
