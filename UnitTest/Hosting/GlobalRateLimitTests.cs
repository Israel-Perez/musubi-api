using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Musubi.Api.Platform.Auth;
using Musubi.Api.Platform.Hosting;

namespace UnitTest.Hosting;

/// <summary>SCRUM-125: como reparte el tope global (persona, IP o ninguno) y con que limite.</summary>
public sealed class GlobalRateLimitTests
{
    private static readonly GlobalRateLimit.Limits Limits = new(UserPermitLimit: 3, AnonymousPermitLimit: 7, TimeSpan.FromMinutes(1));

    private static DefaultHttpContext Request(string path, string ip = "203.0.113.9", string? subject = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (subject is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(MusubiClaims.Subject, subject)], authenticationType: "Bearer"));
        }

        return context;
    }

    private static int Permits(RateLimitPartition<string> partition)
    {
        using var limiter = partition.Factory(partition.PartitionKey);
        var permits = 0;
        while (permits < 100)
        {
            using var lease = limiter.AttemptAcquire();
            if (!lease.IsAcquired)
            {
                break;
            }

            permits++;
        }

        return permits;
    }

    [Fact]
    public void SignedIn_IsPartitionedByPerson_WithTheUserLimit()
    {
        var partition = GlobalRateLimit.Partition(Request("/api/v1/products", subject: "abc"), Limits);

        Assert.Equal("user:abc", partition.PartitionKey);
        Assert.Equal(3, Permits(partition));
    }

    [Fact]
    public void Anonymous_IsPartitionedByIp_WithTheAnonymousLimit()
    {
        var partition = GlobalRateLimit.Partition(Request("/api/v1/products"), Limits);

        Assert.Equal("ip:203.0.113.9", partition.PartitionKey);
        Assert.Equal(7, Permits(partition));
    }

    [Fact]
    public void AnUnauthenticatedIdentity_WithASubject_StillCountsByIp()
    {
        var context = Request("/api/v1/products");
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(MusubiClaims.Subject, "inventado")]));

        Assert.Equal("ip:203.0.113.9", GlobalRateLimit.Partition(context, Limits).PartitionKey);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    [InlineData("/HEALTH")]
    public void HealthProbes_HaveNoLimit(string path) =>
        Assert.Equal(100, Permits(GlobalRateLimit.Partition(Request(path), Limits)));

    [Fact]
    public void ARouteThatOnlyStartsLikeHealth_IsLimited() =>
        Assert.Equal(7, Permits(GlobalRateLimit.Partition(Request("/healthz"), Limits)));
}
