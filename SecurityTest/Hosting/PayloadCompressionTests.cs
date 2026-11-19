using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SecurityTest.Common;

namespace SecurityTest.Hosting;

/// <summary>
/// SCRUM-120: la API comprime (Brotli o Gzip) cuando el cliente lo pide, tambien los errores, pero nunca las
/// respuestas de <c>/auth</c>, que llevan tokens y aceptan datos del cliente (BREACH).
/// </summary>
public sealed class PayloadCompressionTests(ProductionApiFactory production) : IClassFixture<ProductionApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string encoding, object? body = null)
    {
        using var client = production.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Accept-Encoding", encoding);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request, Ct).ConfigureAwait(false);
    }

    [Fact]
    public async Task Brotli_WhenTheClientAcceptsIt_AndTheBodyIsStillTheSame()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/ping", "br, gzip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["br"], response.Content.Headers.ContentEncoding);
        using var brotli = new BrotliStream(await response.Content.ReadAsStreamAsync(Ct), CompressionMode.Decompress);
        using var json = await JsonDocument.ParseAsync(brotli, cancellationToken: Ct);
        Assert.Equal("pong", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Gzip_WhenTheClientOnlyAcceptsGzip()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/ping", "gzip");

        Assert.Equal(["gzip"], response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task ErrorsAreCompressedToo()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/test-auth/protected", "gzip");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(["gzip"], response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task WithoutAcceptEncoding_TheResponseIsPlain()
    {
        var response = await SendAsync(HttpMethod.Get, "/api/v1/ping", "identity");

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task SessionRoutes_AreNeverCompressed()
    {
        var login = await SendAsync(HttpMethod.Post, "/api/v1/auth/login", "br, gzip", new { email = "nadie@uttt.edu.mx", password = "musubi2026" });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Empty(login.Content.Headers.ContentEncoding);
        Assert.Contains("auth.invalid_credentials", await login.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
