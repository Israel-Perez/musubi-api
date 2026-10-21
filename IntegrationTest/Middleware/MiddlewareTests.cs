using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using IntegrationTest.Common;
using Microsoft.Extensions.DependencyInjection;
using Musubi.Api.Platform.Auth;

namespace IntegrationTest.Middleware;

/// <summary>01-02: comportamiento del pipeline en Development.</summary>
public sealed class MiddlewareTests(MusubiApiFactory factory) : IClassFixture<MusubiApiFactory>
{
    [Fact]
    public async Task Development_DoesNotSendHsts()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Development_ExposesOpenApiDocumentV1()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/api/v1/ping", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRoute_Anonymous_Returns401SoRoutesCannotBeEnumerated()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/no-existe", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_WithSession_Returns404ProblemWithCode()
    {
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<AccessTokenIssuer>().Issue("6716a0000000000000000001", "000000000000000000000001", ["student"]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var response = await client.GetAsync(new Uri("/no-existe", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("resource.not_found", doc.RootElement.GetProperty("code").GetString());
    }
}
