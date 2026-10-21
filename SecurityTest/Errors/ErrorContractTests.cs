using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecurityTest.Common;

namespace SecurityTest.Errors;

/// <summary>02-03 / RNF-18: cada error es problem+json con code estable, titulo en espanol y traceId, sin detalle interno.</summary>
public sealed class ErrorContractTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct).ConfigureAwait(false));
        var root = doc.RootElement.Clone();
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.False(root.TryGetProperty("detail", out _));
        return root;
    }

    [Fact]
    public async Task InvalidInput_Returns400WithCodeAndFieldErrors()
    {
        using var client = production.CreateClient();

        using var response = await client.PostAsJsonAsync(new Uri("/api/v1/test-auth/validate", UriKind.Relative), new { email = "no-es-correo" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal("validation.failed", problem.GetProperty("code").GetString());
        Assert.Equal("Revisa los datos enviados.", problem.GetProperty("title").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("Email", out _));
    }

    [Fact]
    public async Task BusinessError_KeepsItsOwnCodeStatusAndMessage()
    {
        using var client = production.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/test-auth/business-error", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal("auth.invalid_credentials", problem.GetProperty("code").GetString());
        Assert.StartsWith("El correo o la contraseña no coinciden", problem.GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnhandledException_InProduction_IsGenericServerError()
    {
        using var client = production.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/test-probe/throw", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await ProblemAsync(response);
        Assert.Equal("server.error", problem.GetProperty("code").GetString());
        Assert.Equal("Ocurrió un error inesperado. Inténtalo más tarde.", problem.GetProperty("title").GetString());
    }
}
