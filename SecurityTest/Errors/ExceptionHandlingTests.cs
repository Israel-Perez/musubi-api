using System.Net;
using SecurityTest.Common;

namespace SecurityTest.Errors;

/// <summary>01-03: fuera de Development las excepciones salen como ProblemDetails generico, sin detalle interno.</summary>
public sealed class ExceptionHandlingTests(ProductionApiFactory production, DevelopmentApiFactory development)
    : IClassFixture<ProductionApiFactory>, IClassFixture<DevelopmentApiFactory>
{
    private static readonly Uri ProbeUri = new("/api/v1/test-probe/throw", UriKind.Relative);

    [Fact]
    public async Task Production_UnhandledException_ReturnsGenericProblemWithoutDetails()
    {
        using var client = production.CreateClient();

        var response = await client.GetAsync(ProbeUri, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(ThrowingProbeController.SensitiveMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_UnhandledException_ShowsDeveloperDetails()
    {
        // Grupo control: confirma que el probe si lanza y que el ocultamiento depende del ambiente.
        using var client = development.CreateClient();

        var response = await client.GetAsync(ProbeUri, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("InvalidOperationException", body, StringComparison.Ordinal);
    }
}
