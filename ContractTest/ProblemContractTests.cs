using System.Net;
using System.Text.Json;

namespace ContractTest;

/// <summary>
/// 02-03: forma de cada error que recibe musubi-app (problem+json con <c>code</c> y <c>traceId</c>).
/// El front traduce por <c>code</c> (catalogo en docs/errores.md); cambiar esta forma rompe a la app.
/// </summary>
public sealed class ProblemContractTests(ContractApiFactory factory) : IClassFixture<ContractApiFactory>
{
    [Fact]
    public async Task Unauthorized_MatchesProblemFixtureShape()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/requiere-sesion", UriKind.Relative), TestContext.Current.CancellationToken);
        var fixture = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "problem.json"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var actual = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var expected = JsonDocument.Parse(fixture);
        Assert.Equal(Shape(expected.RootElement), Shape(actual.RootElement));
        Assert.Equal("auth.unauthorized", actual.RootElement.GetProperty("code").GetString());
    }

    private static List<string> Shape(JsonElement element) =>
        [.. element.EnumerateObject().Select(p => $"{p.Name}:{p.Value.ValueKind}").Order(StringComparer.Ordinal)];
}
