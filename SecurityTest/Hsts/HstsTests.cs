using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecurityTest.Common;

namespace SecurityTest.Hsts;

/// <summary>01-02 / RNF-02: HSTS 365 dias solo fuera de Development.</summary>
public sealed class HstsTests(ProductionApiFactory production, DevelopmentApiFactory development)
    : IClassFixture<ProductionApiFactory>, IClassFixture<DevelopmentApiFactory>
{
    // HSTS excluye localhost por diseno: se usa un host no local para observarlo en el wire.
    private static readonly Uri HttpsBase = new("https://api.musubi.test");

    [Fact]
    public void Production_HstsOptions_Are365DaysWithSubdomains()
    {
        var options = production.Services.GetRequiredService<IOptions<HstsOptions>>().Value;

        Assert.Equal(TimeSpan.FromDays(365), options.MaxAge);
        Assert.True(options.IncludeSubDomains);
    }

    [Fact]
    public async Task Production_HttpsResponse_CarriesHstsHeader()
    {
        using var client = production.CreateClient(new() { BaseAddress = HttpsBase });

        var response = await client.GetAsync(new Uri("/api/v1/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.True(response.Headers.TryGetValues("Strict-Transport-Security", out var values));
        Assert.Contains("max-age=31536000", string.Join(';', values), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Development_HttpsResponse_HasNoHstsHeader()
    {
        using var client = development.CreateClient(new() { BaseAddress = HttpsBase });

        var response = await client.GetAsync(new Uri("/api/v1/ping", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }
}
