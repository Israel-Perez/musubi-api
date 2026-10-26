using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Musubi.Api.Platform.Hosting;
using SecurityTest.Common;

namespace SecurityTest.Hosting;

/// <summary>Production detras de CloudFront (02-05): confia en las cabeceras de CloudFront.</summary>
public sealed class CloudFrontApiFactory : SecurityApiFactory
{
    public CloudFrontApiFactory()
        : base("Production")
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [CloudFrontForwardedHeaders.TrustKey] = "true",
        }));
    }
}

/// <summary>02-05 / OWASP API4: el rate limit es por cliente real detras de CloudFront, y la cabecera no se puede falsificar fuera de la nube.</summary>
public sealed class CloudFrontHeadersTests
{
    private static readonly Uri LoginProbe = new("/api/v1/test-auth/login", UriKind.Relative);

    private static async Task<HttpStatusCode> PostAsync(HttpClient client, string viewerAddress)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginProbe);
        request.Headers.Add(CloudFrontForwardedHeaders.ViewerAddressHeader, viewerAddress);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
        return response.StatusCode;
    }

    [Fact]
    public async Task BehindCloudFront_EachViewerHasItsOwnRateLimit()
    {
        using var factory = new CloudFrontApiFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < SecurityApiFactory.AuthPermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await PostAsync(client, "198.51.100.10:46532"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await PostAsync(client, "198.51.100.10:50001"));
        // Otro estudiante (otra IP, tambien IPv6) no paga los intentos del primero.
        Assert.Equal(HttpStatusCode.OK, await PostAsync(client, "2001:db8::1:47234"));
    }

    [Fact]
    public async Task WithoutTheSetting_TheHeaderIsIgnored()
    {
        // Fuera de la nube nadie garantiza que la cabecera venga de CloudFront: cambiarla no da intentos nuevos.
        using var factory = new ProductionApiFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < SecurityApiFactory.AuthPermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await PostAsync(client, $"198.51.100.{i + 1}:46532"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await PostAsync(client, "203.0.113.99:46532"));
    }

    [Theory]
    [InlineData("198.51.100.10:46532", "198.51.100.10")]
    [InlineData("2001:db8:85a3::8a2e:370:7334:46532", "2001:db8:85a3::8a2e:370:7334")]
    public void ViewerAddress_SplitsIpAndPort(string header, string expected)
    {
        Assert.True(CloudFrontForwardedHeaders.TryParseViewerAddress(header, out var address));
        Assert.Equal(IPAddress.Parse(expected), address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-ip:80")]
    [InlineData(":80")]
    public void ViewerAddress_RejectsGarbage(string? header)
    {
        Assert.False(CloudFrontForwardedHeaders.TryParseViewerAddress(header, out _));
    }

    [Theory]
    [InlineData("https", "https")]
    [InlineData("HTTP", "http")]
    [InlineData("ftp", "http")]
    public void ForwardedProto_SetsTheScheme(string header, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Headers[CloudFrontForwardedHeaders.ForwardedProtoHeader] = header;

        CloudFrontForwardedHeaders.Apply(context);

        Assert.Equal(expected, context.Request.Scheme);
    }
}
